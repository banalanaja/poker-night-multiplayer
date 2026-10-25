using System.IO.Compression;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using PokerNight.Multiplayer;

namespace PokerNight.App;

public sealed record DiagnosticEvent(DateTimeOffset Utc, string Code, string? ErrorType = null, int? SocketError = null,
    int? HResult = null, int? HttpStatus = null, string? InstallStep = null, int? ExitCode = null, string[]? Calls = null);

// Structured events only. Never accept arbitrary strings/payloads from the game or a peer.
public sealed class DiagnosticJournal
{
    private static readonly HashSet<string> Codes = ["app_open", "app_failed", "install_start", "install_ok", "install_failed",
        "session_start", "session_stop", "session_failed", "lobby_ready", "game_open", "game_open_failed",
        "runtime_failed", "export_ok", "uninstall_ok", "uninstall_failed"];
    private readonly Queue<DiagnosticEvent> entries = new();
    private readonly object gate = new();
    private readonly string? path;
    public DiagnosticJournal(string? directory = null)
    {
        if (directory is null) return;
        Directory.CreateDirectory(directory); path = Path.Combine(directory, "events.json");
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length <= 131072)
                foreach (var entry in JsonSerializer.Deserialize<DiagnosticEvent[]>(File.ReadAllText(path)) ?? [])
                    if (Codes.Contains(entry.Code)) entries.Enqueue(Sanitize(entry));
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        while (entries.Count > 200) entries.Dequeue();
    }
    private static HashSet<string> KnownMethods()
    {
        var result = new HashSet<string>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().Where(x => x.GetName().Name?.StartsWith("PokerNight", StringComparison.Ordinal) == true))
            try
            {
                foreach (var type in assembly.GetTypes()) foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                    result.Add(method.DeclaringType?.FullName + "." + method.Name);
            }
            catch (ReflectionTypeLoadException) { }
        return result;
    }
    private static DiagnosticEvent Sanitize(DiagnosticEvent entry)
    {
        var known = KnownMethods();
        return entry with {
            ErrorType = entry.ErrorType is "SocketException" or "IOException" or "InvalidDataException" or "HttpRequestException" or "JsonException" or "Win32Exception" or
                "ArgumentException" or "FormatException" or "NullReferenceException" or "ExternalException" or "COMException" or "DirectoryNotFoundException" or "FileNotFoundException" or "InstallationException" or "InvalidOperationException" or "OperationCanceledException" or "UnauthorizedAccessException" ? entry.ErrorType : null,
            InstallStep = entry.InstallStep is "compile_bridge" or "compile_lua" or "pack_scripts" or "extract_bootstrap" or "installer_process" or "manifest_guard" or "game_running" ? entry.InstallStep : null,
            Calls = entry.Calls?.Where(known.Contains).Take(8).ToArray()
        };
    }
    public void Add(string code, Exception? error = null)
    {
        if (!Codes.Contains(code)) throw new ArgumentException("Unknown diagnostic event.");
        lock (gate)
        {
            entries.Enqueue(Sanitize(new(DateTimeOffset.UtcNow, code, error?.GetType().Name,
                error is SocketException socket ? (int)socket.SocketErrorCode : null, error?.HResult,
                error is HttpRequestException http ? (int?)http.StatusCode : null,
                (error as InstallationException)?.Step, (error as InstallationException)?.ExitCode,
                error is null ? null : new StackTrace(error, false).GetFrames().Select(x => x.GetMethod()).Where(x => x is not null)
                    .Select(x => x!.DeclaringType?.FullName + "." + x.Name).ToArray())));
            while (entries.Count > 200) entries.Dequeue();
            if (path is not null)
                try { File.WriteAllText(path, JsonSerializer.Serialize(entries)); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
    public DiagnosticEvent[] Snapshot() { lock (gate) return [.. entries]; }
}

public sealed record DiagnosticContext(string Version, string GameDirectory, string RuntimeDirectory,
    int Port, string AddressCategory, string Role);

public static class DiagnosticBundle
{
    private static object NetworkSummary()
    {
        try
        {
            var adapters = NetworkInterface.GetAllNetworkInterfaces().Where(x => x.OperationalStatus == OperationalStatus.Up).ToArray();
            return new { upCount = adapters.Length,
                radminDetected = adapters.Any(x => (x.Name + x.Description).Contains("radmin", StringComparison.OrdinalIgnoreCase)),
                tailscaleDetected = adapters.Any(x => (x.Name + x.Description).Contains("tailscale", StringComparison.OrdinalIgnoreCase)) };
        }
        catch (NetworkInformationException) { return new { unavailable = true }; }
    }
    private static object ModFiles(string game, string runtime)
    {
        var entries = new List<object>();
        foreach (string name in new[] { "CP_pc_Project_PokerNightMultiplayer.ttarch2", "_resdesc_50_Project_PokerNightMultiplayer.lua" })
        {
            string? hash = null;
            try { using var input = File.OpenRead(Path.Combine(game, "Archives", name)); hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(input)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            entries.Add(new { name, present = hash is not null, sha256 = hash });
        }
        return new { manifestPresent = File.Exists(Path.Combine(runtime, "install-manifest.json")), files = entries };
    }
    private static object? TableSummary(string directory)
    {
        foreach (string name in new[] { "view.json", "host-frame.json" })
        {
            try
            {
                using var stream = new FileStream(Path.Combine(directory, name), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (stream.Length > 65536) continue;
                using var document = JsonDocument.Parse(stream, new() { MaxDepth = 32 });
                var table = document.RootElement.GetProperty("public");
                long Number(string field) => table.TryGetProperty(field, out var value) && value.TryGetInt64(out var n) ? n : -1;
                string? EnumString(string field, string[] allowed) => table.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String && allowed.Contains(value.GetString()) ? value.GetString() : null;
                return new { handId = Number("handId"), turnId = Number("turnId"), sequence = Number("sequence"),
                    street = EnumString("street", ["idle", "dealing", "preflop", "flop", "turn", "river", "showdown", "complete"]),
                    activeSeat = EnumString("activeSeat", ["max", "strongBad", "heavy", "tycho", "player"]) };
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException) { }
        }
        return null;
    }
    public static void Export(string destination, DiagnosticContext context, DiagnosticJournal journal)
    {
        var runtime = new FileGameRuntime(context.RuntimeDirectory);
        var status = runtime.Status;
        object? safeStatus = status is null ? null : new { status.Loaded, status.Installed, status.ProgressIsolated,
            status.SnapshotError, status.Ended, role = status.Role is "host" or "client" ? status.Role : null,
            stage = status.Stage is "bindings" or "table" or "input" or "protect_progress" or "launch_hook" or "input_proxy" or "host_hooks" or "replica_hooks" or "runtime_loop" ? status.Stage : null,
            installFailed = status.Error == "runtime_install_failed" };
        string? executableHash = null;
        try
        {
            using var input = File.OpenRead(Path.Combine(context.GameDirectory, "CelebrityPoker.exe"));
            executableHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(input));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        var report = new { schemaVersion = 1, createdAtUtc = DateTimeOffset.UtcNow, version = context.Version,
            protocolVersion = WireCodec.Version, os = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            executableHash, port = context.Port,
            addressCategory = context.AddressCategory is "loopback" or "tailscale" or "radmin" or "lan" or "other" ? context.AddressCategory : "unknown",
            role = context.Role is "host" or "client" ? context.Role : "none", runtime = safeStatus,
            network = NetworkSummary(), installation = ModFiles(context.GameDirectory, context.RuntimeDirectory),
            table = TableSummary(context.RuntimeDirectory), privacy = new { cardsExcluded = true, savesExcluded = true, rawLogsExcluded = true } };
        // Build only a fixed allowlist; never zip the runtime folder or raw exception text.
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var zip = new ZipArchive(output, ZipArchiveMode.Create);
        void Json(string name, object value)
        {
            using var writer = new StreamWriter(zip.CreateEntry(name).Open());
            writer.Write(JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
        }
        Json("diagnostico.json", report); Json("eventos.json", journal.Snapshot());
        using var readme = new StreamWriter(zip.CreateEntry("LEIA-ME.txt").Open());
        readme.Write("Envie este ZIP inteiro para quem vai investigar o problema.\n" +
            "Inclui versão, erros estruturados, estado técnico e hash do executável.\n" +
            "Não inclui cartas, baralho, saves, nomes, IPs, caminhos pessoais ou logs brutos.\n" +
            "Informe também o que estava fazendo, em qual máquina ocorreu e se usa Radmin/Tailscale/LAN.\n");
    }
}
