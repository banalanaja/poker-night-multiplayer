using System.Net;
using System.Net.Sockets;

namespace PokerNight.Multiplayer;

public sealed class GameClient(FileGameRuntime runtime)
{
    private LobbyState? lobbyState;
    public LobbyState? Lobby => Volatile.Read(ref lobbyState);
    public async Task RunAsync(IPAddress address, int port, Hello hello, Seat seat, CancellationToken cancellationToken)
    {
        using var socket = new TcpClient(address.AddressFamily) { NoDelay = true };
        using var connectionTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectionTimeout.CancelAfter(TimeSpan.FromSeconds(10));
        try { await socket.ConnectAsync(address, port, connectionTimeout.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new IOException("A conexão não respondeu em 10 segundos. Confira o IP, a VPN e o firewall."); }
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var stream = socket.GetStream();
        var sendLock = new SemaphoreSlim(1);
        async Task Send(WireMessage message)
        {
            await sendLock.WaitAsync(lifetime.Token);
            try { await WireCodec.WriteAsync(stream, message, lifetime.Token); }
            finally { sendLock.Release(); }
        }
        await Send(WireMessage.From("hello", hello));
        WireMessage? welcomeMessage;
        using (var handshake = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
        {
            handshake.CancelAfter(TimeSpan.FromSeconds(10));
            try { welcomeMessage = await WireCodec.ReadAsync(stream, handshake.Token); }
            catch (OperationCanceledException) when (!lifetime.IsCancellationRequested) { throw new IOException("O anfitrião não concluiu a conexão."); }
        }
        if (welcomeMessage?.Type != "welcome") throw new InvalidDataException("O anfitrião recusou a conexão. Confiram a mesma versão do mod e do jogo.");
        Guid session = welcomeMessage.Read<Welcome>().SessionId;
        runtime.Configure(new(session, "client", seat, new() { [seat] = true }));
        await Send(WireMessage.From("selectSeat", new SeatRequest(seat)));
        await Send(WireMessage.From("ready", new ReadyRequest(true)));
        long lastLocal = 0;
        var names = Enum.GetValues<Seat>().ToDictionary(x => x, x => x.ToString());
        PublicTableState? current = null;
        Task input = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
            while (await timer.WaitForNextTickAsync(lifetime.Token))
            {
                if (runtime.EndRequested(session)) { lifetime.Cancel(); return; }
                var action = runtime.ReadLocalAction();
                if (action is null || action.SessionId != session || action.Seat != seat || action.Sequence <= lastLocal) continue;
                lastLocal = action.Sequence;
                await Send(WireMessage.From("action", action.ToPlayerAction()));
            }
        }, CancellationToken.None);
        _ = input.ContinueWith(_ => lifetime.Cancel(), CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        try
        {
            while (await WireCodec.ReadAsync(stream, lifetime.Token) is { } message)
            {
                switch (message.Type)
                {
                    case "lobby":
                        var lobby = message.Read<LobbyState>();
                        if (lobby.SessionId != session) throw new InvalidDataException("Outra sessão.");
                        names = lobby.Seats.ToDictionary(x => x.Seat, x => x.IsBot ? "Bot" : x.Nickname);
                        if (lobby.Started) runtime.Start(session);
                        Volatile.Write(ref lobbyState, lobby);
                        break;
                    case "publicTable":
                        var table = message.Read<PublicTableState>();
                        if (table.SessionId != session || table.CommunityCards is null || table.CommunityCards.Length > 5 || table.Players is null || table.Players.Length != 5)
                            throw new InvalidDataException("Mesa inválida.");
                        if (current is null || table.Sequence > current.Sequence) current = table;
                        break;
                    case "privateHand":
                        var hand = message.Read<PrivateHand>();
                        if (hand.SessionId != session || hand.Seat != seat || hand.Cards is null || hand.Cards.Length is not (0 or 2))
                            throw new InvalidDataException("Mão privada inválida.");
                        foreach (var card in hand.Cards) card.Validate();
                        if (current is not null && current.HandId == hand.HandId && current.Sequence == hand.Sequence)
                            runtime.Show(new(current, hand, names));
                        break;
                    case "error":
                        var error = message.Read<ProtocolError>();
                        if (current is null) throw new InvalidOperationException(error.Code == "seat_unavailable" ? "Esse personagem já está ocupado. Escolha outro e entre novamente." : "O anfitrião recusou o pedido da sala.");
                        runtime.ActionError(session, error.TurnId ?? current.TurnId);
                        break;
                    default: throw new InvalidDataException("Mensagem desconhecida.");
                }
            }
        }
        finally
        {
            lifetime.Cancel(); socket.Dispose();
            try { await input; } catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            runtime.End(session); sendLock.Dispose();
        }
    }
}
