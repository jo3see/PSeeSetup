using System.Runtime.InteropServices;
using System.Text;

namespace PCSetup.Services;

public static class MsiPackage
{
    // Read-only summary metadata. This does not install or repair products.
    public static Architecture? ReadArchitecture(string path)
    {
        if (MsiGetSummaryInformation(0, Path.GetFullPath(path), 0, out var summary) != 0) return null;
        try
        {
            var length = 256u;
            var template = new StringBuilder((int)length);
            if (MsiSummaryInfoGetProperty(summary, 7, out _, out _, out _, template, ref length) != 0) return null;
            return template.ToString().Split(';')[0] switch
            {
                "Arm64" => Architecture.Arm64,
                "x64" => Architecture.X64,
                "Intel" => Architecture.X86,
                _ => null
            };
        }
        finally { MsiCloseHandle(summary); }
    }

    public static string[] SilentInstallArguments(string package, string log) =>
        ["/i", Path.GetFullPath(package), "/qn", "/norestart", "/L*v", Path.GetFullPath(log)];

    [DllImport("msi.dll", EntryPoint = "MsiGetSummaryInformationW", CharSet = CharSet.Unicode)]
    private static extern uint MsiGetSummaryInformation(uint database, string path, uint updateCount, out uint summary);
    [DllImport("msi.dll", EntryPoint = "MsiSummaryInfoGetPropertyW", CharSet = CharSet.Unicode)]
    private static extern uint MsiSummaryInfoGetProperty(uint summary, uint property, out uint dataType,
        out int integer, out long fileTime, StringBuilder text, ref uint length);
    [DllImport("msi.dll")]
    private static extern uint MsiCloseHandle(uint handle);
}
