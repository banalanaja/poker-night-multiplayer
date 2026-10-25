using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PokerNight.Multiplayer;

public enum Seat { Max, StrongBad, Heavy, Tycho, Player }
public enum PokerAction { Check, Call, Fold, Raise, AllIn }
public sealed record SeatState(Seat Seat, string Nickname, bool IsBot, bool Connected, bool Ready);
public sealed record LobbyState(Guid SessionId, int ProtocolVersion, string GameSha256, bool Started, SeatState[] Seats);
public sealed record PublicPlayerState(Seat Seat, long Chips, long Contribution, bool Folded, bool AllIn, bool Eliminated);
public sealed record Card(int Rank, int Suit)
{
    public void Validate()
    {
        if (Rank is < 2 or > 14 || Suit is < 1 or > 4) throw new InvalidDataException("Carta inválida.");
    }
}
public sealed record PublicTableState(Guid SessionId, long HandId, long TurnId, long Sequence,
    Seat? ActiveSeat, string Street, long Pot, long ToCall, long MinimumRaise, Card[] CommunityCards,
    PublicPlayerState[] Players, Dictionary<Seat, Card[]> RevealedHands, TableActionEvent? LastAction = null);
public sealed record TableActionEvent(Seat Seat, PokerAction Action, long Amount, long TurnId);
public sealed record PrivateHand(Guid SessionId, long HandId, long Sequence, Seat Seat, Card[] Cards);
public sealed record PlayerAction([property: JsonRequired] Guid SessionId, [property: JsonRequired] long HandId,
    [property: JsonRequired] long TurnId, [property: JsonRequired] long Sequence,
    [property: JsonRequired] PokerAction Action, long Amount = 0);
public static class BuildIdentity { public const string ModVersion = "0.2.0-preview.2"; }
public sealed record Hello(int ProtocolVersion, string GameSha256, string Nickname,
    [property: JsonRequired] string ModVersion = BuildIdentity.ModVersion);
public sealed record Welcome(Guid SessionId, Guid ConnectionId);
public sealed record SeatRequest([property: JsonRequired] Seat Seat);
public sealed record ReadyRequest(bool Ready);
public sealed record ProtocolError(string Code, string Message, long? TurnId = null);
public sealed record WireMessage(string Type, JsonElement Payload)
{
    public static WireMessage From<T>(string type, T value) => new(type, JsonSerializer.SerializeToElement(value, WireCodec.Json));
    public T Read<T>() => Payload.Deserialize<T>(WireCodec.Json) ?? throw new InvalidDataException("Mensagem vazia.");
}

public static class WireCodec
{
    public const int Version = 3;
    public const int MaximumFrameBytes = 65536;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 32,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    public static async ValueTask WriteAsync(Stream stream, WireMessage message, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, Json);
        if (payload.Length is 0 or > MaximumFrameBytes) throw new InvalidDataException("Mensagem muito grande.");
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static async ValueTask<WireMessage?> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        byte[] header = new byte[4];
        int first = await stream.ReadAsync(header.AsMemory(0, 1), cancellationToken);
        if (first == 0) return null;
        await stream.ReadExactlyAsync(header.AsMemory(1), cancellationToken);
        int size = BinaryPrimitives.ReadInt32BigEndian(header);
        if (size is <= 0 or > MaximumFrameBytes) throw new InvalidDataException("Tamanho de mensagem inválido.");
        byte[] payload = new byte[size];
        await stream.ReadExactlyAsync(payload, cancellationToken);
        var message = JsonSerializer.Deserialize<WireMessage>(payload, Json) ?? throw new InvalidDataException("Mensagem inválida.");
        if (string.IsNullOrWhiteSpace(message.Type) || message.Type.Length > 40 || message.Payload.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Envelope inválido.");
        return message;
    }
}
