using System.Runtime.InteropServices;

namespace PCSetup.Services;

public enum CompatibilityKind { Native, Emulated, Skipped, NeedsVerification }

public sealed record InstallPlan(CompatibilityKind Kind, string? Architecture, string Reason)
{
    public bool CanInstall => Kind is CompatibilityKind.Native or CompatibilityKind.Emulated;
    public string Label => Kind switch
    {
        CompatibilityKind.Native => "Native",
        CompatibilityKind.Emulated => Architecture == "arm64" ? "Native preferred" : "Emulation allowed",
        CompatibilityKind.Skipped => "Skipped",
        _ => "Needs verification"
    };
}

public static class ArchitecturePolicy
{
    public static Architecture OSArchitecture => RuntimeInformation.OSArchitecture;
    public static string NativeArchitecture => OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
    public static string AppInstallerPattern(Architecture architecture) =>
        $"Microsoft.DesktopAppInstaller_*_{(architecture == Architecture.Arm64 ? "arm64" : "x64")}__8wekyb3d8bbwe";
    public static bool IsSupported(Architecture architecture) => architecture is Architecture.X64 or Architecture.Arm64;

    private static readonly HashSet<string> NativeArm = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "brave", "teams", "slack", "claude", "vscode", "powershell", "powertoys",
        "equalizerapo", "ytdlp", "audacity", "obs", "vlc", "spotify", "paintdotnet", "putty",
        "tailscale", "spacedesk", "jdownloader2", "orcaslicer", "7zip", "translucenttb",
        "dotnet10", "dotnet9", "dotnet8", "snapdragoncontrolpanel"
    };
    private static readonly HashSet<string> RequiredNativeArm = new(StringComparer.OrdinalIgnoreCase)
    {
        "equalizerapo", "spacedesk", "tailscale", "powertoys", "translucenttb", "powershell",
        "dotnet10", "dotnet9", "dotnet8", "snapdragoncontrolpanel"
    };
    private static readonly Dictionary<string, string> UnsupportedArm = new(StringComparer.OrdinalIgnoreCase)
    {
        ["nvcleanstall"] = "Installation can be attempted; NVIDIA drivers cannot operate Snapdragon/Adreno hardware.",
        ["afterburner"] = "Installation can be attempted; Intel-only GPU tuning drivers cannot operate on ARM64.",
        ["powerautomate"] = "Installation can be attempted under emulation; the vendor does not support ARM processors.",
        ["ultraiso"] = "The app may run under emulation; an Intel-only virtual-drive driver cannot work on ARM64.",
        ["klite"] = "Intel codecs may serve emulated players; native ARM64 Explorer integrations are not established."
    };
    private static readonly Dictionary<string, string> VerifyArm = new(StringComparer.OrdinalIgnoreCase)
    {
        ["parsec"] = "Runs the app without bundled virtual drivers on ARM. Snapdragon hosting and GPU acceleration require testing.",
        ["logioptionsplus"] = "The web installer's device integrations and ARM payload need separate verification.",
        ["bluebubbles"] = "Microsoft Store availability and this x64 client's ARM operation need verification.",
        ["teamviewer"] = "Official ARM64 installer is preferred before emulation; remote-control features require testing.",
        ["wireshark"] = "Official ARM64 installer is preferred. Live capture requires separate ARM64 Npcap; silent setup does not install it.",
        ["balenaetcher"] = "The disk-writing helper needs validation on this ARM device.",
        ["rpiimager"] = "Elevated disk-writing functionality needs validation on this ARM device.",
        ["unraidusb"] = "The disk-writing helpers need validation on this ARM device.",
        ["rainmeter"] = "Hardware-monitoring plug-ins and downloaded native DLLs need verification."
    };

    public static InstallPlan For(AppDefinition app, Architecture? architecture = null, bool? snapdragonEligible = null)
    {
        var os = architecture ?? OSArchitecture;
        if (app.Key == SnapdragonSupport.AppKey)
            return os == Architecture.Arm64 && (snapdragonEligible ?? SnapdragonSupport.IsEligibleSystem())
                ? new(CompatibilityKind.Native, "arm64", "Snapdragon ARM64 only; installs .NET 9 Desktop Runtime before Qualcomm's signed MSIX.")
                : new(CompatibilityKind.Skipped, null, "Requires a detected Qualcomm Snapdragon ARM64 processor and Windows 11 24H2 or later.");
        if (!IsSupported(os)) return new(CompatibilityKind.Skipped, null, "Unsupported Windows architecture.");
        if (os == Architecture.X64) return new(CompatibilityKind.Native, "x64", "Selects x64 first, then x86 when that is the available Intel package; never selects ARM64.");
        if (!Catalog.Applications.Any(x => x.Key.Equals(app.Key, StringComparison.OrdinalIgnoreCase)))
            return new(CompatibilityKind.NeedsVerification, null, "This application is not in the reviewed catalog.");
        if (RequiredNativeArm.Contains(app.Key)) return new(CompatibilityKind.Native, "arm64", "Requires native ARM64 drivers, shell integration or setup runtime; Intel kernel drivers cannot be emulated.");
        var reason = UnsupportedArm.GetValueOrDefault(app.Key) ?? VerifyArm.GetValueOrDefault(app.Key)
            ?? (app.Key == "powerbi" ? "Requires Windows updates including KB5065789 or a superseding update."
                : "Application functionality, plug-ins and game anti-cheat require testing under emulation.");
        return new(CompatibilityKind.Emulated, NativeArm.Contains(app.Key) ? "arm64" : "x64",
            "Prefers ARM64, then x64, then x86 when available. " + reason);
    }

    public static IReadOnlyList<string> InstallerArchitectures(AppDefinition app, Architecture? architecture = null) =>
        (architecture ?? OSArchitecture) == Architecture.Arm64
            ? RequiredNativeArm.Contains(app.Key) ? ["arm64"] : ["arm64", "x64", "x86"]
            : ["x64", "x86"];

    public static VendorFallback? PreferredArmFallbackFor(AppDefinition app) => app.Key switch
    {
        "teamviewer" => new("https://download.teamviewer.com/download/version_15x/update/Update_msi_15.82.6_arm64.zip",
            "TeamViewer-15.82.6-ARM64.zip", "", ["TeamViewer Germany GmbH"],
            "24DA6DF7A481008FF85995E7F58CD25A867E639C0C2C3F8B963FBC63147A1582",
            MsiArchiveEntry: "update.msi"),
        "wireshark" => new("https://2.na.dl.wireshark.org/win64/Wireshark-4.6.9-arm64.exe",
            "Wireshark-4.6.9-arm64.exe", "/S", ["Wireshark Foundation"],
            "E25B68C0A7D8CCC03A325235D1734D71998AF30A0136DC352F3EA5CE0EEFAFC4"),
        _ => null
    };

    public static VendorFallback? FallbackFor(AppDefinition app, Architecture? architecture = null)
    {
        if ((architecture ?? OSArchitecture) != Architecture.Arm64) return app.Fallback;
        if (!For(app, Architecture.Arm64).CanInstall) return null;
        return app.Key switch
        {
            "equalizerapo" => new("https://sourceforge.net/projects/equalizerapo/files/1.4.2/EqualizerAPO-ARM64-1.4.2.exe/download",
                "EqualizerAPO-ARM64-1.4.2.exe", "/S", ["Jonas Thedering", "Open Source Developer"],
                "0DBFC64DEE60F94CF77B297FEB1F54D2633D49669BB474086FFFB8DB23E8F066", true),
            "jdownloader2" => new("https://installer.jdownloader.org/winget/2026-03-31/JDownloader2Setup_windows-aarch64_v21_0_10.exe",
                "JDownloader2Setup_windows-aarch64_v21_0_10.exe", "-q", ["AppWork GmbH"],
                "497B4490B2A7673BFBFD4FE084EF09DBEA909B6CEE985DEDDAC2DCCB6EA73E3D"),
            "translucenttb" => app.Fallback! with
            {
                Url = "https://github.com/TranslucentTB/TranslucentTB/releases/latest/download/TranslucentTB-portable-arm64.zip",
                FileName = "TranslucentTB-portable-arm64.zip"
            },
            "teams" or "unraidusb" => app.Fallback,
            "parsec" => app.Fallback! with { SilentArguments = app.Fallback!.SilentArguments + " /nodrivers" },
            _ => null
        };
    }

    public static void ApplyDefaults(SetupProfile profile, Architecture? architecture = null)
    {
        if ((architecture ?? OSArchitecture) != Architecture.Arm64) return;
        foreach (var app in Catalog.Applications.Where(x => !For(x, Architecture.Arm64).CanInstall))
            profile.Applications.Remove(app.Key);
    }
}
