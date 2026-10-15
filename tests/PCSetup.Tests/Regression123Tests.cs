using PCSetup;
using PCSetup.Services;
using System.Runtime.InteropServices;

public static class Regression123Tests
{
    private static ProcessResult Package() => new(0,
        "Installer Url: https://vendor.example/setup.exe\nInstaller SHA256: " + new string('B', 64), "");
    private static ProcessResult NoPackage() => new(0, "No applicable installer found", "");
    public static IEnumerable<(string, Action)> All() => new (string, Action)[]
    {
        ("Log opening uses the exact file and default editor without launching during tests", () =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".log");
            try
            {
                File.WriteAllText(path, "Read-only log fixture");
                var start = LogFileService.BuildOpenInfo(path);
                Check(start.UseShellExecute && start.FileName == Path.GetFullPath(path) && start.ArgumentList.Count == 0);
            }
            finally { File.Delete(path); }
            try { LogFileService.BuildOpenInfo(path); } catch (FileNotFoundException) { return; }
            throw new Exception("Missing log was accepted");
        }),
        ("Installed BlueBubbles Store package skips duplicate installation", () =>
        {
            using var logger = new RunLogger();
            var process = new ProcessRunner(logger);
            var winget = new WingetService(process, logger, () => "fake", (_, _, _, _) => throw new Exception("BlueBubbles must not reinstall"));
            var installer = new ApplicationInstaller(winget, new DownloadService(process, logger), process, logger,
                bluebubblesStoreInstalled: () => true);
            Check(installer.IsInstalled(Catalog.AppByKey("bluebubbles")));
            Check(installer.InstallAsync(Catalog.AppByKey("bluebubbles"), "unused", CancellationToken.None).GetAwaiter().GetResult() == ResultKind.Skipped);
        }),
        ("Parsec ARM installs explicitly exclude bundled virtual drivers", () =>
        {
            var app = Catalog.AppByKey("parsec");
            var install = WingetService.BuildInstallArguments(app, architecture: Architecture.Arm64);
            Check(install.Contains("--custom") && install.Any(x => x.Contains("/nodrivers")));
            var query = WingetService.BuildShowArguments(app, Architecture.Arm64, "x64");
            Check(!query.Contains("--custom") && !query.Any(x => x.Contains("/nodrivers")));
            Check(ArchitecturePolicy.FallbackFor(app, Architecture.Arm64)!.SilentArguments.Contains("/nodrivers"));
        }),
        ("Every catalog application is selectable on ARM64", () =>
        {
            foreach (var app in Catalog.Applications) Check(ArchitecturePolicy.For(app, Architecture.Arm64).CanInstall);
        }),
        ("Intel PCs select x64/x86 only and reject an ARM override", () =>
        {
            foreach (var app in Catalog.Applications)
            {
                Check(ArchitecturePolicy.InstallerArchitectures(app, Architecture.X64).SequenceEqual(["x64", "x86"]));
                var args = WingetService.BuildInstallArguments(app, architecture: Architecture.X64);
                Check(!args.Contains("arm64"));
                try { WingetService.BuildInstallArguments(app, architecture: Architecture.X64, installerArchitecture: "arm64"); }
                catch (InvalidOperationException) { continue; }
                throw new Exception("Intel PC accepted ARM64 override");
            }
        }),
        ("ARM ordinary apps try native then x64 then x86", () =>
        {
            foreach (var key in new[] { "parsec", "logioptionsplus", "bluebubbles", "teamviewer", "wireshark", "balenaetcher", "rpiimager", "unraidusb", "rainmeter", "chrome", "ultraiso" })
                Check(ArchitecturePolicy.InstallerArchitectures(Catalog.AppByKey(key), Architecture.Arm64).SequenceEqual(["arm64", "x64", "x86"]));
            foreach (var key in new[] { "equalizerapo", "spacedesk", "tailscale" })
                Check(ArchitecturePolicy.InstallerArchitectures(Catalog.AppByKey(key), Architecture.Arm64).SequenceEqual(["arm64"]));
        }),
        ("Unavailable ARM package falls back to x64 and records selection", () =>
        {
            if (ArchitecturePolicy.OSArchitecture != Architecture.Arm64) return;
            using var log = new RunLogger();
            var calls = new List<string[]>();
            var winget = new WingetService(new ProcessRunner(log), log, () => "fake",
                (_, args, _, _) => { calls.Add(args.ToArray()); return Task.FromResult(args[0] == "show" && args.Contains("arm64") ? NoPackage() : Package()); });
            Check(winget.InstallAsync(Catalog.AppByKey("logioptionsplus"), false, CancellationToken.None).GetAwaiter().GetResult().Success);
            Check(calls.Count == 3 && calls.Last()[0] == "install" && calls.Last().Contains("x64"));
            Check(winget.LastInstallerArchitecture == "x64");
        }),
        ("Native vendor apps precede emulation; unavailable native download falls back", () =>
        {
            if (ArchitecturePolicy.OSArchitecture != Architecture.Arm64) return;
            using var log = new RunLogger();
            var process = new ProcessRunner(log);
            var calls = 0;
            var winget = new WingetService(process, log, () => "fake", (_, args, _, _) =>
            {
                calls++;
                return Task.FromResult(args[0] == "show" && args.Contains("arm64") ? NoPackage() : Package());
            });
            var native = new ApplicationInstaller(winget, new DownloadService(process, log), process, log,
                isInstalled: _ => false, installFallback: (_, fallback, _, _) =>
                { Check(fallback.FileName.Contains("ARM64")); return Task.FromResult(ResultKind.Succeeded); });
            Check(native.InstallAsync(Catalog.AppByKey("teamviewer"), "unused", CancellationToken.None).GetAwaiter().GetResult() == ResultKind.Succeeded);
            Check(calls == 0 && native.LastInstallerArchitecture!.StartsWith("arm64"));
            var failedNative = new ApplicationInstaller(winget, new DownloadService(process, log), process, log,
                isInstalled: _ => false, installFallback: (_, _, _, _) => throw new InvalidDataException("Unavailable native download"));
            Check(failedNative.InstallAsync(Catalog.AppByKey("wireshark"), "unused", CancellationToken.None).GetAwaiter().GetResult() == ResultKind.Succeeded);
            Check(calls == 3 && failedNative.LastInstallerArchitecture == "x64");
        }),
        ("Tor portable detection uses actual Desktop including redirected folders", () =>
        {
            var app = Catalog.AppByKey("tor");
            Check(app.RequireKnownInstallPath);
            var redirected = Path.Combine(Path.GetTempPath(), "RedirectedDesktop");
            Check(ApplicationInstaller.KnownInstallPaths(app, redirected).Contains(Path.Combine(redirected, "Tor Browser", "Browser", "firefox.exe")));
            Check(!ApplicationInstaller.WingetInstallCountsAsCompleted(app, new ProcessResult(0, "", ""), false));
            Check(ApplicationInstaller.WingetInstallCountsAsCompleted(app, new ProcessResult(0, "", ""), true));
        }),
        ("Presence verification keeps preinstalled apps classified as skipped", () =>
        {
            var state = new SetupState { Steps = [new() { Key = "app:spotify", Result = ResultKind.Skipped }] };
            state.ApplicationVerification["spotify"] = ResultKind.Succeeded;
            Check(RunResults.Final(state)["app:spotify"] == ResultKind.Skipped && !RunResults.HasIssues(state));
            state.ApplicationVerification["spotify"] = ResultKind.Failed;
            Check(RunResults.Final(state)["app:spotify"] == ResultKind.Failed && RunResults.HasIssues(state));
        }),
        ("Unraid x86 bootstrapper does not invent a manifest scope", () =>
        {
            var args = WingetService.BuildInstallArguments(Catalog.AppByKey("unraidusb"), architecture: Architecture.Arm64, installerArchitecture: "x86");
            Check(!args.Contains("--scope") && args.Contains("x86"));
        })
    };
    private static void Check(bool condition) { if (!condition) throw new Exception("1.2.3 regression safeguard failed"); }
}
