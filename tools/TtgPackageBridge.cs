// Adapter to an independently installed TTG Tools 1.16.1. No third-party implementation is embedded.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Text;

class TtgPackageBridge {
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static Assembly tool;
    static byte[] FindKey(string descriptor) {
        byte[] encrypted = File.ReadAllBytes(descriptor);
        var decrypt = tool.GetType("TTG_Tools.Methods").GetMethod("decryptLua", All);
        foreach (var type in tool.GetTypes()) foreach (var field in type.GetFields(All)) {
            if ((field.Attributes & FieldAttributes.HasFieldRVA) == 0) continue;
            int size;
            try { size = Marshal.SizeOf(field.FieldType); } catch { continue; }
            if (size < 4 || size > 56) continue;
            var candidate = new byte[size];
            try {
                RuntimeHelpers.InitializeArray(candidate, field.FieldHandle);
                byte[] plain = (byte[])decrypt.Invoke(null, new object[] { (byte[])encrypted.Clone(), candidate, 7 });
                string text = Encoding.UTF8.GetString(plain);
                if (text.Contains("RegisterSetDescription") && text.Contains("CP_pc_Project_compressed.ttarch2")) return candidate;
            } catch (TargetInvocationException) { }
        }
        throw new InvalidDataException("The external tool could not identify this game's descriptor key.");
    }
    static void ExtractOriginal(string game, string output, byte[] key) {
        var type = tool.GetType("TTG_Tools.ArchiveUnpacker");
        var instance = Activator.CreateInstance(type);
        var archiveType = tool.GetType("TTG_Tools.ClassesStructs.Ttarch2Class");
        type.GetField("ttarch2", All).SetValue(null, Activator.CreateInstance(archiveType));
        string archivePath = Path.Combine(game, "Archives", "CP_pc_Project_compressed.ttarch2");
        type.GetMethod("ReadHeaderTtarch2", All).Invoke(instance, new object[] { archivePath, key });
        var archive = type.GetField("ttarch2", All).GetValue(null);
        var entries = (Array)archiveType.GetField("files", All).GetValue(archive);
        using (var reader = new BinaryReader(File.OpenRead(archivePath))) foreach (var entry in entries) {
            string name = (string)entry.GetType().GetField("fileName", All).GetValue(entry);
            if (!String.Equals(name, "_system.lua", StringComparison.OrdinalIgnoreCase)) continue;
            byte[] data = (byte[])type.GetMethod("getTtarch2File", All).Invoke(null, new object[] { archive, entry, key, reader });
            data = (byte[])tool.GetType("TTG_Tools.Methods").GetMethod("decryptLua", All).Invoke(null, new object[] { data, key, 7 });
            if (data.Length < 18 || data[0] != 27 || data[1] != 76 || data[2] != 117 || data[3] != 97 || data[4] != 82)
                throw new InvalidDataException("Unexpected original script format.");
            File.WriteAllBytes(output, data);
            return;
        }
        throw new InvalidDataException("Original system script not found.");
    }
    static int Main(string[] args) {
        try {
            if (args.Length != 7) throw new ArgumentException("tool.exe game-directory compiled-scripts archive-out descriptor-source descriptor-out extract|pack");
            string directory = Path.GetDirectoryName(Path.GetFullPath(args[0]));
            AppDomain.CurrentDomain.AssemblyResolve += (s, e) => {
                string path = Path.Combine(directory, new AssemblyName(e.Name).Name + ".dll");
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };
            tool = Assembly.LoadFrom(args[0]);
            byte[] key = FindKey(Path.Combine(args[1], "Archives", "_resdesc_50_Project.lua"));
            if (args[6] == "extract") {
                ExtractOriginal(args[1], Path.Combine(args[2], "PNM_OriginalSystem.lua"), key);
                Console.WriteLine("Original bytecode extracted locally; no key or game script is distributed.");
            } else if (args[6] == "pack") {
                var builder = tool.GetType("TTG_Tools.ModCreator").GetMethod("ttarch2BuilderLegacy1132", All);
                builder.Invoke(null, new object[] { args[2], args[3], true, true, true, key, 2, true, new Action<int>(n => { }),
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase) });
                byte[] descriptor = File.ReadAllBytes(args[4]);
                descriptor = (byte[])tool.GetType("TTG_Tools.Methods").GetMethod("encryptLua", All)
                    .Invoke(null, new object[] { descriptor, key, true, 7 });
                File.WriteAllBytes(args[5], descriptor);
                Console.WriteLine("Compiled script package built.");
            } else throw new ArgumentException("Unknown operation.");
            return 0;
        } catch (Exception error) {
            while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
            Console.Error.WriteLine(error.Message); return 1;
        }
    }
}
