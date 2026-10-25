using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using PokerNight.Multiplayer;

try
{
    if (args.Length == 0)
    {
        Console.WriteLine("Poker Night Multiplayer — pré-alpha técnica; integração Lua em validação\n" +
            "inspect <pasta-do-jogo> <perfil.json>\n" +
            "lobby-host <IP-local> <porta> <nome> <personagem> <sha256>\n" +
            "lobby-join <IP-host> <porta> <nome> <personagem> <sha256>\n" +
            "play-host <IP-local> <porta> <nome> <personagem> <pessoas-2-a-5> <pasta-jogo> <perfil.json> <runtime>\n" +
            "play-join <IP-host> <porta> <nome> <personagem> <pasta-jogo> <perfil.json> <runtime>\n" +
            "stop <runtime>\n" +
            "Personagens: Max, StrongBad, Heavy, Tycho, Player\n" +
            "lobby-* testa somente conexão e seleção; não inicia poker.");
        return 0;
    }
    if (args[0] is "play-host" or "play-join")
    {
        bool hosting = args[0] == "play-host";
        if (args.Length != (hosting ? 9 : 8)) throw new ArgumentException("Argumentos inválidos para a partida; consulte a ajuda.");
        if (!IPAddress.TryParse(args[1], out var ip) || !int.TryParse(args[2], out int gamePort) || gamePort is < 1 or > 65535)
            throw new ArgumentException("IP ou porta inválidos.");
        if (!Enum.TryParse<Seat>(args[4], true, out var gameSeat) || !Enum.IsDefined(gameSeat)) throw new ArgumentException("Personagem inválido.");
        int offset = hosting ? 1 : 0;
        var profile = GameBuildProfile.Load(args[6 + offset]);
        string digest = profile.Verify(args[5 + offset]);
        var runtime = new FileGameRuntime(args[7 + offset]);
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Task gameExit = GameProcessLifetime.Watch(args[5 + offset], cancellation);
        try
        {
            if (hosting)
            {
                if (!int.TryParse(args[5], out int humans) || humans is < 2 or > 5) throw new ArgumentException("Escolha de 2 a 5 pessoas.");
                await using var server = new GameTransport(ip, gamePort, digest, args[3], gameSeat, runtime, expectedHumans: humans);
                Console.WriteLine($"Sala em {server.Endpoint}. Aguardando {humans} pessoas prontas. Abra o jogo pela Steam e clique em Play Game.");
                await server.RunAsync(cancellation.Token);
            }
            else
            {
                Console.WriteLine("Conectando. Depois do lobby, abra o jogo pela Steam e clique em Play Game.");
                await new GameClient(runtime).RunAsync(ip, gamePort, new(WireCodec.Version, digest, args[3]), gameSeat, cancellation.Token);
            }
        }
        finally
        {
            cancellation.Cancel(); await gameExit;
            if (runtime.Configuration is { } configured) runtime.End(configured.SessionId);
        }
        return 0;
    }
    if (args[0] == "stop" && args.Length == 2)
    {
        var runtime = new FileGameRuntime(args[1]);
        if (runtime.Configuration is not { } configuration) throw new InvalidOperationException("Nenhuma sessão configurada.");
        runtime.End(configuration.SessionId);
        Console.WriteLine("Encerramento da sessão solicitado.");
        return 0;
    }
    if (args[0] is "inspect" or "host")
    {
        if (args.Length != 3) throw new ArgumentException("Informe pasta do jogo e perfil JSON.");
        var profile = GameBuildProfile.Load(args[2]);
        string digest = profile.Verify(args[1], requireRuntimeHooks: args[0] == "host");
        if (args[0] == "host") throw new InvalidOperationException("Use play-host com IP, porta e lugar; consulte a ajuda.");
        Console.WriteLine(JsonSerializer.Serialize(new { executableMatched = true, sha256 = digest,
            steamBuild = profile.SteamBuildId, runtimeHooksVerified = profile.RuntimeHooksVerified,
            technicalPlaytestAvailable = profile.RuntimeHooksVerified, playableAlphaValidated = false }, WireCodec.Json));
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
catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or ArgumentException or JsonException or SocketException)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}
