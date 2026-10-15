using System.Runtime.InteropServices;
namespace PCSetup.Launcher;
public static class LauncherTarget
{
    public static string FolderFor(Architecture architecture) => architecture switch
    {
        Architecture.X64 => "x64",
        Architecture.Arm64 => "arm64",
        _ => throw new PlatformNotSupportedException("Windows 11 x64 or ARM64 is required.")
    };
}
