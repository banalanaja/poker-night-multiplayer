using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using PokerNight.Multiplayer;

namespace PokerNight.App;

public static class AppRelease
{
    public const string Version = BuildIdentity.ModVersion;
}

public sealed class InstallationException(string message, string step, int exitCode) : IOException(message)
{
    public string Step { get; } = step;
    public int ExitCode { get; } = exitCode;
}

public sealed class Installation(string assets, string dataDirectory)
{
    public const string ToolUrl = "https://github.com/HeitorSpectre/TTG-Tools/releases/download/v.1.16.1/TTG.Tools.1.16.1.zip";
    public const string ToolHash = "535E26200666CD584E33C2E262CCA478183C578439EDA3E0AB39A4D8281F4614";
    public string DataDirectory { get; } = Path.GetFullPath(dataDirectory);
    public GameBuildProfile Profile => GameBuildProfile.Load(Path.Combine(assets, "profiles", "steam-25257439.json"));
    public string RuntimeDirectory(string game)
    {
        string key = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.TrimEndingDirectorySeparator(Path.GetFullPath(game)).ToUpperInvariant())))[..12];
        return Path.Combine(DataDirectory, "runtime-" + key);
    }
    public bool IsInstalled(string game)
    {
        try
        {
            Profile.Verify(game);
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(RuntimeDirectory(game), "install-manifest.json")));
            if (!manifest.RootElement.TryGetProperty("modVersion", out var version) || version.GetString() != AppRelease.Version) return false;
            if (!string.Equals(manifest.RootElement.GetProperty("gameDirectory").GetString(), Path.TrimEndingDirectorySeparator(Path.GetFullPath(game)), StringComparison.OrdinalIgnoreCase)) return false;
            string[] expected = ["CP_pc_Project_PokerNightMultiplayer.ttarch2", "_resdesc_50_Project_PokerNightMultiplayer.lua"];
            var files = manifest.RootElement.GetProperty("files").EnumerateArray().ToArray();
            return files.Length == 2 && expected.All(name => files.Count(x => x.GetProperty("name").GetString() == name &&
                Hash(Path.Combine(game, "Archives", name)) == x.GetProperty("sha256").GetString()) == 1);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException) { return false; }
    }
    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }

    public static void ExtractVerifiedTool(string archive, string destination)
    {
        if (Hash(archive) != ToolHash) throw new InvalidDataException("A ferramenta baixada não passou na verificação de integridade.");
        ExtractSafe(archive, destination);
    }
    public static void ExtractSafe(string archive, string destination)
    {
        string root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        using var zip = ZipFile.OpenRead(archive);
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            string path = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || Path.IsPathRooted(entry.FullName) ||
                entry.FullName.Contains(':') || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException("Caminho inválido no pacote.");
            total = checked(total + entry.Length);
            if (total > 512 * 1024 * 1024 || zip.Entries.Count > 5000) throw new InvalidDataException("Pacote grande demais.");
        }
        Directory.CreateDirectory(root); zip.ExtractToDirectory(root, overwriteFiles: true);
    }
    private async Task<string> Tools(IProgress<string> progress, CancellationToken token)
    {
        string? local = Environment.GetEnvironmentVariable("PNM_TTG_DIRECTORY");
        if (!string.IsNullOrEmpty(local)) return Path.GetFullPath(local); // Local developer test; not part of the user setup.
        string archive = Path.Combine(DataDirectory, "ttg-1.16.1.zip");
        Directory.CreateDirectory(DataDirectory);
        if (!File.Exists(archive) || Hash(archive) != ToolHash)
        {
            progress.Report("Baixando a ferramenta de instalação verificada…");
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            using var response = await http.GetAsync(ToolUrl, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 128 * 1024 * 1024) throw new InvalidDataException("Download grande demais.");
            string temporary = archive + ".download";
            try
            {
                await using (var output = File.Create(temporary))
                await using (var input = await response.Content.ReadAsStreamAsync(token))
                {
                    byte[] buffer = new byte[65536]; long bytes = 0; int count;
                    while ((count = await input.ReadAsync(buffer, token)) > 0)
                    { bytes += count; if (bytes > 128 * 1024 * 1024) throw new InvalidDataException("Download grande demais."); await output.WriteAsync(buffer.AsMemory(0, count), token); }
                }
                if (Hash(temporary) != ToolHash) throw new InvalidDataException("A ferramenta baixada não passou na verificação de integridade.");
                File.Move(temporary, archive, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        progress.Report("Conferindo e preparando a ferramenta de instalação…");
        string tools = Path.Combine(DataDirectory, "tools", "ttg-1.16.1");
        ExtractVerifiedTool(archive, tools); return tools;
    }
    public async Task Install(string game, bool uninstall, IProgress<string> progress, CancellationToken token)
    {
        Profile.Verify(game);
        string runtime = RuntimeDirectory(game); Directory.CreateDirectory(runtime);
        string manifest = Path.Combine(runtime, "install-manifest.json");
        string legacy = Path.Combine(game, "mod-project", ".local", "runtime", "install-manifest.json");
        if (!File.Exists(manifest) && File.Exists(legacy)) File.Copy(legacy, manifest);
        string tools = uninstall ? DataDirectory : await Tools(progress, token);
        progress.Report(uninstall ? "Removendo somente os arquivos do mod…" : "Preparando os scripts a partir da sua instalação…");
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        // Windows PowerShell must build its own module path, even when launched from PowerShell 7.
        start.Environment.Remove("PSModulePath"); start.Environment.Remove("PSModuleAnalysisCachePath");
        foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(assets, "tools", "Install-GameScripts.ps1"),
            "-GameDirectory", Path.GetFullPath(game), "-TtgDirectory", tools, "-RuntimeDirectory", runtime, "-ModVersion", AppRelease.Version }) start.ArgumentList.Add(arg);
        if (uninstall) start.ArgumentList.Add("-Uninstall");
        using var process = Process.Start(start) ?? throw new IOException("Não foi possível abrir o instalador.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(); Task<string> stderr = process.StandardError.ReadToEndAsync();
        // Finish or roll back the transaction before allowing the UI to close.
        await process.WaitForExitAsync(); _ = await stdout; string failure = await stderr;
        if (process.ExitCode != 0)
        {
            if (failure.Contains("manifesto", StringComparison.OrdinalIgnoreCase)) throw new InstallationException("Há uma instalação anterior ou um arquivo do mod alterado. Nenhum original foi substituído.", "manifest_guard", process.ExitCode);
            if (failure.Contains("Feche esta instala", StringComparison.OrdinalIgnoreCase)) throw new InstallationException("Feche o jogo e tente instalar novamente.", "game_running", process.ExitCode);
            string step = failure.Contains("adaptador externo") ? "compile_bridge" : failure.Contains("compilar scripts") ? "compile_lua" : failure.Contains("empacotar scripts") ? "pack_scripts" : failure.Contains("extrair o bootstrap") ? "extract_bootstrap" : "installer_process";
            throw new InstallationException("A instalação não terminou. Exporte o diagnóstico para investigação.", step, process.ExitCode);
        }
        if (!uninstall && !IsInstalled(game)) throw new IOException("A conferência final da instalação falhou.");
    }
}
