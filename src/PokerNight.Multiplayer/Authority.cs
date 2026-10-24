namespace PokerNight.Multiplayer;

// Host-only data. This object is never serialized onto the peer transport.
public sealed record HostTableFrame(PublicTableState Public, IReadOnlyDictionary<Seat, Card[]> HoleCards);

public static class PrivacyProjection
{
    public static (PublicTableState Public, PrivateHand Private) ForSeat(HostTableFrame frame, Seat seat)
    {
        if (!frame.HoleCards.TryGetValue(seat, out var hand)) hand = [];
        if (hand.Length is not (0 or 2)) throw new InvalidDataException("Mão privada inválida.");
        foreach (var card in hand) card.Validate();
        foreach (var card in frame.Public.CommunityCards) card.Validate();
        // Deep-copy private cards: do not expose a mutable host container to callers.
        return (frame.Public, new(frame.Public.SessionId, frame.Public.HandId, frame.Public.Sequence, seat, [.. hand]));
    }
}

public sealed class ActionAuthority
{
    private readonly Dictionary<Guid, long> lastAccepted = new();
    private (Guid Session, long Hand, long Turn)? consumedTurn;
    private readonly object gate = new();
    public bool TryAccept(Guid connectionId, Seat boundSeat, PlayerAction action, PublicTableState table, out string reason)
    {
        lock (gate)
        {
            reason = "";
            if (action.SessionId != table.SessionId || action.HandId != table.HandId || action.TurnId != table.TurnId)
                reason = "stale_turn";
            else if (table.ActiveSeat != boundSeat) reason = "not_your_turn";
            else if (consumedTurn == (table.SessionId, table.HandId, table.TurnId)) reason = "turn_already_consumed";
            else if (action.Sequence <= 0 || lastAccepted.TryGetValue(connectionId, out var last) && action.Sequence <= last) reason = "duplicate_action";
            else if (!Enum.IsDefined(action.Action)) reason = "invalid_action";
            else
            {
                var player = table.Players.SingleOrDefault(x => x.Seat == boundSeat);
                if (player is null || player.Eliminated || player.Folded || player.AllIn) reason = "inactive_seat";
                else if (action.Amount < 0 || action.Amount > player.Chips) reason = "invalid_amount";
                else if (action.Action == PokerAction.Check && table.ToCall != 0) reason = "cannot_check";
                else if (action.Action == PokerAction.Raise && (action.Amount <= table.ToCall || action.Amount < table.MinimumRaise && action.Amount != player.Chips)) reason = "invalid_raise";
                else if (action.Action != PokerAction.Raise && action.Amount != 0) reason = "unexpected_amount";
            }
            if (reason.Length != 0) return false;
            lastAccepted[connectionId] = action.Sequence;
            consumedTurn = (table.SessionId, table.HandId, table.TurnId);
            return true;
        }
    }
    public static PokerAction DisconnectedAction(long toCall) => toCall == 0 ? PokerAction.Check : PokerAction.Fold;
}
