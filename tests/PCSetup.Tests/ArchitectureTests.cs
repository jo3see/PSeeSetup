using System.Runtime.InteropServices;
using PCSetup;
using PCSetup.Services;
using PCSetup.Launcher;

public static class ArchitectureTests
{
    public static IEnumerable<(string, Action)> All() => new (string, Action)[]
    {
        ("Native ARM integrations reject explicit x64 overrides", () =>
        {
            try
            {
                WingetService.BuildInstallArguments(Catalog.AppByKey("spacedesk"), architecture: Architecture.Arm64,
                    installerArchitecture: "x64");
                throw new Exception("Native driver architecture override accepted");
            }
            catch (InvalidOperationException) { }
        }),
        ("Store source lets Windows select the applicable package", () =>
        {
            var args = WingetService.BuildInstallArguments(Catalog.AppByKey("bluebubbles"), architecture: Architecture.Arm64);
            Check(args.Contains("msstore") && !args.Contains("--architecture") && !args.Contains("--scope"));
        }),
        ("Saved runs from another version stop before system work", () =>
        {
            using var logger = new RunLogger();
            var state = new SetupState
            {
                Phase = SetupPhase.Prepare,
                SetupVersion = "1.1.8",
                TargetArchitecture = ArchitecturePolicy.NativeArchitecture
            };
            var outcome = new SetupEngine(state, logger).RunAsync(null, CancellationToken.None).GetAwaiter().GetResult();
            Check(outcome == EngineOutcome.Failed && state.Steps.Count == 0 && state.StatePath.Length == 0);
        }),
        ("Architecture support and automatic launcher select native OS", () =>
        {
            Check(ArchitecturePolicy.IsSupported(Architecture.X64) && ArchitecturePolicy.IsSupported(Architecture.Arm64));
            Check(!ArchitecturePolicy.IsSupported(Architecture.X86));
            Check(LauncherTarget.FolderFor(Architecture.Arm64) == "arm64");
            Check(LauncherTarget.FolderFor(Architecture.X64) == "x64");
            try { LauncherTarget.FolderFor(Architecture.X86); throw new Exception("x86 accepted"); }
            catch (PlatformNotSupportedException) { }
        }),
        ("ARM preset retains all requested default applications", () =>
        {
            var arm = SetupProfile.DefaultSelection(Architecture.Arm64);
            foreach (var key in new[] { "nvcleanstall", "afterburner", "powerautomate", "ultraiso", "klite", "parsec", "logioptionsplus", "wireshark" })
                Check(arm.Applications.Contains(key), key);
            foreach (var key in new[] { "chrome", "equalizerapo", "spacedesk", "tailscale", "steam", "postman" })
                Check(arm.Applications.Contains(key), key);
            var x64 = SetupProfile.DefaultSelection(Architecture.X64);
            Check(x64.Applications.Contains("nvcleanstall"));
        }),
        ("All catalog apps are selectable with their ARM limitations visible", () =>
        {
            foreach (var key in new[] { "nvcleanstall", "afterburner", "powerautomate", "ultraiso", "klite", "parsec" })
            {
                Check(ArchitecturePolicy.For(Catalog.AppByKey(key), Architecture.Arm64).CanInstall);
                Check(ArchitecturePolicy.For(Catalog.AppByKey(key), Architecture.Arm64).Reason.Length > 40);
                WingetService.BuildInstallArguments(Catalog.AppByKey(key), architecture: Architecture.Arm64);
            }
        }),
        ("Native drivers use explicit ARM64 with no emulation downgrade", () =>
        {
            foreach (var key in new[] { "spacedesk", "tailscale", "equalizerapo", "7zip", "powershell" })
            {
                var args = WingetService.BuildInstallArguments(Catalog.AppByKey(key), architecture: Architecture.Arm64);
                Check(Value(args, "--architecture") == "arm64");
                Check(Value(args, "--scope") == "machine");
                Check(!args.Contains("--ignore-security-hash"));
            }
        }),
        ("Native query matches installer scope and architecture", () =>
        {
            var app = Catalog.AppByKey("postman");
            var query = WingetService.BuildShowArguments(app, Architecture.Arm64, "arm64");
            Check(query[0] == "show" && Value(query, "--architecture") == "arm64");
            Check(Value(query, "--scope") == "user");
            Check(!query.Contains("--silent") && !query.Contains("--accept-package-agreements"));
            var install = WingetService.BuildInstallArguments(app, true, Architecture.Arm64);
            Check(Value(install, "--architecture") == "x64" && install.Contains("--force"));
        }),
        ("OBS ARM portable package is not forced into machine scope", () =>
        {
            var args = WingetService.BuildInstallArguments(Catalog.AppByKey("obs"), architecture: Architecture.Arm64);
            Check(Value(args, "--architecture") == "arm64" && !args.Contains("--scope"));
        }),
        ("Fallbacks select ARM64 payload and independently pinned hashes", () =>
        {
            foreach (var key in new[] { "equalizerapo", "jdownloader2", "translucenttb" })
            {
                var app = Catalog.AppByKey(key);
                var fallback = ArchitecturePolicy.FallbackFor(app, Architecture.Arm64)!;
                Check(fallback is not null && !fallback.Url.Contains("x64", StringComparison.OrdinalIgnoreCase));
                if (key != "translucenttb") Check(fallback!.ExpectedSha256?.Length == 64);
                Check(ArchitecturePolicy.FallbackFor(app, Architecture.X64) == app.Fallback);
            }
            Check(ArchitecturePolicy.FallbackFor(Catalog.AppByKey("parsec"), Architecture.Arm64)!.SilentArguments.Contains("/nodrivers"));
        }),
        ("Unknown new catalog items are held for verification on ARM", () =>
        {
            var app = new AppDefinition("new-driver", "New driver", "Test", "Test.Driver", InstallContext.Machine, true, "Test");
            Check(ArchitecturePolicy.For(app, Architecture.Arm64).Kind == CompatibilityKind.NeedsVerification);
            Check(!ArchitecturePolicy.For(app, Architecture.Arm64).CanInstall);
        }),
        ("ARM WinGet discovery uses native App Installer package name", () =>
        {
            Check(ArchitecturePolicy.AppInstallerPattern(Architecture.Arm64).Contains("_arm64__"));
            Check(ArchitecturePolicy.AppInstallerPattern(Architecture.X64).Contains("_x64__"));
        }),
        ("PE inspection recognizes ARM64 and rejects malformed binary", () =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe");
            try
            {
                var bytes = new byte[128];
                bytes[0] = 0x4D; bytes[1] = 0x5A; bytes[0x3C] = 64;
                bytes[64] = 0x50; bytes[65] = 0x45; bytes[68] = 0x64; bytes[69] = 0xAA;
                File.WriteAllBytes(path, bytes);
                Check(BinaryArchitecture.Read(path) == Architecture.Arm64);
                bytes[0x3C] = 127; File.WriteAllBytes(path, bytes);
                Check(BinaryArchitecture.Read(path) is null);
            }
            finally { File.Delete(path); }
        })
    };
    private static void Check(bool condition, string message = "Architecture safeguard failed")
    {
        if (!condition) throw new Exception(message);
    }
    private static string? Value(IReadOnlyList<string> args, string option)
    {
        for (var i = 0; i < args.Count - 1; i++) if (args[i] == option) return args[i + 1];
        return null;
    }
}
