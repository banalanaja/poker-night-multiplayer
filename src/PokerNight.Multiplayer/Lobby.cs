namespace PokerNight.Multiplayer;

// Every mutation is serialized, including simultaneous seat requests.
public sealed class Lobby(string gameSha256, Guid hostConnectionId)
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, string> names = new();
    private readonly Dictionary<Guid, Seat> assignments = new();
    private readonly HashSet<Guid> ready = new();
    private bool started;
    public Guid SessionId { get; } = Guid.NewGuid();
    public Guid HostConnectionId { get; } = hostConnectionId;

    public void Join(Guid connectionId, Hello hello)
    {
        lock (gate)
        {
            if (started) throw new InvalidOperationException("A partida já começou; entrada somente no lobby.");
            if (hello.ProtocolVersion != WireCodec.Version || !string.Equals(hello.GameSha256, gameSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Versão do jogo ou protocolo incompatível.");
            if (names.Count >= 5 || names.ContainsKey(connectionId)) throw new InvalidOperationException("Sala cheia ou conexão duplicada.");
            string nickname = hello.Nickname?.Trim() ?? "";
            if (nickname.Length is < 1 or > 24 || nickname.Any(char.IsControl)) throw new InvalidOperationException("Nome inválido.");
            names.Add(connectionId, nickname);
        }
    }

    public void SelectSeat(Guid connectionId, Seat seat)
    {
        lock (gate)
        {
            if (started || !names.ContainsKey(connectionId) || !Enum.IsDefined(seat)) throw new InvalidOperationException("Seleção indisponível.");
            if (assignments.Any(x => x.Key != connectionId && x.Value == seat)) throw new InvalidOperationException("Personagem já escolhido.");
            assignments[connectionId] = seat;
            ready.Remove(connectionId);
        }
    }

    public void SetReady(Guid connectionId, bool value)
    {
        lock (gate)
        {
            if (started || !assignments.ContainsKey(connectionId)) throw new InvalidOperationException("Escolha um personagem primeiro.");
            if (value) ready.Add(connectionId); else ready.Remove(connectionId);
        }
    }

    public void Start(Guid connectionId)
    {
        lock (gate)
        {
            if (connectionId != HostConnectionId) throw new InvalidOperationException("Só o anfitrião inicia a partida.");
            if (started || names.Count < 2 || assignments.Count != names.Count || ready.Count != names.Count)
                throw new InvalidOperationException("São necessárias pelo menos duas pessoas prontas.");
            started = true;
        }
    }

    public Seat? FindSeat(Guid connectionId) { lock (gate) return assignments.TryGetValue(connectionId, out var seat) ? seat : null; }
    public Guid? FindConnection(Seat seat) { lock (gate) return assignments.Where(x => x.Value == seat).Select(x => (Guid?)x.Key).SingleOrDefault(); }

    public void Disconnect(Guid connectionId)
    {
        lock (gate)
        {
            names.Remove(connectionId); ready.Remove(connectionId);
            // During a hand reserve the seat until HandFinished; never hand it to a new peer.
            if (!started) assignments.Remove(connectionId);
        }
    }

    public void HandFinished()
    {
        lock (gate) foreach (var id in assignments.Keys.Where(id => !names.ContainsKey(id)).ToArray()) assignments.Remove(id);
    }

    public LobbyState Snapshot()
    {
        lock (gate)
        {
            var seats = Enum.GetValues<Seat>().Select(seat =>
            {
                var owner = assignments.Where(x => x.Value == seat).Select(x => (Guid?)x.Key).SingleOrDefault();
                bool online = owner.HasValue && names.ContainsKey(owner.Value);
                return new SeatState(seat, online ? names[owner!.Value] : seat.ToString(), !owner.HasValue, online, owner.HasValue && ready.Contains(owner.Value));
            }).ToArray();
            return new(SessionId, WireCodec.Version, gameSha256, started, seats);
        }
    }
}
