using System.Net;
using System.Text.Json;
using PokerNight.Multiplayer;

static class FivePeerTests
{
    public static async Task Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "pnm-five-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var host = new FileGameRuntime(Path.Combine(root, "host"));
        const string hash = "7D6E8BD48AC434EF59EAEDD61A3704D5DF7934A5593A204F247E93A0194FFBC9";
        await using var server = new GameTransport(IPAddress.Loopback, 0, hash, "Host", Seat.Player, host, expectedHumans: 5);
        Task run = server.RunAsync(timeout.Token);
        var clients = new List<(Seat Seat, FileGameRuntime Runtime, CancellationTokenSource Lifetime, Task Run)>();
        async Task Wait(Func<bool> condition)
        { while (!condition()) { timeout.Token.ThrowIfCancellationRequested(); await Task.Delay(25, timeout.Token); } }
        T? Read<T>(string path) where T : class
        { try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path), WireCodec.Json); } catch (IOException) { return null; } }
        void Write<T>(string path, T value)
        { string tmp = path + ".tmp"; File.WriteAllBytes(tmp, JsonSerializer.SerializeToUtf8Bytes(value, WireCodec.Json)); File.Move(tmp, path, true); }
        try
        {
            foreach (Seat seat in new[] { Seat.Max, Seat.StrongBad, Seat.Heavy, Seat.Tycho })
            {
                var runtime = new FileGameRuntime(Path.Combine(root, seat.ToString()));
                var lifetime = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                Task task = new GameClient(runtime).RunAsync(server.Endpoint.Address, server.Endpoint.Port,
                    new(WireCodec.Version, hash, "Guest" + seat), seat, lifetime.Token);
                clients.Add((seat, runtime, lifetime, task));
                await Wait(() => server.Snapshot.Seats.Single(x => x.Seat == seat).Ready);
                if (clients.Count < 4 && server.Snapshot.Started) throw new Exception("Room started before five humans were ready.");
            }
            await Wait(() => server.Snapshot.Started);
            Guid session = server.Snapshot.SessionId;
            var players = Enum.GetValues<Seat>().Select(x => new PublicPlayerState(x, 1000, 0, false, false, false)).ToArray();
            var table = new PublicTableState(session, 1, 1, 1, Seat.Heavy, "preflop", 100, 100, 200, [], players, new());
            var hands = Enum.GetValues<Seat>().ToDictionary(x => x, x => new[] { new Card(2 + 2 * (int)x, 1), new Card(3 + 2 * (int)x, 2) });
            Write(Path.Combine(host.DirectoryPath, "host-frame.json"), new HostTableFrame(table, hands));
            foreach (var peer in clients)
            {
                string path = Path.Combine(peer.Runtime.DirectoryPath, "view.json");
                await Wait(() => Read<ClientView>(path)?.Public.Sequence == 1);
                var view = Read<ClientView>(path)!;
                if (view.Private.Seat != peer.Seat || !view.Private.Cards.SequenceEqual(hands[peer.Seat])) throw new Exception("Wrong private hand among five peers.");
                if (File.ReadAllText(path).Contains("holeCards")) throw new Exception("Host data leaked.");
            }
            var heavy = clients.Single(x => x.Seat == Seat.Heavy);
            heavy.Lifetime.Cancel();
            try { await heavy.Run; } catch (OperationCanceledException) { }
            string command = Path.Combine(host.DirectoryPath, "command.json");
            await Wait(() => Read<RuntimeAction>(command)?.Action == PokerAction.Fold);
            if (Read<RuntimeAction>(command)?.Seat != Seat.Heavy) throw new Exception("Disconnect fallback used another seat.");
            Write(Path.Combine(host.DirectoryPath, "host-frame.json"), new HostTableFrame(table with { HandId = 2, TurnId = 2, Sequence = 2, ActiveSeat = Seat.Max }, hands));
            await Wait(() => host.Configuration?.ControlledSeats.ContainsKey(Seat.Heavy) == false);
            if (!server.Snapshot.Seats.Single(x => x.Seat == Seat.Heavy).IsBot) throw new Exception("Disconnected player not replaced on next hand.");
        }
        finally
        {
            timeout.Cancel();
            foreach (var peer in clients) { try { await peer.Run; } catch (OperationCanceledException) { } peer.Lifetime.Dispose(); }
            await run;
            foreach (string directory in Directory.GetDirectories(root))
            { foreach (string file in Directory.EnumerateFiles(directory)) File.Delete(file); Directory.Delete(directory); }
            Directory.Delete(root);
        }
    }
}
