using PCSetup;
using PCSetup.Services;
using System.Runtime.InteropServices;

public static class SnapdragonTests
{
    public static IEnumerable<(string, Action)> All() => new (string, Action)[]
    {
        ("Snapdragon dependency reboot state survives saving without marking the control panel complete", () =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
            try
            {
                var state = new SetupState { StatePath = path, SetupVersion = "1.2.7", Phase = SetupPhase.InstallUserApplications,
                    Profile = new SetupProfile { Applications = [SnapdragonSupport.AppKey] },
                    Steps = [new StepRecord { Key = "dependency:snapdragon:dotnet9", Result = ResultKind.RebootRequired }] };
                StateStore.Save(state);
                var loaded = StateStore.Load(path);
                Check(loaded.Phase == SetupPhase.InstallUserApplications && loaded.Profile.Applications.Contains(SnapdragonSupport.AppKey));
                Check(loaded.Steps.Single().Result == ResultKind.RebootRequired && !loaded.Steps.Any(x => x.Key == "app:" + SnapdragonSupport.AppKey));
            }
            finally { File.Delete(path); }
        }),
        ("Snapdragon hardware gating covers x64, other ARM CPUs, old Windows and unknown CPU", () =>
        {
            Check(SnapdragonSupport.IsEligible(Architecture.Arm64, "Snapdragon(R) X2 Elite", "Qualcomm Technologies Inc", 26100));
            Check(SnapdragonSupport.IsEligible(Architecture.Arm64, "Oryon", "Qualcomm Technologies Inc", 26200));
            Check(!SnapdragonSupport.IsEligible(Architecture.X64, "Snapdragon", "Qualcomm", 26100));
            Check(!SnapdragonSupport.IsEligible(Architecture.Arm64, "Other ARM", "Other", 26100));
            Check(!SnapdragonSupport.IsEligible(Architecture.Arm64, "Snapdragon", "Qualcomm", 22631));
            Check(!SnapdragonSupport.IsEligible(Architecture.Arm64, null, null, 26100));
        }),
        ("Control Panel remains optional and requires ARM64 with no WinGet fallback", () =>
        {
            var app = Catalog.AppByKey(SnapdragonSupport.AppKey);
            Check(!app.DefaultSelection && app.Context == InstallContext.User && app.WingetId == "");
            Check(!SetupProfile.DefaultSelection(Architecture.Arm64).Applications.Contains(app.Key));
            Check(ArchitecturePolicy.For(app, Architecture.Arm64, true).CanInstall);
            Check(!ArchitecturePolicy.For(app, Architecture.Arm64, false).CanInstall);
            Check(!ArchitecturePolicy.For(app, Architecture.X64, true).CanInstall);
            Check(ArchitecturePolicy.InstallerArchitectures(app, Architecture.Arm64).SequenceEqual(["arm64"]));
            ExpectFailure(() => WingetService.BuildInstallArguments(app, architecture: Architecture.X64));
        }),
        ("Desktop Runtime requires stable 9.0.21 or newer 9.0, not SDK, Core, preview or .NET 10", () =>
        {
            Check(SnapdragonSupport.HasRequiredRuntime("Microsoft.WindowsDesktop.App 9.0.21 [C:\\Program Files\\dotnet]"));
            Check(SnapdragonSupport.HasRequiredRuntime("Microsoft.WindowsDesktop.App 9.0.22 [path]"));
            foreach (var output in new[] { "", "Microsoft.NETCore.App 9.0.21 [path]", "Microsoft.WindowsDesktop.App 9.0.20 [path]",
                         "Microsoft.WindowsDesktop.App 10.0.13 [path]", "Microsoft.WindowsDesktop.App 9.0.22-preview [path]", "9.0.319 [sdk]" })
                Check(!SnapdragonSupport.HasRequiredRuntime(output));
        }),
        ("Runtime and hardware failures stop Control Panel before downloads or execution", () =>
        {
            using var log = new RunLogger();
            var process = new ProcessRunner(log);
            var service = new SnapdragonInstaller(new DownloadService(process, log), process, log,
                eligible: () => true, runtimePresent: () => false,
                download: (_, _, _) => throw new Exception("Unexpected download"));
            ExpectFailure(() => service.InstallAsync("unused", default).GetAwaiter().GetResult());
            var ineligible = new SnapdragonInstaller(new DownloadService(process, log), process, log,
                eligible: () => false, runtimePresent: () => true,
                download: (_, _, _) => throw new Exception("Unexpected download"));
            ExpectFailure(() => ineligible.EnsureRuntimeAsync("unused", default).GetAwaiter().GetResult());
            ExpectFailure(() => ineligible.InstallAsync("unused", default).GetAwaiter().GetResult());
        }),
        ("Existing ARM64 runtime is reused without downloads", () =>
        {
            using var log = new RunLogger();
            var process = new ProcessRunner(log);
            var service = new SnapdragonInstaller(new DownloadService(process, log), process, log,
                eligible: () => true, runtimePresent: () => true,
                download: (_, _, _) => throw new Exception("Existing runtime downloaded again"));
            Check(service.EnsureRuntimeAsync("unused", default).GetAwaiter().GetResult() == ResultKind.Skipped);
        }),
        ("Runtime success is verified and reboot requests propagate before package installation", () =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe");
            try
            {
                var bytes = new byte[128];
                bytes[0] = 0x4D; bytes[1] = 0x5A; bytes[0x3C] = 64;
                // ARM64 .NET's official Burn bootstrapper is an x86 PE.
                bytes[64] = 0x50; bytes[65] = 0x45; bytes[68] = 0x4C; bytes[69] = 0x01;
                File.WriteAllBytes(path, bytes);
                using var log = new RunLogger();
                var process = new ProcessRunner(log);
                foreach (var exit in new[] { 0, 3010, 1603 })
                {
                    var ready = false;
                    var service = new SnapdragonInstaller(new DownloadService(process, log), process, log,
                        eligible: () => true, runtimePresent: () => ready,
                        download: (fallback, _, _) => { Check(fallback == SnapdragonInstaller.Runtime); return Task.FromResult(path); },
                        run: (_, args, _) => { Check(args.Contains("/norestart")); ready = exit == 0; return Task.FromResult(new ProcessResult(exit, "", "")); });
                    if (exit == 1603) ExpectFailure(() => service.EnsureRuntimeAsync("unused", default).GetAwaiter().GetResult());
                    else Check(service.EnsureRuntimeAsync("unused", default).GetAwaiter().GetResult() ==
                        (exit == 3010 ? ResultKind.RebootRequired : ResultKind.Succeeded));
                }
                var unconfirmed = new SnapdragonInstaller(new DownloadService(process, log), process, log,
                    eligible: () => true, runtimePresent: () => false, download: (_, _, _) => Task.FromResult(path),
                    run: (_, _, _) => Task.FromResult(new ProcessResult(0, "", "")));
                ExpectFailure(() => unconfirmed.EnsureRuntimeAsync("unused", default).GetAwaiter().GetResult());
            }
            finally { File.Delete(path); }
        }),
        ("Missing or forged Qualcomm package is rejected before deployment", () =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zip");
            try
            {
                using (var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create))
                using (var writer = new StreamWriter(zip.CreateEntry("AppxManifest.xml").Open()))
                    writer.Write("<Package xmlns='http://schemas.microsoft.com/appx/manifest/foundation/windows10'><Identity Name='AdrenoControlPanel' ProcessorArchitecture='x64'/></Package>");
                ExpectInvalid(() => SnapdragonInstaller.ExtractPackage(path));
                ExpectInvalid(() => SnapdragonInstaller.ValidateManifest(path));
            }
            finally { File.Delete(path); }
        })
    };
    private static void Check(bool condition) { if (!condition) throw new Exception("Snapdragon check failed"); }
    private static void ExpectFailure(Action action)
    {
        try { action(); } catch (InvalidOperationException) { return; }
        throw new Exception("Expected rejection did not occur");
    }
    private static void ExpectInvalid(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new Exception("Invalid package was accepted");
    }
}
