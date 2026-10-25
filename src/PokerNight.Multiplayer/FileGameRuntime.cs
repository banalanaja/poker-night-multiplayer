using System.Text.Json;

namespace PokerNight.Multiplayer;

public sealed record RuntimeConfiguration(Guid SessionId, string Role, Seat LocalSeat, Dictionary<Seat, bool> ControlledSeats);
public sealed record RuntimeAction(Guid SessionId, long HandId, long TurnId, long Sequence, Seat Seat, PokerAction Action, long Amount = 0)
{
    public PlayerAction ToPlayerAction() => new(SessionId, HandId, TurnId, Sequence, Action, Amount);
}
public sealed record ClientView(PublicTableState Public, PrivateHand Private, Dictionary<Seat, string> Names);
public sealed record RuntimeStatus(bool Loaded, bool Installed, string? Lua, Guid SessionId, string? Role,
    bool ProgressIsolated = false, bool SnapshotError = false, bool Ended = false, string? Error = null, string? Stage = null);

// This directory is transient local IPC, not a diagnostic log. Never send host-frame.json to a peer.
public sealed class FileGameRuntime(string directory)
{
    private sealed record EndRequest(Guid SessionId);
    private long dispatchSequence;
    public string DirectoryPath { get; } = Path.GetFullPath(directory);
    public RuntimeConfiguration? Configuration => Read<RuntimeConfiguration>("session-config.json");
    public RuntimeStatus? Status => Read<RuntimeStatus>("status.json");

    private T? Read<T>(string name) where T : class
    {
        try
        {
            using var input = new FileStream(Path.Combine(DirectoryPath, name), FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            if (input.Length is <= 0 or > WireCodec.MaximumFrameBytes) return null;
            return JsonSerializer.Deserialize<T>(input, WireCodec.Json);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    private void Write<T>(string name, T value)
    {
        Directory.CreateDirectory(DirectoryPath);
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(value, WireCodec.Json);
        if (data.Length > WireCodec.MaximumFrameBytes) throw new InvalidDataException("IPC message too large.");
        string path = Path.Combine(DirectoryPath, name);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, data);
            // The engine's Lua reader does not grant FILE_SHARE_DELETE on Windows.
            // Wait briefly for that reader to close before atomically replacing its view.
            for (int attempt = 0; ; attempt++)
            {
                try { File.Move(temporary, path, overwrite: true); break; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    if (attempt >= 19) throw new IOException("Não foi possível atualizar o IPC local.", e);
                    Thread.Sleep(5);
                }
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void Configure(RuntimeConfiguration configuration) => Write("session-config.json", configuration);
    public void Start(Guid sessionId) => Write("start.json", new { sessionId });
    public HostTableFrame? ReadHostFrame() => Read<HostTableFrame>("host-frame.json");
    public RuntimeAction? ReadLocalAction() => Read<RuntimeAction>("local-action.json");
    public void SendAction(Seat seat, PlayerAction action) => Write("command.json",
        new RuntimeAction(action.SessionId, action.HandId, action.TurnId, Interlocked.Increment(ref dispatchSequence), seat, action.Action, action.Amount));
    public void Show(ClientView view) => Write("view.json", view);
    public void End(Guid sessionId) => Write("end.json", new { sessionId });
    public bool EndRequested(Guid sessionId) => Read<EndRequest>("end.json")?.SessionId == sessionId;
    public void ActionError(Guid sessionId, long turnId) => Write("action-error.json", new { sessionId, turnId });
}
