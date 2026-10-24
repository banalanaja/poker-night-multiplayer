using System.Security.Cryptography;
using System.Text.Json;

namespace PokerNight.Multiplayer;

public sealed record GameBuildProfile(int SchemaVersion, int AppId, string SteamBuildId, string EngineBuild,
    string Executable, string Sha256, string Architecture, string LuaVersion, int LuaNumberBytes, int LuaSizeTBytes,
    bool RuntimeHooksVerified)
{
    public static GameBuildProfile Load(string path) => JsonSerializer.Deserialize<GameBuildProfile>(File.ReadAllText(path), WireCodec.Json)
        ?? throw new InvalidDataException("Perfil vazio.");
    public string Verify(string gameDirectory, bool requireRuntimeHooks = true)
    {
        if (SchemaVersion != 1 || Architecture != "x64" || Executable != "CelebrityPoker.exe") throw new InvalidDataException("Perfil não suportado.");
        string exe = Path.Combine(Path.GetFullPath(gameDirectory), Executable);
        using var stream = File.OpenRead(exe);
        string digest = Convert.ToHexString(SHA256.HashData(stream));
        if (!digest.Equals(Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Executável incompatível; nenhum hook foi aplicado.");
        if (requireRuntimeHooks && !RuntimeHooksVerified) throw new InvalidOperationException("Integração em runtime ainda não validada para esta build. Multiplayer indisponível.");
        return digest;
    }
}
