using System.IO.Compression;
using System.Text.Json;
using PokerNight.App;

static class AppTests
{
    public static Task Diagnostics()
    {
        string root = Path.Combine(Path.GetTempPath(), "pnm-diagnostics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            const string secret = "PRIVATE_CARDS_DECK_SEED_NICKNAME_MUST_NOT_EXPORT";
            File.WriteAllText(Path.Combine(root, "view.json"), JsonSerializer.Serialize(new {
                @public = new { handId = 7, turnId = 11, sequence = 21, activeSeat = "heavy", street = "flop", communityCards = new[] { secret } },
                @private = new { cards = new[] { secret } }, names = secret, deck = secret, seed = secret }));
            File.WriteAllText(Path.Combine(root, "host-frame.json"), secret);
            File.WriteAllText(Path.Combine(root, "status.json"), JsonSerializer.Serialize(new { loaded = true, installed = true, sessionId = Guid.NewGuid(), role = "client", snapshotError = true, stage = "table", error = secret }));
            File.WriteAllText(Path.Combine(root, "save.bundle"), secret); File.WriteAllText(Path.Combine(root, "game.log"), secret);
            var journal = new DiagnosticJournal(root); journal.Add("session_failed", new IOException(secret));
            journal.Add("install_failed", new InstallationException(secret, "compile_lua", 1));
            // Reloading only restores structured, sanitized events.
            journal = new DiagnosticJournal(root);
            string zipPath = Path.Combine(root, "diagnostic.zip");
            DiagnosticBundle.Export(zipPath, new(AppRelease.Version, root, root, 27841, "lan", "client"), journal);
            using var zip = ZipFile.OpenRead(zipPath);
            if (!zip.Entries.Select(x => x.FullName).Order().SequenceEqual(new[] { "LEIA-ME.txt", "diagnostico.json", "eventos.json" }.Order())) throw new Exception("Unexpected diagnostic files.");
            foreach (var entry in zip.Entries)
            {
                using var reader = new StreamReader(entry.Open()); string text = reader.ReadToEnd();
                if (text.Contains(secret) || text.Contains(root)) throw new Exception("Private data leaked into diagnostic ZIP.");
            }
            using var report = JsonDocument.Parse(new StreamReader(zip.GetEntry("diagnostico.json")!.Open()).ReadToEnd());
            if (report.RootElement.GetProperty("table").GetProperty("turnId").GetInt64() != 11 ||
                !report.RootElement.GetProperty("runtime").GetProperty("SnapshotError").GetBoolean()) throw new Exception("Missing useful diagnostic context.");
            return Task.CompletedTask;
        }
        finally { foreach (var file in Directory.EnumerateFiles(root)) File.Delete(file); Directory.Delete(root); }
    }
    public static Task PackageSafety()
    {
        string root = Path.Combine(Path.GetTempPath(), "pnm-package-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string archive = Path.Combine(root, "bad.zip");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(zip.CreateEntry("../escape.txt").Open())) writer.Write("bad");
            try { Installation.ExtractSafe(archive, Path.Combine(root, "output")); throw new Exception("Zip traversal accepted."); }
            catch (InvalidDataException) { }
            if (File.Exists(Path.Combine(root, "escape.txt"))) throw new Exception("Zip escaped destination.");
            try { Installation.ExtractVerifiedTool(archive, Path.Combine(root, "output")); throw new Exception("Unverified tool accepted."); }
            catch (InvalidDataException) { }
            return Task.CompletedTask;
        }
        finally { foreach (var file in Directory.EnumerateFiles(root)) File.Delete(file); Directory.Delete(root); }
    }
}
