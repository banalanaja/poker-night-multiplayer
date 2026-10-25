using System.Reflection;
using System.Text;
using PokerNight.App;

namespace PokerNight.Desktop;

internal static class AppAssets
{
    public static string Prepare(string data)
    {
        string root = Path.Combine(data, "app-assets", AppRelease.Version);
        var assembly = Assembly.GetExecutingAssembly();
        foreach (string resource in assembly.GetManifestResourceNames().Where(x => x.StartsWith("pnm/", StringComparison.Ordinal)))
        {
            string relative = resource[4..].Replace('/', Path.DirectorySeparatorChar);
            string destination = Path.GetFullPath(Path.Combine(root, relative));
            if (!destination.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Recurso interno inválido.");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var input = assembly.GetManifestResourceStream(resource)!;
            if (destination.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
            {
                using var reader = new StreamReader(input, Encoding.UTF8);
                // Windows PowerShell 5 needs a BOM for the Portuguese messages.
                File.WriteAllText(destination, reader.ReadToEnd(), new UTF8Encoding(true));
            }
            else { using var output = File.Create(destination); input.CopyTo(output); }
        }
        if (!File.Exists(Path.Combine(root, "profiles", "steam-25257439.json"))) throw new InvalidDataException("Recursos de instalação ausentes.");
        return root;
    }
}
