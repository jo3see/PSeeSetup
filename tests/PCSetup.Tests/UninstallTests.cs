using PCSetup;
using PCSetup.Services;

public static class UninstallTests
{
    public static RemovalInventory UiInventory() => new()
    {
        Packages = [new("Microsoft.Paint", "Microsoft.Paint_1.0_arm64__8wekyb3d8bbwe", "CN=Microsoft Corporation"),
            new("Microsoft.GamingApp", "Microsoft.GamingApp_1.0_neutral__8wekyb3d8bbwe", "CN=Microsoft Corporation", NonRemovable: true)],
        Programs = [new("deno", "Deno", "Deno Land Inc.", "winget uninstall --product-code DenoLand.Deno", "")],
        StartNames = ["Sticky Notes (new)"], RecallState = "Enabled"
    };
    public static IEnumerable<(string Name, Action Test)> All()
    {
        yield return ("All 24 requested removal entries are present, unique and unchecked by default", () =>
        {
            Check(UninstallCatalog.Applications.Count == 24 && UninstallCatalog.Applications.Select(x => x.Key).Distinct().Count() == 24);
            Check(SetupProfile.DefaultSelection().UninstallApplications.Count == 0);
            Check(UninstallCatalog.Applications.Count(x => x.Name.Contains("Copilot")) == 2);
            Check(UninstallCatalog.Applications.Count(x => x.Name.Contains("Sticky Notes")) == 2);
        });
        yield return ("Absent, protected and undetected apps remain blocked; publisher labels do not block installed apps", () =>
        {
            Check(UninstallService.Status(UninstallCatalog.ByKey("paint"), UiInventory()).CanRemove);
            Check(UninstallService.Status(UninstallCatalog.ByKey("glidex"), UiInventory()).Availability == RemovalAvailability.NotInstalled);
            Check(!UninstallService.Status(UninstallCatalog.ByKey("xbox"), UiInventory()).CanRemove);
            Check(UninstallService.Status(UninstallCatalog.ByKey("paint"), new() { PackagesAvailable = false }).Availability == RemovalAvailability.Unknown);
            Check(UninstallService.Status(UninstallCatalog.ByKey("paint"), new() { Packages = [new("Microsoft.Paint", "fake", "Unknown publisher")] }).CanRemove);
        });
        yield return ("Unknown or blank publishers do not prevent exact Store app removal", () =>
        {
            using var log = new RunLogger();
            foreach (var publisher in new[] { "", "CN=Unexpected publisher" })
            {
                var removed = false;
                var service = new UninstallService(new ProcessRunner(log), log,
                    _ => Task.FromResult(removed ? new RemovalInventory() : new RemovalInventory
                    { Packages = [new("Microsoft.Paint", "Microsoft.Paint_exact-package", publisher)] }),
                    (_, args, _) =>
                    {
                        var script = System.Text.Encoding.Unicode.GetString(Convert.FromBase64String(args.Last()));
                        Check(script.Contains("-Package 'Microsoft.Paint_exact-package'"));
                        removed = true; return Task.FromResult(new ProcessResult(0, "", ""));
                    });
                Check(service.UninstallAsync(UninstallCatalog.ByKey("paint"), "unused", CancellationToken.None).GetAwaiter().GetResult() == ResultKind.Succeeded);
            }
        });
        yield return ("Registered EXE removal accepts unsigned uninstallers and unfamiliar publishers without executing during tests", () =>
        {
            var file = Path.Combine(Path.GetTempPath(), "Uninstaller test " + Guid.NewGuid() + ".exe");
            try
            {
                File.WriteAllText(file, "Unsigned test fixture; never executed");
                var removed = false;
                using var log = new RunLogger();
                var program = new InstalledDesktopApp("copilot", "Copilot", "", "\"" + file + "\" --uninstall", "");
                var service = new UninstallService(new ProcessRunner(log), log,
                    _ => Task.FromResult(removed ? new RemovalInventory() : new RemovalInventory { Programs = [program] }),
                    (executable, args, _) =>
                    { Check(executable == file && args.SequenceEqual(["--uninstall"])); removed = true; return Task.FromResult(new ProcessResult(0, "", "")); });
                Check(service.UninstallAsync(UninstallCatalog.ByKey("copilotdesktop"), "unused", CancellationToken.None).GetAwaiter().GetResult() == ResultKind.Succeeded);
            }
            finally { File.Delete(file); }
        });
        yield return ("Sticky Notes new cannot uninstall OneNote", () =>
        {
            Check(!UninstallService.Status(UninstallCatalog.ByKey("stickynotesnew"), UiInventory()).CanRemove);
            Check(UninstallCatalog.ByKey("stickynotesnew").PackageNames.Length == 0);
        });
        yield return ("Xbox and Game Bar removal excludes shared gaming services and system packages", () =>
        {
            var inventory = new RemovalInventory { Packages = [new("Microsoft.GamingServices", "a", "Microsoft"),
                new("Microsoft.XboxIdentityProvider", "b", "Microsoft"), new("Microsoft.WindowsStore", "c", "Microsoft"),
                new("Microsoft.XboxGameCallableUI", "d", "Microsoft")] };
            Check(!UninstallService.StoreMatches(UninstallCatalog.ByKey("xbox"), inventory).Any());
            Check(!UninstallService.StoreMatches(UninstallCatalog.ByKey("gamebar"), inventory).Any());
            Check(UninstallCatalog.ByKey("paint").PackageNames.SequenceEqual(["Microsoft.Paint"]));
        });
        yield return ("Two Copilot entries resolve to distinct desktop and Store installations", () =>
        {
            var inventory = new RemovalInventory { Packages = [new("Microsoft.Copilot", "storecopilot", "Microsoft")],
                Programs = [new("Microsoft Copilot", "Copilot", "Microsoft Corporation", "command", "")] };
            Check(UninstallService.StoreMatches(UninstallCatalog.ByKey("copilotstore"), inventory).Count() == 1);
            Check(!UninstallService.DesktopMatches(UninstallCatalog.ByKey("copilotstore"), inventory).Any());
            Check(!UninstallService.StoreMatches(UninstallCatalog.ByKey("copilotdesktop"), inventory).Any());
            Check(UninstallService.DesktopMatches(UninstallCatalog.ByKey("copilotdesktop"), inventory).Count() == 1);
        });
        yield return ("ASUS Store detection accepts exact app labels and rejects unrelated utilities", () =>
        {
            var inventory = new RemovalInventory { Packages = [new("Vendor.VariableIdentity", "glidex", "ASUSTeK COMPUTER INC.", DisplayNames: ["GlideX"]),
                new("Vendor.MyASUS", "myasus", "ASUSTeK COMPUTER INC.", DisplayNames: ["MyASUS"])] };
            Check(UninstallService.StoreMatches(UninstallCatalog.ByKey("glidex"), inventory).Single().PackageFullName == "glidex");
            Check(UninstallService.Status(UninstallCatalog.ByKey("glidex"), inventory).CanRemove);
        });
        yield return ("Store removal targets one exact package in current-user scope and escapes quotes", () =>
        {
            var script = UninstallService.BuildStoreRemovalScript("pkg';unexpected");
            Check(script.Contains("-Package 'pkg'';unexpected'") && !script.Contains("-AllUsers") && !script.Contains("Provisioned"));
            Check(!script.Contains("Remove-Item") && !script.Contains("*"));
        });
        yield return ("Registered MSI uninstall uses product identity, quiet flags and separate paths", () =>
        {
            var code = Guid.NewGuid().ToString("B");
            var command = UninstallService.BuildDesktopCommand(UninstallCatalog.ByKey("glidexservice"),
                new("msi", "GlideX Service Installer", "ASUSTeK COMPUTER INC.", "msiexec /I" + code, "", true, code), Path.Combine(Path.GetTempPath(), "Uninstall test.log"));
            Check(command.Arguments.Take(4).SequenceEqual(["/x", code, "/qn", "/norestart"]) && command.Arguments[4] == "/L*v");
            try { UninstallService.BuildDesktopCommand(UninstallCatalog.ByKey("glidexservice"), new("bad", "GlideX Service Installer", "ASUS", "", "", true, "not a GUID"), "unused"); }
            catch (InvalidDataException) { return; }
            throw new Exception("Invalid MSI identity accepted");
        });
        yield return ("Deno removal uses its exact WinGet ID instead of executing registry text", () =>
        {
            var command = UninstallService.BuildDesktopCommand(UninstallCatalog.ByKey("deno"),
                new("deno", "Deno", "Deno Land Inc.", "arbitrary registry command", ""), "unused", "test-winget.exe");
            Check(command.Executable == "test-winget.exe" && command.Arguments[0] == "uninstall" && command.Arguments.Contains("DenoLand.Deno"));
            Check(!command.Arguments.Contains("arbitrary registry command"));
        });
        yield return ("Uninstaller command parsing preserves quoted paths and vendor switches", () =>
        {
            Check(UninstallService.ParseCommandLine("\"C:\\Program Files\\Vendor\\uninstall.exe\" /uninstall \"argument with spaces\"")
                .SequenceEqual([@"C:\Program Files\Vendor\uninstall.exe", "/uninstall", "argument with spaces"]));
            try { UninstallService.BuildDesktopCommand(UninstallCatalog.ByKey("copilotdesktop"), new("copilot", "Copilot", "Microsoft", "cmd.exe /c arbitrary", ""), "unused"); }
            catch (InvalidDataException) { return; }
            throw new Exception("Shell registry command accepted");
        });
        yield return ("Absent and unsupported selections never invoke removal commands", () =>
        {
            using var log = new RunLogger();
            var service = new UninstallService(new ProcessRunner(log), log, _ => Task.FromResult(UiInventory()),
                (_, _, _) => throw new Exception("Unselected/unsupported app removed"));
            Check(service.UninstallAsync(UninstallCatalog.ByKey("glidex"), "unused", CancellationToken.None).GetAwaiter().GetResult() == ResultKind.Skipped);
            Check(service.UninstallAsync(UninstallCatalog.ByKey("stickynotesnew"), "unused", CancellationToken.None).GetAwaiter().GetResult() == ResultKind.NeedsVerification);
        });
        yield return ("Store removal verifies absence and leaves other apps untouched", () =>
        {
            using var log = new RunLogger();
            var removed = false; var calls = 0;
            var service = new UninstallService(new ProcessRunner(log), log,
                _ => Task.FromResult(removed ? new RemovalInventory() : UiInventory()), (file, args, _) =>
                {
                    var argv = args.ToArray();
                    var script = System.Text.Encoding.Unicode.GetString(Convert.FromBase64String(argv.Last()));
                    Check(script.Contains("Microsoft.Paint_1.0_arm64__8wekyb3d8bbwe") && !script.Contains("Microsoft.GamingApp"));
                    calls++; removed = true; return Task.FromResult(new ProcessResult(0, "", ""));
                });
            Check(service.UninstallAsync(UninstallCatalog.ByKey("paint"), "unused", CancellationToken.None).GetAwaiter().GetResult() == ResultKind.Succeeded && calls == 1);
        });
        yield return ("Installer exit zero cannot hide an app that remains installed", () =>
        {
            using var log = new RunLogger();
            var service = new UninstallService(new ProcessRunner(log), log, _ => Task.FromResult(UiInventory()),
                (_, _, _) => Task.FromResult(new ProcessResult(0, "", "")));
            Check(service.UninstallAsync(UninstallCatalog.ByKey("paint"), "unused", CancellationToken.None).GetAwaiter().GetResult() == ResultKind.NeedsVerification);
        });
        yield return ("Recall uses supported feature removal without forcing a restart", () =>
        {
            using var log = new RunLogger(); var started = false;
            var service = new UninstallService(new ProcessRunner(log), log,
                _ => Task.FromResult(new RemovalInventory { RecallState = started ? "DisablePending" : "Enabled" }),
                (file, args, _) => { Check(Path.GetFileName(file) == "dism.exe" && args.Contains("/FeatureName:Recall") && args.Contains("/Remove") && args.Contains("/NoRestart")); started = true; return Task.FromResult(new ProcessResult(3010, "", "")); });
            Check(service.UninstallAsync(UninstallCatalog.ByKey("recall"), "unused", CancellationToken.None).GetAwaiter().GetResult() == ResultKind.RebootRequired);
            Check(!UninstallService.Status(UninstallCatalog.ByKey("recall"), new() { RecallState = "DisabledWithPayloadRemoved" }).CanRemove);
        });
        yield return ("Removal state persists but failed-install retries strip all removals", () =>
        {
            var state = new SetupState { Profile = new SetupProfile { Applications = ["tor"], UninstallApplications = ["paint"] },
                Steps = [new() { Key = "app:tor", Result = ResultKind.Failed }, new() { Key = "uninstall:paint", Result = ResultKind.Failed }] };
            state.UninstallVerification["paint"] = ResultKind.Succeeded;
            Check(RunResults.Final(state)["uninstall:paint"] == ResultKind.Succeeded);
            Check(RunResults.RetryProfile(state).UninstallApplications.Count == 0);
            var json = System.Text.Json.JsonSerializer.Serialize(state);
            Check(System.Text.Json.JsonSerializer.Deserialize<SetupState>(json)!.Profile.UninstallApplications.SetEquals(["paint"]));
            try { RunResults.ValidateRetryProfile(new() { ApplicationsOnly = true, UninstallApplications = ["paint"] }); }
            catch (InvalidDataException) { return; }
            throw new Exception("Installation retry replayed removals");
        });
    }
    private static void Check(bool condition) { if (!condition) throw new Exception("Uninstall feature regression failed"); }
}
