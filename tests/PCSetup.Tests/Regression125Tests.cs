using System.IO.Compression;
using System.Runtime.InteropServices;
using PCSetup;
using PCSetup.Services;

internal static class Regression125Tests
{
    public static IEnumerable<(string Name, Action Test)> All()
    {
        yield return ("Unraid uses a pinned signed versioned vendor release on both architectures", () =>
        {
            var app = Catalog.AppByKey("unraidusb");
            foreach (var architecture in new[] { Architecture.X64, Architecture.Arm64 })
            {
                var fallback = ArchitecturePolicy.FallbackFor(app, architecture)!;
                Check(fallback.Url.Contains("/releases/download/v2.0.11.1-unraid.0/"));
                Check(fallback.ExpectedSha256?.Length == 64 && !fallback.AllowPinnedHashWithoutSignature);
                Check(fallback.SilentArguments.Contains("/VERYSILENT") && fallback.SilentArguments.Contains("/NORESTART"));
            }
            using var log = new RunLogger();
            var process = new ProcessRunner(log);
            var winget = new WingetService(process, log, () => "fake", (_, _, _, _) => throw new Exception("Stale Unraid WinGet package used"));
            var calls = 0;
            var installer = new ApplicationInstaller(winget, new DownloadService(process, log), process, log,
                isInstalled: _ => false, installFallback: (_, fallback, _, _) =>
                { calls++; Check(fallback == app.Fallback); return Task.FromResult(ResultKind.Succeeded); });
            Check(installer.InstallAsync(app, "unused", CancellationToken.None).GetAwaiter().GetResult() == ResultKind.Succeeded && calls == 1);
        });
        yield return ("Unraid installer cannot remove an existing Raspberry Pi Imager", () =>
        {
            using var log = new RunLogger();
            var process = new ProcessRunner(log);
            var winget = new WingetService(process, log, () => "fake", (_, _, _, _) => throw new Exception("Conflicting installer launched"));
            var installer = new ApplicationInstaller(winget, new DownloadService(process, log), process, log,
                isInstalled: app => app.Key == "rpiimager", installFallback: (_, _, _, _) => throw new Exception("Conflicting installer launched"));
            Check(installer.InstallAsync(Catalog.AppByKey("unraidusb"), "unused", CancellationToken.None).GetAwaiter().GetResult() == ResultKind.NeedsVerification);
        });
        yield return ("TeamViewer ARM installer is a pinned MSI archive, not the EXE wrapper", () =>
        {
            var fallback = ArchitecturePolicy.PreferredArmFallbackFor(Catalog.AppByKey("teamviewer"))!;
            Check(fallback.FileName.Contains("ARM64") && fallback.MsiArchiveEntry == "update.msi");
            Check(fallback.ExpectedSha256?.Length == 64 && !fallback.AllowPinnedHashWithoutSignature);
            Check(fallback.Url.Contains("15.82.6_arm64.zip") && fallback.SilentArguments != "/S");
        });
        yield return ("MSI arguments preserve paths with spaces and include install, quiet, and no restart", () =>
        {
            var package = Path.Combine(Path.GetTempPath(), "Installer test", "update.msi");
            var log = Path.Combine(Path.GetTempPath(), "Installer test", "installer log.txt");
            Check(MsiPackage.SilentInstallArguments(package, log).SequenceEqual(["/i", package, "/qn", "/norestart", "/L*v", log]));
        });
        yield return ("TeamViewer detection checks actual PE files and both Program Files locations", () =>
        {
            var app = Catalog.AppByKey("teamviewer");
            Check(app.RequireKnownInstallPath);
            var paths = ApplicationInstaller.KnownInstallPaths(app).ToArray();
            Check(paths.Contains(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "TeamViewer", "TeamViewer.exe")));
            var file = Path.Combine(Path.GetTempPath(), "teamviewer-detection-" + Guid.NewGuid() + ".exe");
            try
            {
                foreach (var (machine, architecture) in new[] { ((ushort)0xAA64, Architecture.Arm64), ((ushort)0x8664, Architecture.X64), ((ushort)0x014C, Architecture.X86) })
                {
                    var bytes = new byte[128];
                    BitConverter.GetBytes((ushort)0x5A4D).CopyTo(bytes, 0);
                    BitConverter.GetBytes(64).CopyTo(bytes, 0x3C);
                    BitConverter.GetBytes(0x00004550).CopyTo(bytes, 64);
                    BitConverter.GetBytes(machine).CopyTo(bytes, 68);
                    File.WriteAllBytes(file, bytes);
                    Check(ApplicationInstaller.InstalledTeamViewerArchitecture([file]) == architecture);
                }
                File.WriteAllText(file, "Not an application");
                Check(ApplicationInstaller.InstalledTeamViewerArchitecture([file]) is null);
            }
            finally { File.Delete(file); }
        });
        yield return ("Unconfirmed TeamViewer ARM install never starts a second Intel installer", () =>
        {
            if (ArchitecturePolicy.OSArchitecture != Architecture.Arm64) return;
            using var log = new RunLogger();
            var process = new ProcessRunner(log);
            var winget = new WingetService(process, log, () => "fake", (_, _, _, _) => throw new Exception("Second installer launched"));
            var installer = new ApplicationInstaller(winget, new DownloadService(process, log), process, log,
                isInstalled: _ => false, installFallback: (_, _, _, _) => Task.FromResult(ResultKind.NeedsVerification));
            Check(installer.InstallAsync(Catalog.AppByKey("teamviewer"), "unused", CancellationToken.None).GetAwaiter().GetResult() == ResultKind.NeedsVerification);
        });
        yield return ("Existing TeamViewer is skipped without upgrade or uninstall", () =>
        {
            using var log = new RunLogger();
            var process = new ProcessRunner(log);
            var winget = new WingetService(process, log, () => "fake", (_, _, _, _) => throw new Exception("Existing app changed"));
            var installer = new ApplicationInstaller(winget, new DownloadService(process, log), process, log,
                isInstalled: _ => true, installFallback: (_, _, _, _) => throw new Exception("Existing app changed"));
            Check(installer.InstallAsync(Catalog.AppByKey("teamviewer"), "unused", CancellationToken.None).GetAwaiter().GetResult() == ResultKind.Skipped);
        });
        yield return ("Tampered MSI archives are rejected before extraction", () =>
        {
            var archive = Path.Combine(Path.GetTempPath(), "bad-msi-" + Guid.NewGuid() + ".zip");
            try
            {
                using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
                using (var writer = new StreamWriter(zip.CreateEntry("update.msi").Open())) writer.Write("tampered");
                using var log = new RunLogger();
                var service = new DownloadService(new ProcessRunner(log), log);
                try { service.ExtractVerifiedArmMsi(ArchitecturePolicy.PreferredArmFallbackFor(Catalog.AppByKey("teamviewer"))!, archive); }
                catch (InvalidDataException) { Check(!File.Exists(archive + ".msi")); return; }
                throw new Exception("Hash mismatch accepted");
            }
            finally { File.Delete(archive); }
        });
        if (Environment.GetEnvironmentVariable("PCSETUP125_RESEARCH_DIR") is { Length: > 0 } research)
            yield return ("Real vendor downloads validate hashes, publishers and ARM64 MSI metadata without installing", () =>
            {
                using var log = new RunLogger();
                var service = new DownloadService(new ProcessRunner(log), log);
                var fallback = ArchitecturePolicy.PreferredArmFallbackFor(Catalog.AppByKey("teamviewer"))!;
                var archive = Path.Combine(research, "teamviewer-arm64.zip");
                DownloadService.ValidatePayload(archive, true);
                var msi = service.ExtractVerifiedArmMsi(fallback, archive);
                Check(MsiPackage.ReadArchitecture(msi) == Architecture.Arm64);
                var x64Archive = Path.Combine(research, "teamviewer-x64.zip");
                var x64Fallback = fallback with { ExpectedSha256 = "1391101EE6F3B02974FF57705AE0C833562EFFED7471FC71899D59F536FAA259" };
                try { service.ExtractVerifiedArmMsi(x64Fallback, x64Archive); }
                catch (InvalidDataException exception) when (exception.Message.Contains("not an ARM64"))
                { Check(MsiPackage.ReadArchitecture(x64Archive + ".msi") == Architecture.X64); goto VerifyUnraid; }
                throw new Exception("Signed x64 MSI accepted as ARM64");
                VerifyUnraid:
                var unraid = Path.Combine(research, "unraid.exe");
                Check(DownloadService.ComputeSha256Async(unraid).GetAwaiter().GetResult() == Catalog.AppByKey("unraidusb").Fallback!.ExpectedSha256);
                service.VerifyAuthenticode(unraid, Catalog.AppByKey("unraidusb").Fallback!.AllowedSignerFragments);
            });
    }

    private static void Check(bool condition) { if (!condition) throw new Exception("1.2.6 regression safeguard failed"); }
}
