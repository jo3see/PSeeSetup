using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace PCSetup.Services;

public static class SnapdragonSupport
{
    public const string AppKey = "snapdragoncontrolpanel";
    public const string PackageName = "AdrenoControlPanel";
    public const string Publisher = "CN=Qualcomm Inc, O=Qualcomm Inc, STREET=5775 Morehouse Drive, L=San Diego, S=California, C=US, PostalCode=92129";
    public static readonly Version PackageVersion = new(2026, 3, 0, 0);

    public static bool IsEligible(Architecture architecture, string? name, string? vendor, int build) =>
        architecture == Architecture.Arm64 && build >= 26100 &&
        (name?.Contains("Snapdragon", StringComparison.OrdinalIgnoreCase) == true ||
         vendor?.Contains("Qualcomm", StringComparison.OrdinalIgnoreCase) == true);

    public static bool IsEligibleSystem()
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var cpu = root.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return IsEligible(ArchitecturePolicy.OSArchitecture,
                cpu?.GetValue("ProcessorNameString") as string, cpu?.GetValue("VendorIdentifier") as string,
                Environment.OSVersion.Version.Build);
        }
        catch (UnauthorizedAccessException) { return false; }
        catch (System.Security.SecurityException) { return false; }
    }

    public static bool HasRequiredRuntime(string output) => output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
        .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        .Any(parts => parts.Length >= 3 && parts[0] == "Microsoft.WindowsDesktop.App" &&
            Version.TryParse(parts[1], out var version) && version.Major == 9 && version.Minor == 0 &&
            version >= new Version(9, 0, 21));
}
