using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using PokerNight.Multiplayer;

try
{
    if (args.Length == 0)
    {
        Console.WriteLine("Poker Night Multiplayer — desenvolvimento; integração ainda não validada\n" +
            "inspect <pasta-do-jogo> <perfil.json>\n" +
            "host <pasta-do-jogo> <perfil.json>\n" +
            "lobby-host <IP-local> <porta> <nome> <personagem> <sha256>\n" +
            "lobby-join <IP-host> <porta> <nome> <personagem> <sha256>\n" +
            "Personagens: Max, StrongBad, Heavy, Tycho, Player\n" +
            "lobby-* testa somente conexão e seleção; não inicia poker.");
        return 0;
    }
    if (args[0] is "inspect" or "host")
    {
        if (args.Length != 3) throw new ArgumentException("Informe pasta do jogo e perfil JSON.");
        var profile = GameBuildProfile.Load(args[2]);
        string digest = profile.Verify(args[1], requireRuntimeHooks: args[0] == "host");
        if (args[0] == "host") throw new InvalidOperationException("Bootstrap do jogo ainda não implementado; este comando não inicia uma partida.");
        Console.WriteLine(JsonSerializer.Serialize(new { executableMatched = true, sha256 = digest,
            steamBuild = profile.SteamBuildId, runtimeHooksVerified = profile.RuntimeHooksVerified,
            multiplayerAvailable = false }, WireCodec.Json));
        return 0;
    }
    if (args.Length != 6 || args[0] is not ("lobby-host" or "lobby-join")) throw new ArgumentException("Argumentos inválidos; execute sem argumentos para ajuda.");
    if (!IPAddress.TryParse(args[1], out var address) || !int.TryParse(args[2], out int port) || port is < 1 or > 65535)
        throw new ArgumentException("IP ou porta inválidos.");
    if (!Enum.TryParse<Seat>(args[4], true, out var seat) || !Enum.IsDefined(seat)) throw new ArgumentException("Personagem inválido.");
    if (args[5].Length != 64 || !args[5].All(Uri.IsHexDigit)) throw new ArgumentException("SHA-256 inválido.");
    using var lifetime = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; lifetime.Cancel(); };
    if (args[0] == "lobby-host")
    {
        await using var server = new LobbyTransport(address, port, args[5], args[3], seat);
        Console.WriteLine($"Diagnóstico de lobby em {server.Endpoint}; poker desativado. Ctrl+C para sair.");
        await server.RunAsync(lifetime.Token);
    }
    else
    {
        using var client = new TcpClient(address.AddressFamily) { NoDelay = true };
        await client.ConnectAsync(address, port, lifetime.Token);
        var stream = client.GetStream();
        await WireCodec.WriteAsync(stream, WireMessage.From("hello", new Hello(WireCodec.Version, args[5], args[3])), lifetime.Token);
        while (await WireCodec.ReadAsync(stream, lifetime.Token) is { } message)
        {
            if (message.Type == "welcome")
            {
                await WireCodec.WriteAsync(stream, WireMessage.From("selectSeat", new SeatRequest(seat)), lifetime.Token);
                await WireCodec.WriteAsync(stream, WireMessage.From("ready", new ReadyRequest(true)), lifetime.Token);
            }
            // Only lobby/error message types are displayed. Never print a private hand or arbitrary server payload.
            if (message.Type == "lobby")
            {
                var lobby = message.Read<LobbyState>();
                Console.WriteLine(string.Join(" | ", lobby.Seats.Select(x => $"{x.Seat}: {x.Nickname} {(x.IsBot ? "bot" : x.Ready ? "pronto" : "aguardando")}")));
            }
            else if (message.Type == "error") Console.WriteLine(message.Read<ProtocolError>().Message);
        }
    }
    return 0;
}
catch (OperationCanceledException) { return 0; }
catch (Exception e) when (e is IOException or InvalidOperationException or ArgumentException or JsonException or SocketException)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}
