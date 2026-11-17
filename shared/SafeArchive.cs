using System.IO;
using System.IO.Compression;

namespace Leitor.Security;
public static class SafeArchive
{
    public static string Resolve(string directory, string entryName)
    {
        string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (entryName.Contains(':') || Path.IsPathRooted(entryName)) throw new InvalidDataException("Caminho absoluto no pacote.");
        string target = Path.GetFullPath(Path.Combine(root, entryName.Replace('/', Path.DirectorySeparatorChar)));
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("O pacote tenta sair do diretório autorizado.");
        for (string? part = target; part != null && part.Length >= root.TrimEnd(Path.DirectorySeparatorChar).Length; part = Path.GetDirectoryName(part))
            if ((File.Exists(part) || Directory.Exists(part)) && (File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Links não são permitidos no destino.");
        return target;
    }
    public static void Extract(ZipArchive archive, string directory)
    {
        // Validate every entry before touching an existing installation.
        foreach (var entry in archive.Entries)
        { Resolve(directory, entry.FullName); if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException("Link simbólico no pacote."); }
        foreach (var entry in archive.Entries)
        {
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException("Link simbólico no pacote.");
            string target = Resolve(directory, entry.FullName);
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); entry.ExtractToFile(target, true);
        }
    }
}
