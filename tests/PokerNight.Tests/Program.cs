using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using PokerNight.Multiplayer;

int passed = 0, failed = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
async Task Test(string name, Func<Task> body)
{
    try { await body(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception e) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + e.Message); }
}
Task Sync(Action body) { body(); return Task.CompletedTask; }
void Throws(Action body) { try { body(); } catch (InvalidOperationException) { return; } throw new Exception("Expected rejection"); }
const string hash = "7D6E8BD48AC434EF59EAEDD61A3704D5DF7934A5593A204F247E93A0194FFBC9";
await Test("fragmented TCP frame roundtrip", async () =>
{
    using var storage = new MemoryStream();
    await WireCodec.WriteAsync(storage, WireMessage.From("ready", new ReadyRequest(true)));
    using var fragmented = new FragmentedStream(storage.ToArray());
    var result = await WireCodec.ReadAsync(fragmented);
    Check(result?.Read<ReadyRequest>().Ready == true, "Wrong frame");
    Check(await WireCodec.ReadAsync(fragmented) is null, "EOF");
});
await Test("oversized and truncated frames rejected", async () =>
{
    byte[] header = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(header, WireCodec.MaximumFrameBytes + 1);
    try { await WireCodec.ReadAsync(new MemoryStream(header)); throw new Exception("Accepted oversized"); } catch (InvalidDataException) { }
    try { await WireCodec.ReadAsync(new MemoryStream([0, 0])); throw new Exception("Accepted truncated"); } catch (EndOfStreamException) { }
});
await Test("seat ownership and readiness", () => Sync(() =>
{
    var host = Guid.NewGuid(); var guest = Guid.NewGuid(); var lobby = new Lobby(hash, host);
    lobby.Join(host, new(1, hash, "Host")); lobby.Join(guest, new(1, hash, "Guest"));
    lobby.SelectSeat(host, Seat.Max); Throws(() => lobby.SelectSeat(guest, Seat.Max));
    lobby.SelectSeat(guest, Seat.Heavy); lobby.SetReady(host, true); Throws(() => lobby.Start(host));
    lobby.SetReady(guest, true); Throws(() => lobby.Start(guest)); lobby.Start(host);
    Check(lobby.Snapshot().Seats.Count(x => x.IsBot) == 3, "Expected 3 bots");
    Throws(() => lobby.SelectSeat(guest, Seat.Tycho));
    lobby.Disconnect(guest); Check(lobby.FindSeat(guest) == Seat.Heavy, "Seat lost before end of hand");
    lobby.HandFinished(); Check(lobby.FindSeat(guest) is null, "Disconnected seat not released to bot");
}));
await Test("build and protocol mismatch rejected", () => Sync(() =>
{
    var lobby = new Lobby(hash, Guid.NewGuid());
    Throws(() => lobby.Join(Guid.NewGuid(), new(2, hash, "Peer")));
    Throws(() => lobby.Join(Guid.NewGuid(), new(1, new string('0', 64), "Peer")));
}));
await Test("simultaneous claims have one winner", async () =>
{
    var lobby = new Lobby(hash, Guid.NewGuid()); var a = Guid.NewGuid(); var b = Guid.NewGuid();
    lobby.Join(a, new(1, hash, "A")); lobby.Join(b, new(1, hash, "B"));
    int wins = 0;
    await Task.WhenAll(new[] { a, b }.Select(id => Task.Run(() => { try { lobby.SelectSeat(id, Seat.Tycho); Interlocked.Increment(ref wins); } catch (InvalidOperationException) { } })));
    Check(wins == 1, "Duplicate seat");
});
var table = new PublicTableState(Guid.NewGuid(), 3, 17, 12, Seat.Heavy, "preflop", 150, 100, 200, [],
    [new(Seat.Heavy, 900, 0, false, false, false)], new());
