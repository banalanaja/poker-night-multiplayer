using System.Net;
using System.Text;
using System.Text.Json;
using PokerNight.Multiplayer;

static class LiveBridgeTests
{
    public static async Task Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "pnm-bridge-" + Guid.NewGuid().ToString("N"));
        string hostPath = Path.Combine(root, "host"), guestPath = Path.Combine(root, "guest");
        Directory.CreateDirectory(hostPath); Directory.CreateDirectory(guestPath);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var host = new FileGameRuntime(hostPath); var guest = new FileGameRuntime(guestPath);
        const string hash = "7D6E8BD48AC434EF59EAEDD61A3704D5DF7934A5593A204F247E93A0194FFBC9";
        await using var server = new GameTransport(IPAddress.Loopback, 0, hash, "Host", Seat.Player, host, expectedHumans: 2);
        Task serverRun = server.RunAsync(timeout.Token);
        Task guestRun = new GameClient(guest).RunAsync(server.Endpoint.Address, server.Endpoint.Port,
            new(WireCodec.Version, hash, "Guest"), Seat.Heavy, timeout.Token);
        async Task Wait(Func<bool> condition)
        {
            while (!condition()) { timeout.Token.ThrowIfCancellationRequested(); await Task.Delay(30, timeout.Token); }
        }
        T Read<T>(string name) => JsonSerializer.Deserialize<T>(File.ReadAllText(name), WireCodec.Json)!;
        void Write<T>(string name, T value)
        {
            string temporary = name + ".tmp";
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(value, WireCodec.Json));
            File.Move(temporary, name, true);
        }
        try
        {
            await Wait(() => server.Snapshot.Started);
            var session = server.Snapshot.SessionId;
            var players = Enum.GetValues<Seat>().Select(seat => new PublicPlayerState(seat, 1000, 0, false, false, false)).ToArray();
            var table = new PublicTableState(session, 1, 1, 1, Seat.Heavy, "preflop", 150, 100, 200, [], players, new());
            var hands = new Dictionary<Seat, Card[]> {
                [Seat.Heavy] = [new(14,1),new(14,2)], [Seat.Max] = [new(2,3),new(3,4)],
                [Seat.Player] = [new(4,1),new(5,2)], [Seat.Tycho] = [], [Seat.StrongBad] = [] };
            Write(Path.Combine(hostPath, "host-frame.json"), new HostTableFrame(table, hands));
            string viewFile = Path.Combine(guestPath, "view.json");
            await Wait(() => File.Exists(viewFile));
            var view = Read<ClientView>(viewFile);
            if (view.Private.Seat != Seat.Heavy || !view.Private.Cards.SequenceEqual(hands[Seat.Heavy])) throw new Exception("Guest hand mismatch.");
            if (File.ReadAllText(viewFile).Contains("holeCards", StringComparison.Ordinal)) throw new Exception("Host-only data leaked.");
            if (view.Private.Cards.Intersect(hands[Seat.Max]).Any()) throw new Exception("Opponent cards leaked.");
            if (OperatingSystem.IsWindows())
            {
                // Match Lua's reader: allow reads/writes, but not replacing the open file.
                var reader = new FileStream(viewFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                Task replace = Task.Run(() => guest.Show(view));
                await Task.Delay(30, timeout.Token); reader.Dispose();
                await replace;
            }
            Write(Path.Combine(guestPath, "local-action.json"), new RuntimeAction(session, 1, 1, 1, Seat.Heavy, PokerAction.Call));
            string command = Path.Combine(hostPath, "command.json");
            await Wait(() => File.Exists(command));
            var action = Read<RuntimeAction>(command);
            if (action.Seat != Seat.Heavy || action.Action != PokerAction.Call || action.TurnId != 1) throw new Exception("Action lost binding.");
            File.Delete(command);
            // A second sequence cannot consume the same authoritative turn.
            Write(Path.Combine(guestPath, "local-action.json"), new RuntimeAction(session, 1, 1, 2, Seat.Heavy, PokerAction.Raise, 400));
            await Task.Delay(250, timeout.Token);
            if (File.Exists(command)) throw new Exception("Turn consumed twice.");
            Write(Path.Combine(guestPath, "local-action.json"), new RuntimeAction(session, 1, 999, 3, Seat.Heavy, PokerAction.Call));
            string rejection = Path.Combine(guestPath, "action-error.json");
            await Wait(() => File.Exists(rejection) && Read<JsonElement>(rejection).GetProperty("turnId").GetInt64() == 999);
            if (File.Exists(command)) throw new Exception("Stale turn dispatched to engine.");
            Write(Path.Combine(hostPath, "host-frame.json"), new HostTableFrame(table with { TurnId = 2, Sequence = 2, ActiveSeat = Seat.Player }, hands));
            await Wait(() => Read<ClientView>(viewFile).Public.TurnId == 2);
        }
        finally
        {
            timeout.Cancel();
            try { await guestRun; } catch (OperationCanceledException) { }
            await serverRun;
            // Fixed task-owned files only; do not recursively remove a computed directory.
            foreach (string directory in new[] { hostPath, guestPath })
            {
                foreach (string file in Directory.EnumerateFiles(directory)) File.Delete(file);
                Directory.Delete(directory);
            }
            Directory.Delete(root);
        }
    }
}
