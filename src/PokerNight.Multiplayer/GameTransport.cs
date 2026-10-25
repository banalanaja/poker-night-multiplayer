using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace PokerNight.Multiplayer;

// A session transports the live engine state. Poker rules and bot decisions remain in the host game.
public sealed class GameTransport : IAsyncDisposable
{
    private sealed class Peer(TcpClient socket)
    {
        public Guid Id { get; } = Guid.NewGuid();
        public TcpClient Socket { get; } = socket;
        public bool Joined { get; set; }
        private readonly SemaphoreSlim send = new(1);
        public async Task Send(WireMessage value, CancellationToken token)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            bool acquired = false;
            try
            {
                await send.WaitAsync(timeout.Token); acquired = true;
                await WireCodec.WriteAsync(Socket.GetStream(), value, timeout.Token);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new IOException("Peer send timeout."); }
            finally { if (acquired) send.Release(); }
        }
    }
    private readonly TcpListener listener;
    private readonly FileGameRuntime runtime;
    private readonly Lobby lobby;
    private readonly ActionAuthority authority = new();
    private readonly Dictionary<Guid, Peer> peers = new();
    private readonly object gate = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly List<Task> clients = new();
    private HostTableFrame? current;
    private long lastFrame = -1;
    private long hostActionSequence;
    private long disconnectedSequence;
    private bool started;
    private long lastHand;
    private readonly HashSet<Seat> disconnected = new();
    private readonly int expectedHumans;
    public IPEndPoint Endpoint => (IPEndPoint)listener.LocalEndpoint;
    public LobbyState Snapshot => lobby.Snapshot();

    public GameTransport(IPAddress address, int port, string gameSha256, string hostName, Seat hostSeat,
        FileGameRuntime runtime, Guid? sessionId = null, int expectedHumans = 0)
    {
        if (expectedHumans != 0 && expectedHumans is < 2 or > 5) throw new ArgumentOutOfRangeException(nameof(expectedHumans));
        this.expectedHumans = expectedHumans;
        this.runtime = runtime;
        Guid host = Guid.NewGuid();
        lobby = new(gameSha256, host, sessionId);
        lobby.Join(host, new(WireCodec.Version, gameSha256, hostName));
        lobby.SelectSeat(host, hostSeat); lobby.SetReady(host, true);
        runtime.Configure(new(lobby.SessionId, "host", hostSeat, new() { [hostSeat] = true }));
        listener = new(address, port); listener.Start();
    }

    public bool TryStart()
    {
        lock (gate)
        {
            if (started) return false;
            var snapshot = lobby.Snapshot();
            var people = snapshot.Seats.Where(x => !x.IsBot).ToArray();
            if (people.Length < Math.Max(2, expectedHumans) || people.Any(x => !x.Ready)) return false;
            lobby.Start(lobby.HostConnectionId);
            var hostSeat = lobby.FindSeat(lobby.HostConnectionId)!.Value;
            runtime.Configure(new(lobby.SessionId, "host", hostSeat, people.ToDictionary(x => x.Seat, _ => true)));
            runtime.Start(lobby.SessionId);
            started = true;
            return true;
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        Task pump = Pump(linked.Token);
        Task stopOnPump = pump.ContinueWith(_ => linked.Cancel(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        try
        {
            while (!linked.IsCancellationRequested)
            {
                var socket = await listener.AcceptTcpClientAsync(linked.Token);
                lock (gate)
                {
                    if (peers.Count >= 4) { socket.Dispose(); continue; }
                    var peer = new Peer(socket); peers.Add(peer.Id, peer);
                    clients.RemoveAll(x => x.IsCompleted); clients.Add(Serve(peer, linked.Token));
                }
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        finally
        {
            linked.Cancel(); listener.Stop();
            lock (gate) foreach (var peer in peers.Values) peer.Socket.Dispose();
            await Task.WhenAll(clients.Append(pump).Append(stopOnPump));
            runtime.End(lobby.SessionId);
        }
    }

    private async Task Serve(Peer peer, CancellationToken token)
    {
        try
        {
            peer.Socket.NoDelay = true;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            var hello = await WireCodec.ReadAsync(peer.Socket.GetStream(), deadline.Token);
            if (hello?.Type != "hello") return;
            lobby.Join(peer.Id, hello.Read<Hello>());
            await peer.Send(WireMessage.From("welcome", new Welcome(lobby.SessionId, peer.Id)), token);
            lock (gate) peer.Joined = true;
            await Broadcast(WireMessage.From("lobby", lobby.Snapshot()), token);
            while (await WireCodec.ReadAsync(peer.Socket.GetStream(), token) is { } message)
            {
                long? requestedTurn = null;
                try
                {
                    switch (message.Type)
                    {
                        case "selectSeat": lobby.SelectSeat(peer.Id, message.Read<SeatRequest>().Seat); break;
                        case "ready": lobby.SetReady(peer.Id, message.Read<ReadyRequest>().Ready); break;
                        case "action":
                            if (lobby.FindSeat(peer.Id) is not { } seat) throw new InvalidOperationException();
                            var action = message.Read<PlayerAction>(); requestedTurn = action.TurnId;
                            Accept(peer.Id, seat, action);
                            continue;
                        default: throw new InvalidOperationException();
                    }
                    await Broadcast(WireMessage.From("lobby", lobby.Snapshot()), token);
                    if (expectedHumans > 0 && TryStart()) await Broadcast(WireMessage.From("lobby", lobby.Snapshot()), token);
                }
                catch (InvalidOperationException)
                {
                    await peer.Send(WireMessage.From("error", new ProtocolError(message.Type == "selectSeat" ? "seat_unavailable" : "invalid_request", "Pedido inválido para o estado atual.", requestedTurn)), token);
                }
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or SocketException or JsonException or OperationCanceledException or ObjectDisposedException) { }
        finally
        {
            lock (gate)
            {
                if (started && lobby.FindSeat(peer.Id) is { } seat) disconnected.Add(seat);
                peers.Remove(peer.Id);
            }
            lobby.Disconnect(peer.Id); peer.Socket.Dispose();
        }
    }

    private void Accept(Guid connection, Seat seat, PlayerAction action)
    {
        lock (gate)
        {
            if (!started || current is null || !authority.TryAccept(connection, seat, action, current.Public, out _))
                throw new InvalidOperationException("Ação recusada.");
            runtime.SendAction(seat, action);
        }
    }

    private async Task Pump(CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
            while (await timer.WaitForNextTickAsync(token))
            {
                if (runtime.EndRequested(lobby.SessionId)) { lifetime.Cancel(); return; }
                var frame = runtime.ReadHostFrame();
                if (!started || frame is null || frame.Public.SessionId != lobby.SessionId) continue;
                lock (gate) current = frame;
                if (frame.Public.HandId != lastHand)
                {
                    lock (gate)
                    {
                        lobby.HandFinished();
                        if (disconnected.Count > 0)
                        {
                            var seats = lobby.Snapshot().Seats.Where(x => !x.IsBot).ToDictionary(x => x.Seat, _ => true);
                            runtime.Configure(new(lobby.SessionId, "host", lobby.FindSeat(lobby.HostConnectionId)!.Value, seats));
                            disconnected.Clear();
                        }
                        lastHand = frame.Public.HandId;
                    }
                }
                if (frame.Public.ActiveSeat is { } active)
                {
                    lock (gate)
                    {
                        if (disconnected.Contains(active))
                        {
                            var action = new PlayerAction(lobby.SessionId, frame.Public.HandId, frame.Public.TurnId,
                                ++disconnectedSequence, ActionAuthority.DisconnectedAction(frame.Public.ToCall));
                            if (authority.TryAccept(Guid.Empty, active, action, frame.Public, out _)) runtime.SendAction(active, action);
                        }
                    }
                }
                if (runtime.ReadLocalAction() is { } local && local.SessionId == lobby.SessionId && local.Sequence > hostActionSequence)
                {
                    hostActionSequence = local.Sequence;
                    if (lobby.FindSeat(lobby.HostConnectionId) == local.Seat)
                    {
                        try { Accept(lobby.HostConnectionId, local.Seat, local.ToPlayerAction()); }
                        catch (InvalidOperationException) { runtime.ActionError(lobby.SessionId, local.TurnId); }
                    }
                }
                if (frame.Public.Sequence <= lastFrame) continue;
                lastFrame = frame.Public.Sequence;
                var names = lobby.Snapshot().Seats.ToDictionary(x => x.Seat, x => x.IsBot ? "Bot" : x.Nickname);
                if (lobby.FindSeat(lobby.HostConnectionId) is { } hostSeat)
                {
                    var own = PrivacyProjection.ForSeat(frame, hostSeat);
                    runtime.Show(new(own.Public, own.Private, names));
                }
                foreach (var peer in JoinedPeers())
                {
                    if (lobby.FindSeat(peer.Id) is not { } seat) continue;
                    var view = PrivacyProjection.ForSeat(frame, seat);
                    try
                    {
                        await peer.Send(WireMessage.From("publicTable", view.Public), token);
                        await peer.Send(WireMessage.From("privateHand", view.Private), token);
                    }
                    catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException) { peer.Socket.Dispose(); }
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private Peer[] JoinedPeers() { lock (gate) return [.. peers.Values.Where(x => x.Joined)]; }
    private async Task Broadcast(WireMessage message, CancellationToken token)
    {
        foreach (var peer in JoinedPeers())
        {
            try { await peer.Send(message, token); }
            catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException) { peer.Socket.Dispose(); }
        }
    }
    public ValueTask DisposeAsync() { lifetime.Cancel(); listener.Stop(); return ValueTask.CompletedTask; }
}
