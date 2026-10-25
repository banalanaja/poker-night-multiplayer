using System.Net;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace PokerNight.Desktop;

internal static partial class GameDiscovery
{
    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"")]
    private static partial Regex Libraries();
    public static string Find()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        if (key?.GetValue("SteamPath") is string steam) roots.Add(steam);
        roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        foreach (string root in roots.ToArray())
            try
            {
                string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf)) foreach (Match match in Libraries().Matches(File.ReadAllText(vdf))) roots.Add(match.Groups[1].Value.Replace(@"\\", @"\"));
            }
            catch (IOException) { }
        return roots.Select(root => Path.GetFullPath(Path.Combine(root, "steamapps", "common", "Poker Night at the Inventory Remastered")))
            .FirstOrDefault(path => File.Exists(Path.Combine(path, "CelebrityPoker.exe"))) ?? "";
    }
    public static string[] Addresses()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces().Where(x => x.OperationalStatus == OperationalStatus.Up)
                .OrderByDescending(x => x.Name.Contains("tailscale", StringComparison.OrdinalIgnoreCase) || x.Name.Contains("radmin", StringComparison.OrdinalIgnoreCase))
                .SelectMany(x => x.GetIPProperties().UnicastAddresses).Select(x => x.Address)
                .Where(x => x.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !IPAddress.IsLoopback(x))
                .Select(x => x.ToString()).Distinct().Append("127.0.0.1").ToArray();
        }
        catch (NetworkInformationException) { return ["127.0.0.1"]; }
    }
    public static string Category(string value)
    {
        if (!IPAddress.TryParse(value, out var address)) return "unknown";
        if (IPAddress.IsLoopback(address)) return "loopback";
        try
        {
            if (NetworkInterface.GetAllNetworkInterfaces().Any(x => (x.Name + x.Description).Contains("radmin", StringComparison.OrdinalIgnoreCase) &&
                x.GetIPProperties().UnicastAddresses.Any(a => a.Address.Equals(address)))) return "radmin";
        }
        catch (NetworkInformationException) { }
        byte[] b = address.GetAddressBytes();
        if (b.Length != 4) return "other";
        if (b[0] == 100 && b[1] is >= 64 and <= 127) return "tailscale";
        return b[0] == 10 || b[0] == 192 && b[1] == 168 || b[0] == 172 && b[1] is >= 16 and <= 31 ? "lan" : "other";
    }
}