await Test("actions require current turn and cannot be replayed", () => Sync(() =>
{
    var authority = new ActionAuthority(); var peer = Guid.NewGuid();
    var action = new PlayerAction(table.SessionId, 3, 17, 1, PokerAction.Call);
    Check(!authority.TryAccept(peer, Seat.Max, action, table, out _), "Wrong seat");
    Check(!authority.TryAccept(peer, Seat.Heavy, action with { TurnId = 16 }, table, out _), "Stale turn");
    Check(!authority.TryAccept(peer, Seat.Heavy, action with { Action = PokerAction.Check }, table, out _), "Illegal check");
    Check(!authority.TryAccept(peer, Seat.Heavy, action with { Action = PokerAction.Raise, Amount = -1 }, table, out _), "Negative raise");
    Check(!authority.TryAccept(peer, Seat.Heavy, action with { Action = PokerAction.Raise, Amount = 901 }, table, out _), "Raise above stack");
    Check(!authority.TryAccept(peer, Seat.Heavy, action with { Action = PokerAction.Raise, Amount = 101 }, table, out _), "Underraise");
    Check(authority.TryAccept(peer, Seat.Heavy, action, table, out _), "Valid call rejected");
    Check(!authority.TryAccept(peer, Seat.Heavy, action, table, out _), "Replay accepted");
    Check(!authority.TryAccept(peer, Seat.Heavy, action with { Sequence = 2 }, table, out _), "Two actions consumed the same turn");
    Check(authority.TryAccept(peer, Seat.Heavy, action with { Sequence = 2, TurnId = 18 }, table with { TurnId = 18 }, out _), "Next turn rejected");
    Check(ActionAuthority.DisconnectedAction(0) == PokerAction.Check && ActionAuthority.DisconnectedAction(100) == PokerAction.Fold, "Disconnect policy");
}));
await Test("private projection excludes other players' hands", async () =>
{
    var frame = new HostTableFrame(table, new Dictionary<Seat, Card[]> { [Seat.Heavy] = [new(14, 1), new(14, 2)], [Seat.Max] = [new(2, 3), new(3, 4)] });
    var projected = PrivacyProjection.ForSeat(frame, Seat.Heavy);
    using var bytes = new MemoryStream();
    await WireCodec.WriteAsync(bytes, WireMessage.From("publicTable", projected.Public));
    await WireCodec.WriteAsync(bytes, WireMessage.From("privateHand", projected.Private));
    bytes.Position = 0;
    Check((await WireCodec.ReadAsync(bytes))!.Payload.TryGetProperty("holeCards", out _) == false, "Host data leaked");
    var hand = (await WireCodec.ReadAsync(bytes))!.Read<PrivateHand>();
    Check(hand.Seat == Seat.Heavy && hand.Cards.SequenceEqual(frame.HoleCards[Seat.Heavy]), "Wrong private hand");
    Check(!hand.Cards.Intersect(frame.HoleCards[Seat.Max]).Any(), "Opponent cards leaked");
    hand.Cards[0] = new(5, 1); Check(frame.HoleCards[Seat.Heavy][0].Rank == 14, "Mutable alias");
});
await Test("unknown enum and arbitrary extra fields rejected", () => Sync(() =>
{
    foreach (string json in new[] { "{}", "{\"seat\":99}", "{\"seat\":\"dealer\"}", "{\"seat\":\"max\",\"code\":\"execute()\"}" })
    { try { JsonSerializer.Deserialize<SeatRequest>(json, WireCodec.Json); throw new Exception("Accepted invalid message"); } catch (JsonException) { } }
}));
await Test("actual TCP lobby joins and rejects occupied seat", async () =>
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    await using var server = new LobbyTransport(IPAddress.Loopback, 0, hash, "Host", Seat.Max);
    var run = server.RunAsync(timeout.Token);
    using var client = new TcpClient();
    await client.ConnectAsync(server.Endpoint.Address, server.Endpoint.Port, timeout.Token);
    var stream = client.GetStream();
    await WireCodec.WriteAsync(stream, WireMessage.From("hello", new Hello(1, hash, "Guest")), timeout.Token);
    Check((await WireCodec.ReadAsync(stream, timeout.Token))?.Type == "welcome", "No welcome");
    Check((await WireCodec.ReadAsync(stream, timeout.Token))?.Type == "lobby", "No lobby");
    await WireCodec.WriteAsync(stream, WireMessage.From("selectSeat", new SeatRequest(Seat.Max)), timeout.Token);
    Check((await WireCodec.ReadAsync(stream, timeout.Token))?.Type == "error", "Occupied seat accepted");
    await WireCodec.WriteAsync(stream, WireMessage.From("selectSeat", new SeatRequest(Seat.Heavy)), timeout.Token);
    var snapshot = (await WireCodec.ReadAsync(stream, timeout.Token))!.Read<LobbyState>();
    Check(snapshot.Seats.Single(x => x.Seat == Seat.Heavy).Nickname == "Guest", "Wrong assignment");
    timeout.Cancel(); await run;
});
Console.WriteLine($"{passed} passed; {failed} failed. These are protocol tests, not a playable game validation.");
return failed == 0 ? 0 : 1;

sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes)
{
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => base.ReadAsync(buffer[..Math.Min(buffer.Length, 2)], cancellationToken);
}
