using PCSetup;
using PCSetup.Services;
using System.Runtime.InteropServices;

public static class Regression122Tests
{
    private static readonly ProcessResult Applicable = new(0,
        "Installer:\n  Installer Url: https://vendor.example/app.exe\n  Installer SHA256: " + new string('A', 64), "");
    private static readonly ProcessResult Missing = new(0,
        "Found Steam [Valve.Steam]\nInstaller:\n  No applicable installer found; see logs for more details.", "");

    public static IEnumerable<(string, Action)> All() => new (string, Action)[]
    {
        ("An installed Spotify Store package skips the desktop installer", () =>
        {
            using var log = new RunLogger();
            var process = new ProcessRunner(log);
            var winget = new WingetService(process, log, () => "fake-winget",
                (_, _, _, _) => throw new Exception("Store Spotify must not invoke an installer"));
            var installer = new ApplicationInstaller(winget, new DownloadService(process, log), process, log, () => true);
            Check(installer.IsInstalled(Catalog.AppByKey("spotify")));
            var result = installer.InstallAsync(Catalog.AppByKey("spotify"), "unused-cache", CancellationToken.None).GetAwaiter().GetResult();
            Check(result == ResultKind.Skipped);
        }),
        ("WinGet show exit zero without an installer is rejected", () =>
        {
            Check(!WingetService.HasApplicableInstaller(Missing));
            Check(WingetService.HasApplicableInstaller(Applicable));
            Check(!WingetService.HasApplicableInstaller(Applicable with { StandardOutput = "Installer Url: https://vendor.example/app.exe" }));
            Check(!WingetService.HasApplicableInstaller(Applicable with { ExitCode = -1 }));
        }),
        ("ARM unavailable native installer uses approved emulation", () =>
        {
            if (ArchitecturePolicy.OSArchitecture != Architecture.Arm64) return;
            var calls = new List<string[]>();
            using var log = new RunLogger();
            var winget = new WingetService(new ProcessRunner(log), log, () => "fake-winget",
                (_, args, _, _) =>
                {
                    calls.Add(args.ToArray());
                    return Task.FromResult(args[0] == "show" && !args.Contains("x86") ? Missing : Applicable);
                });
            var result = winget.InstallAsync(Catalog.AppByKey("steam"), false, CancellationToken.None).GetAwaiter().GetResult();
            Check(result.Success && calls.Count == 4);
            Check(calls[0][0] == "show" && calls[0].Contains("arm64"));
            Check(calls[1][0] == "show" && calls[1].Contains("x64"));
            Check(calls[2][0] == "show" && calls[2].Contains("x86"));
            Check(calls[3][0] == "install" && calls[3].Contains("x86") && !calls[3].Contains("arm64"));
        }),
        ("Available ARM installer is preferred and native integrations fail closed", () =>
        {
            if (ArchitecturePolicy.OSArchitecture != Architecture.Arm64) return;
            using var log = new RunLogger();
            var calls = new List<string[]>();
            var winget = new WingetService(new ProcessRunner(log), log, () => "fake-winget",
                (_, args, _, _) => { calls.Add(args.ToArray()); return Task.FromResult(Applicable); });
            winget.InstallAsync(Catalog.AppByKey("steam"), false, CancellationToken.None).GetAwaiter().GetResult();
            Check(calls.Last()[0] == "install" && calls.Last().Contains("arm64"));
            calls.Clear();
            var blocked = new WingetService(new ProcessRunner(log), log, () => "fake-winget",
                (_, args, _, _) => { calls.Add(args.ToArray()); return Task.FromResult(Missing); });
            var result = blocked.InstallAsync(Catalog.AppByKey("spacedesk"), false, CancellationToken.None).GetAwaiter().GetResult();
            Check(!result.Success && calls.Count == 1 && calls[0][0] == "show");
        }),
        ("HTML and malformed downloads cannot pass executable validation", () =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe");
            try
            {
                File.WriteAllText(path, "<!doctype html><html>Download now</html>");
                Reject(() => DownloadService.ValidatePayload(path, false));
                File.WriteAllBytes(path, [0x4D, 0x5A]);
                Reject(() => DownloadService.ValidatePayload(path, false));
                var notepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "notepad.exe");
                DownloadService.ValidatePayload(notepad, false);
            }
            finally { File.Delete(path); }
        }),
        ("Final results count components and resolve earlier failed attempts", () =>
        {
            var state = Fixture();
            var final = RunResults.Final(state);
            Check(final.Count == 3 && final.Values.Count(x => x == ResultKind.Failed) == 1);
            Check(RunResults.HasIssues(state));
            state.ApplicationVerification["tor"] = ResultKind.Succeeded;
            Check(!RunResults.HasIssues(state));
            state.ApplicationVerification["steam"] = ResultKind.Failed;
            Check(RunResults.HasIssues(state));
        }),
        ("Failed-app retry starts a fresh application selection without tweaks or features", () =>
        {
            var previous = Fixture();
            previous.Profile.Tweaks.Add("some-prior-tweak");
            previous.Profile.Features.Add("some-prior-feature");
            previous.Profile.BlockRazerDomains = true;
            var retry = RunResults.RetryProfile(previous);
            Check(retry.Applications.SetEquals(["tor"]));
            Check(retry.ApplicationsOnly && !retry.BlockRazerDomains);
            Check(retry.Tweaks.Count == 0 && retry.Features.Count == 0);
            Check(previous.Steps.Count == 5 && previous.Profile.Tweaks.Count == 1);
            Check(RunResults.AfterMachineApps(retry) == SetupPhase.Verify);
            Check(RunResults.AfterMachineApps(SetupProfile.DefaultSelection()) == SetupPhase.ApplyTweaks);
            RunResults.ValidateRetryProfile(retry);
            retry.Tweaks.Add("some-tweak");
            Reject(() => RunResults.ValidateRetryProfile(retry));
        }),
        ("Applications-only flag survives profile export/import", () =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
            try
            {
                StateStore.ExportProfile(RunResults.RetryProfile(Fixture()), path);
                var profile = StateStore.ImportProfile(path);
                Check(profile.ApplicationsOnly && profile.Applications.SetEquals(["tor"]));
            }
            finally { File.Delete(path); }
        })
    };

    private static SetupState Fixture() => new()
    {
        Profile = new SetupProfile { Applications = ["tor", "steam", "spotify"] },
        Steps =
        [
            new() { Key = "app:tor", Result = ResultKind.Failed, Attempt = 1 },
            new() { Key = "app:tor", Result = ResultKind.Failed, Attempt = 2 },
            new() { Key = "app:steam", Result = ResultKind.Failed, Attempt = 1 },
            new() { Key = "app:steam", Result = ResultKind.Succeeded, Attempt = 2 },
            new() { Key = "app:spotify", Result = ResultKind.Skipped, Attempt = 1 }
        ]
    };
    private static void Check(bool condition) { if (!condition) throw new Exception("1.2.2 regression safeguard failed"); }
    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new Exception("Invalid input was accepted");
    }
}
