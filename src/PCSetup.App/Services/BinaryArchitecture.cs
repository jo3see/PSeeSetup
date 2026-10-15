using System.Runtime.InteropServices;

namespace PCSetup.Services;

public static class BinaryArchitecture
{
    public static Architecture? Read(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            if (stream.Length < 64 || reader.ReadUInt16() != 0x5A4D) return null;
            stream.Position = 0x3C;
            var offset = reader.ReadInt32();
            if (offset < 64 || offset > stream.Length - 6) return null;
            stream.Position = offset;
            if (reader.ReadUInt32() != 0x00004550) return null;
            return reader.ReadUInt16() switch
            {
                0xAA64 => Architecture.Arm64,
                0x8664 => Architecture.X64,
                0x014C => Architecture.X86,
                _ => null
            };
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public static IEnumerable<string> ExpandInstallPaths(IEnumerable<string> templates)
    {
        foreach (var template in templates)
        {
            yield return Environment.ExpandEnvironmentVariables(template);
            if (template.Contains("%ProgramFiles%", StringComparison.OrdinalIgnoreCase))
                yield return template.Replace("%ProgramFiles%",
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), StringComparison.OrdinalIgnoreCase);
        }
    }
}
