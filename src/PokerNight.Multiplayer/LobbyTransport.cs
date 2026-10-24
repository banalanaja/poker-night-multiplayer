using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace PokerNight.Multiplayer;

// Network diagnostics only. This lobby deliberately has no command that starts an unverified game adapter.
public sealed class LobbyTransport : IAsyncDisposable
{
    private sealed class Peer(TcpClient client) : IAsyncDisposable
    {
        public Guid Id { get; } = Guid.NewGuid();
        public TcpClient Client { get; } = client;
        public bool Joined { get; set; }
        private readonly SemaphoreSlim sendLock = new(1);
        public async Task Send(WireMessage message, CancellationToken token)
        {
            await sendLock.WaitAsync(token);
            try { await WireCodec.WriteAsync(Client.GetStream(), message, token); }
            finally { sendLock.Release(); }
        }
        public ValueTask DisposeAsync() { Client.Dispose(); return ValueTask.CompletedTask; }
    }
    private readonly TcpListener listener;
    private readonly Lobby lobby;
    private readonly Dictionary<Guid, Peer> peers = new();
    private readonly object gate = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly List<Task> clients = new();
    public IPEndPoint Endpoint => (IPEndPoint)listener.LocalEndpoint;
    public LobbyState Snapshot => lobby.Snapshot();

    public LobbyTransport(IPAddress address, int port, string gameSha256, string hostName, Seat hostSeat)
    {
        Guid host = Guid.NewGuid();
        lobby = new(gameSha256, host);
        lobby.Join(host, new(WireCodec.Version, gameSha256, hostName));
        lobby.SelectSeat(host, hostSeat);
        lobby.SetReady(host, true);
        listener = new(address, port);
        listener.Start();
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        try
        {
            while (!linked.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(linked.Token);
                bool full;
                lock (gate) full = peers.Count >= 4;
                if (full) { client.Dispose(); continue; }
                var peer = new Peer(client);
                lock (gate) peers.Add(peer.Id, peer);
                clients.RemoveAll(t => t.IsCompleted);
                clients.Add(Serve(peer, linked.Token));
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        finally
        {
            listener.Stop();
            Peer[] active;
            lock (gate) active = [.. peers.Values];
            foreach (var peer in active) await peer.DisposeAsync();
            await Task.WhenAll(clients);
        }
    }

    private async Task Serve(Peer peer, CancellationToken token)
    {
        try
        {
            peer.Client.NoDelay = true;
            using var helloTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            helloTimeout.CancelAfter(TimeSpan.FromSeconds(10));
            var hello = await WireCodec.ReadAsync(peer.Client.GetStream(), helloTimeout.Token);
            if (hello?.Type != "hello") throw new InvalidDataException("Handshake obrigatório.");
            lobby.Join(peer.Id, hello.Read<Hello>());
            await peer.Send(WireMessage.From("welcome", new Welcome(lobby.SessionId, peer.Id)), token);
            lock (gate) peer.Joined = true;
            await Broadcast(token);
            while (await WireCodec.ReadAsync(peer.Client.GetStream(), token) is { } message)
            {
                try
                {
                    switch (message.Type)
                    {
                        case "selectSeat": lobby.SelectSeat(peer.Id, message.Read<SeatRequest>().Seat); break;
                        case "ready": lobby.SetReady(peer.Id, message.Read<ReadyRequest>().Ready); break;
                        default: throw new InvalidOperationException("Comando indisponível no diagnóstico de lobby.");
                    }
                    await Broadcast(token);
                }
                catch (InvalidOperationException)
                {
                    await peer.Send(WireMessage.From("error", new ProtocolError("invalid_request", "Pedido recusado; confira personagem e estado da sala.")), token);
                }
            }
        }
        catch (Exception e) when (e is IOException or SocketException or JsonException or InvalidDataException or InvalidOperationException or OperationCanceledException or ObjectDisposedException)
        {
            // Never echo a raw message or exception containing private payloads to logs or peers.
        }
        finally
        {
            lock (gate) peers.Remove(peer.Id);
            lobby.Disconnect(peer.Id);
            await peer.DisposeAsync();
            try { await Broadcast(token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        }
    }

    private async Task Broadcast(CancellationToken token)
    {
        Peer[] active;
        lock (gate) active = [.. peers.Values.Where(p => p.Joined)];
        var message = WireMessage.From("lobby", lobby.Snapshot());
        foreach (var peer in active)
        {
            try { await peer.Send(message, token); }
            catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException) { peer.Client.Dispose(); }
        }
    }

    public ValueTask DisposeAsync() { lifetime.Cancel(); listener.Stop(); return ValueTask.CompletedTask; }
}
