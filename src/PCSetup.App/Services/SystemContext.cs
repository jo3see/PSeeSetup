using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace PCSetup.Services;

public static class SystemContext
{
    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static string CurrentIdentity()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.Name;
    }

    public static string InteractiveIdentity()
    {
        var session = NativeMethods.WTSGetActiveConsoleSessionId();
        var user = Query(session, NativeMethods.WTS_INFO_CLASS.WTSUserName);
        var domain = Query(session, NativeMethods.WTS_INFO_CLASS.WTSDomainName);
        if (string.IsNullOrWhiteSpace(user)) return CurrentIdentity();
        return string.IsNullOrWhiteSpace(domain) ? user : $"{domain}\\{user}";
    }

    public static bool SameIdentity(string left, string right)
    {
        static string Normalize(string value) => value.Trim().Replace('/', '\\');
        return string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase)
               || string.Equals(Normalize(left).Split('\\').Last(), Normalize(right).Split('\\').Last(), StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSupportedWindows(out string reason)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            reason = "This application supports Windows 11 only.";
            return false;
        }
        if (!Environment.Is64BitOperatingSystem || !ArchitecturePolicy.IsSupported(RuntimeInformation.OSArchitecture))
        {
            reason = "PSeeSetup supports Windows 11 on x64 or ARM64. 32-bit Windows is not supported.";
            return false;
        }
        reason = $"Windows 11 {RuntimeInformation.OSArchitecture} detected (app: {RuntimeInformation.ProcessArchitecture}).";
        return true;
    }

    private static string Query(uint sessionId, NativeMethods.WTS_INFO_CLASS infoClass)
    {
        if (!NativeMethods.WTSQuerySessionInformation(IntPtr.Zero, sessionId, infoClass, out var buffer, out var bytes))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try { return bytes <= 2 ? "" : Marshal.PtrToStringUni(buffer) ?? ""; }
        finally { NativeMethods.WTSFreeMemory(buffer); }
    }

    private static class NativeMethods
    {
        internal enum WTS_INFO_CLASS { WTSUserName = 5, WTSDomainName = 7 }

        [DllImport("kernel32.dll")]
        internal static extern uint WTSGetActiveConsoleSessionId();

        [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WTSQuerySessionInformation(IntPtr server, uint sessionId,
            WTS_INFO_CLASS infoClass, out IntPtr buffer, out int bytesReturned);

        [DllImport("wtsapi32.dll")]
        internal static extern void WTSFreeMemory(IntPtr memory);
    }
}
