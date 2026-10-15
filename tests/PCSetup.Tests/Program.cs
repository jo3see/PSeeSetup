using PCSetup;
using PCSetup.Services;
using PCSetup.UI;

var tests = new List<(string Name, Action Test)>
{
    ("Application keys are unique", () => Unique(Catalog.Applications.Select(x => x.Key))),
    ("Tweak keys are unique", () => Unique(Catalog.Tweaks.Select(x => x.Key))),
    ("Feature keys are unique", () => Unique(Catalog.Features.Select(x => x.Key))),
    ("WinGet IDs are exact and unique", () => Unique(Catalog.Applications.Select(x => x.WingetId))),
    ("All fallbacks use HTTPS and signer allowlists", () =>
    {
        foreach (var fallback in Catalog.Applications.Where(x => x.Fallback is not null).Select(x => x.Fallback!))
        {
            Assert(Uri.TryCreate(fallback.Url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps, fallback.Url);
            Assert(fallback.AllowedSignerFragments.Length > 0, fallback.FileName);
        }
    }),
    ("Default selections include Spotify, JDownloader 2, and BlueBubbles", () =>
    {
        var profile = SetupProfile.DefaultSelection(System.Runtime.InteropServices.Architecture.X64);
        Assert(profile.Applications.Contains("spotify"), "Spotify missing");
        Assert(profile.Applications.Contains("jdownloader2"), "JDownloader 2 missing");
        Assert(profile.Applications.Contains("bluebubbles"), "BlueBubbles missing");
    }),
    ("Requested application batch is present without duplicates", () =>
    {
        var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["balenaetcher"] = "Balena.Etcher",
            ["brave"] = "Brave.Brave",
            ["claude"] = "Anthropic.Claude",
            ["discord"] = "Discord.Discord",
            ["logioptionsplus"] = "Logitech.OptionsPlus",
            ["modrinth"] = "Modrinth.ModrinthApp",
            ["nvcleanstall"] = "TechPowerUp.NVCleanstall",
            ["paintdotnet"] = "dotPDN.PaintDotNet",
            ["slack"] = "SlackTechnologies.Slack",
            ["spacedesk"] = "Datronicsoft.SpacedeskDriver.Server",
            ["ultraiso"] = "EZBSystems.UltraISO",
            ["unraidusb"] = "LimeTechnology.UnraidUSBCreator"
        };
        var profile = SetupProfile.DefaultSelection(System.Runtime.InteropServices.Architecture.X64);
        foreach (var (key, wingetId) in expected)
        {
            var app = Catalog.AppByKey(key);
            Assert(app.WingetId == wingetId, $"{key} package ID changed");
            Assert(profile.Applications.Contains(key), $"{key} is not selected by default");
        }
        foreach (var key in new[] { "claude", "discord", "modrinth" })
            Assert(Catalog.AppByKey(key).Context == InstallContext.User, $"{key} must install for the signed-in user");
    }),
    ("BlueBubbles uses the official Microsoft Store product", () =>
    {
        var app = Catalog.AppByKey("bluebubbles");
        Assert(app.WingetId == "9P3XF8KJ0LSM", "Microsoft Store product ID changed");
        Assert(app.WingetSource == "msstore", "BlueBubbles must use the Microsoft Store source");
        Assert(app.Context == InstallContext.User, "BlueBubbles must install for the signed-in user");
        Assert(app.Fallback is null, "Unsigned standalone BlueBubbles installer must not be an automatic fallback");
        var arguments = WingetService.BuildInstallArguments(app, architecture: System.Runtime.InteropServices.Architecture.X64);
        Assert(arguments.Contains("msstore"), "Microsoft Store source argument missing");
        Assert(!arguments.Contains("--scope"), "Microsoft Store install should not force an incompatible scope");
    }),
    ("Only .NET 10 is selected by default", () =>
    {
        var profile = SetupProfile.DefaultSelection(System.Runtime.InteropServices.Architecture.X64);
        Assert(profile.Applications.Contains("dotnet10"), ".NET 10 missing");
        Assert(!profile.Applications.Contains("dotnet9") && !profile.Applications.Contains("dotnet8"), "Compatibility runtimes should be optional");
        Assert(Catalog.Applications.All(x => x.Key is not "dotnet6" and not "dotnet7"), "Unsupported runtimes present");
    }),
    ("PowerShell remains an upgrade-always exception", () =>
    {
        var powerShell = Catalog.AppByKey("powershell");
        Assert(powerShell.UpgradeIfInstalled,
            "PowerShell must still be upgraded to the latest available stable version");
        Assert(powerShell.RequireKnownInstallPath && (powerShell.DetectionPaths?.Any(x => x.EndsWith("pwsh.exe", StringComparison.OrdinalIgnoreCase)) ?? false),
            "PowerShell must require a physical pwsh.exe installation");
        Assert(powerShell.DetectionPaths!.Any(x => x.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase)),
            "PowerShell must recognize its Microsoft Store/MSIX alias");
        Assert(powerShell.ValidationArguments?.Contains("exit 0") == true,
            "PowerShell installation detection must execute pwsh.exe");
        Assert(WingetService.BuildInstallArguments(powerShell, true).Contains("--force"),
            "PowerShell repair must support a forced WinGet installation");
        Assert(!ApplicationInstaller.WingetInstallCountsAsCompleted(powerShell, new ProcessResult(0, "", ""), false),
            "A successful WinGet result must not hide a missing pwsh.exe");
        Assert(ApplicationInstaller.WingetInstallCountsAsCompleted(powerShell, new ProcessResult(unchecked((int)0x8A15002B), "", ""), true),
            "An existing physical pwsh.exe should satisfy the strict PowerShell check");
        Assert(Catalog.Applications.Where(x => x.Key != "powershell").All(x => !x.UpgradeIfInstalled),
            "Only PowerShell should bypass installed-app skipping");
    }),
    ("Removed tweaks stay removed", () =>
    {
        var keys = Catalog.Tweaks.Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var forbidden in new[] { "location", "mpo", "wifi_sense", "adobe_block" })
            Assert(!keys.Contains(forbidden), forbidden);
    }),
    ("Required Windows features remain selected", () =>
    {
        var profile = SetupProfile.DefaultSelection(System.Runtime.InteropServices.Architecture.X64);
        foreach (var key in new[] { "hyperv", "sandbox", "nfs", "netfx3" }) Assert(profile.Features.Contains(key), key);
    }),
    ("BitLocker preparation uses stable protected resume identifiers", () =>
    {
        Assert(BitLockerPreparationService.ResumeArgument == "--bitlocker-resume", "Resume argument changed");
        Assert(BitLockerPreparationService.PreparationTaskName == "PCSetup-BitLockerPreparation", "Task name changed");
        Assert(Path.GetFileName(BitLockerPreparationService.PendingMarkerPath) == "BitLocker Preparation.pending", "Marker name changed");
    }),
    ("Unlocked BitLocker status is not mistaken for locked", () =>
    {
        var unlocked = new BitLockerVolumeStatus("C:", false, "Fully encrypted", 100,
            "Protection off", "Fully encrypted; 100% encrypted; Protection off; Unlocked.", false);
        var locked = unlocked with { Details = "Fully encrypted; 100% encrypted; Protection off; Locked.", IsLocked = true };
        Assert(!BitLockerPreparationService.IsExplicitlyLocked(unlocked), "Unlocked was treated as locked");
        Assert(BitLockerPreparationService.IsExplicitlyLocked(locked), "Locked status was not honored");
    }),
    ("Main selection window constructs before its handle exists", () =>
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var gate = new BitLockerGateResult();
                gate.Volumes.Add(new BitLockerVolumeStatus("C:", true, "Fully decrypted", 0,
                    "Protection off", "Fully decrypted; 0% encrypted; Protection off; Unlocked."));
                using var form = new MainForm(null, gate);
                Assert(!form.IsHandleCreated, "The constructor unexpectedly forced window-handle creation");
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert(thread.Join(TimeSpan.FromSeconds(20)), "Main window construction timed out");
        if (failure is not null) throw new InvalidOperationException("Main window construction failed", failure);
    }),
    ("Main window uses the requested navigation and action text", TestRequestedMainWindowUi),
    ("Known application paths support pre-install skipping", () =>
    {
        var path = Path.Combine(Path.GetTempPath(), "PCSetup-installed-test-" + Guid.NewGuid().ToString("N") + ".exe");
        try
        {
            File.WriteAllText(path, "test");
            var app = new AppDefinition("test", "Test App", "Test", "Test.App", InstallContext.Machine,
                false, "Test", DetectionPaths: [path]);
            Assert(ApplicationInstaller.HasKnownInstallPath(app), "Existing detection path was missed");
            Assert((Catalog.AppByKey("equalizerapo").DetectionPaths?.Length ?? 0) >= 2,
                "Equalizer APO detection paths are missing");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }),
    ("Strict executable detection requires a successful launch", () =>
    {
        using var log = new RunLogger();
        var process = new ProcessRunner(log);
        var winget = new WingetService(process, log);
        var downloads = new DownloadService(process, log);
        var installer = new ApplicationInstaller(winget, downloads, process, log);
        var command = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var usable = new AppDefinition("usable", "Usable", "Test", "Test.Usable", InstallContext.Machine,
            false, "Test", DetectionPaths: [command], RequireKnownInstallPath: true,
            ValidationArguments: ["/c", "exit", "0"]);
        var broken = usable with { Key = "broken", Name = "Broken", ValidationArguments = ["/c", "exit", "7"] };
        Assert(installer.IsInstalled(usable), "A runnable strict executable was rejected");
        Assert(!installer.IsInstalled(broken), "A failing strict executable was accepted");
    }),
    ("Equalizer APO launches from its installation directory", () =>
    {
        var executable = Path.Combine(@"C:\Program Files\EqualizerAPO", "DeviceSelector.exe");
        var start = ApplicationInstaller.BuildEqualizerStartInfo(executable);
        Assert(start.UseShellExecute, "Equalizer launcher must use the Windows shell");
        Assert(start.WorkingDirectory == @"C:\Program Files\EqualizerAPO",
            "Equalizer working directory must expose its Qt plug-ins");
    }),
    ("Fallback hashing releases the installer for retry", () =>
    {
        var path = Path.Combine(Path.GetTempPath(), "PCSetup-hash-test-" + Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            File.WriteAllText(path, "retry-safe");
            var hash = DownloadService.ComputeSha256Async(path).GetAwaiter().GetResult();
            Assert(hash.Length == 64, "SHA256 was not produced");
            using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }),
    ("Authenticode command embeds and escapes its path", () =>
    {
        var encoded = DownloadService.BuildAuthenticodeEncodedCommand(@"C:\Program Files\Developer's Tool\tool.exe");
        var script = System.Text.Encoding.Unicode.GetString(Convert.FromBase64String(encoded));
        Assert(script.Contains(@"C:\Program Files\Developer''s Tool\tool.exe", StringComparison.Ordinal),
            "Signature path was not escaped");
        Assert(!script.Contains("$args[0]", StringComparison.Ordinal), "Broken positional argument remains");
    }),
    ("Authenticode verification accepts a signed Windows executable", () =>
    {
        var signedWindowsFile = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        Assert(File.Exists(signedWindowsFile), "Signed Windows test file is missing");
        using var log = new RunLogger();
        var runner = new ProcessRunner(log);
        new DownloadService(runner, log).VerifyAuthenticode(signedWindowsFile, ["Microsoft"]);
    }),
    ("Parsec retains its official safe fallback", () =>
    {
        var fallback = Catalog.AppByKey("parsec").Fallback!;
        Assert(fallback.Url == "https://builds.parsec.app/package/parsec-windows.exe", "Parsec URL changed");
        Assert(fallback.SilentArguments.Contains("/silent", StringComparison.OrdinalIgnoreCase), "Parsec silent flag missing");
        Assert(fallback.SilentArguments.Contains("/percomputer", StringComparison.OrdinalIgnoreCase), "Parsec machine flag missing");
        Assert(!fallback.SilentArguments.Contains("/vdd") && !fallback.SilentArguments.Contains("/usb"), "Optional drivers must not be requested");
        Assert(fallback.AllowedSignerFragments.Any(x => x.Contains("Unity", StringComparison.OrdinalIgnoreCase)),
            "Parsec publisher allowlist missing");
    }),
    ("Windows Terminal is set explicitly to PowerShell 7", () =>
    {
        var validation = RegistryTweakService.PowerShellValidationArguments();
        Assert(validation.Contains("-NoProfile") && validation.Contains("exit 0"),
            "PowerShell must be executed successfully before Terminal is modified");
        const string original = "{\n  \"defaultProfile\": \"{00000000-0000-0000-0000-000000000000}\",\n  \"profiles\": { \"list\": [] }\n}";
        var updated = RegistryTweakService.SetPowerShell7DefaultInSettingsJson(original);
        Assert(updated.Contains(RegistryTweakService.PowerShell7ProfileGuid, StringComparison.OrdinalIgnoreCase),
            "PowerShell 7 profile GUID was not written");
        Assert(!updated.Contains("{00000000-0000-0000-0000-000000000000}", StringComparison.OrdinalIgnoreCase),
            "Old default profile remained");

        var inserted = RegistryTweakService.SetPowerShell7DefaultInSettingsJson("{\n  \"profiles\": { \"list\": [] }\n}");
        Assert(inserted.Contains("\"defaultProfile\"", StringComparison.Ordinal), "Default profile was not inserted");
    }),
    ("Profile JSON round trip", () =>
    {
        var path = Path.Combine(Path.GetTempPath(), "PCSetup-profile-test-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var profile = SetupProfile.DefaultSelection(System.Runtime.InteropServices.Architecture.X64);
            StateStore.ExportProfile(profile, path);
            var loaded = StateStore.ImportProfile(path);
            Assert(profile.Applications.SetEquals(loaded.Applications), "Application selections changed");
            Assert(profile.Tweaks.SetEquals(loaded.Tweaks), "Tweak selections changed");
            Assert(profile.Features.SetEquals(loaded.Features), "Feature selections changed");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    })
};

Console.WriteLine($"Test environment: OS={System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}, process={System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
tests.AddRange(ArchitectureTests.All());
tests.AddRange(Regression122Tests.All());
tests.AddRange(Regression123Tests.All());
tests.AddRange(Regression125Tests.All());
tests.AddRange(UninstallTests.All());
tests.AddRange(RetryPickerTests.All());
if (Environment.GetEnvironmentVariable("PCSETUP_VENDOR_TEST_CACHE") is { Length: > 0 } vendorCache)
{
    tests.Add(("Official Equalizer ARM download passes production payload/hash validation", () =>
    {
        using var logger = new RunLogger();
        var service = new DownloadService(new ProcessRunner(logger), logger);
        var fallback = ArchitecturePolicy.FallbackFor(Catalog.AppByKey("equalizerapo"), System.Runtime.InteropServices.Architecture.Arm64)!;
        var path = service.DownloadAndVerifyAsync(fallback, vendorCache, CancellationToken.None).GetAwaiter().GetResult();
        Assert(new FileInfo(path).Length == 8870288, "Unexpected Equalizer installer payload");
        Assert(DownloadService.ComputeSha256Async(path).GetAwaiter().GetResult() == fallback.ExpectedSha256, "Equalizer hash mismatch");
    }));
    foreach (var key in new[] { "teamviewer", "wireshark" })
        tests.Add(($"Official {key} ARM download passes production payload/signature validation", () =>
        {
            using var logger = new RunLogger();
            var fallback = ArchitecturePolicy.PreferredArmFallbackFor(Catalog.AppByKey(key))!;
            var service = new DownloadService(new ProcessRunner(logger), logger);
            var path = service.DownloadAndVerifyAsync(fallback, vendorCache, CancellationToken.None).GetAwaiter().GetResult();
            Assert(new FileInfo(path).Length > 1_000_000, "Vendor returned an incomplete download");
        }));
}
if (Environment.GetEnvironmentVariable("PCSETUP_PRIOR_RUN_STATE") is { Length: > 0 } priorRunPath)
    tests.Add(("Real 1.2.2 Tor verification failure resolves through portable detection without installing", () =>
    {
        var originalHash = DownloadService.ComputeSha256Async(priorRunPath).GetAwaiter().GetResult();
        var state = StateStore.Load(priorRunPath);
        Assert(state.SetupVersion == "1.2.2", "Wrong run fixture");
        Assert(RunResults.RetryProfile(state).Applications.SetEquals(["tor"]), "Unexpected failed-app selection");
        using var logger = new RunLogger();
        var process = new ProcessRunner(logger);
        var winget = new WingetService(process, logger, () => "fake", (_, _, _, _) => throw new Exception("Tor must not reinstall"));
        var installer = new ApplicationInstaller(winget, new DownloadService(process, logger), process, logger);
        Assert(installer.IsInstalled(Catalog.AppByKey("tor")), "Real Desktop Tor not detected");
        Assert(installer.InstallAsync(Catalog.AppByKey("tor"), "unused", CancellationToken.None).GetAwaiter().GetResult() == ResultKind.Skipped, "Tor not skipped");
        Assert(DownloadService.ComputeSha256Async(priorRunPath).GetAwaiter().GetResult() == originalHash, "Prior state changed");
    }));
tests.Add(("Encryption dialog text and buttons fit at 100%, 125%, 150% and 200%", DialogLayoutTests.Verify));
var failures = 0;
foreach (var (name, test) in tests)
{
    try { test(); Console.WriteLine($"PASS  {name}"); }
    catch (Exception exception) { failures++; Console.WriteLine($"FAIL  {name}: {exception.Message}"); }
}
Console.WriteLine($"{tests.Count - failures}/{tests.Count} tests passed.");
return failures == 0 ? 0 : 1;

static void Unique(IEnumerable<string> values)
{
    var duplicates = values.GroupBy(x => x, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1).Select(x => x.Key).ToArray();
    Assert(duplicates.Length == 0, "Duplicates: " + string.Join(", ", duplicates));
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void TestRequestedMainWindowUi()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        var log = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".log");
        try
        {
            var gate = new BitLockerGateResult();
            gate.Volumes.Add(new BitLockerVolumeStatus("C:", true, "Fully decrypted", 0, "Protection off", "Fully decrypted."));
            using var form = new MainForm(null, gate, () => Task.FromResult(UninstallTests.UiInventory()));
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var tabs = (System.Windows.Forms.TabControl)typeof(MainForm).GetField("_tabs", flags)!.GetValue(form)!;
            var start = (System.Windows.Forms.Button)typeof(MainForm).GetField("_start", flags)!.GetValue(form)!;
            var exit = (System.Windows.Forms.Button)typeof(MainForm).GetField("_exit", flags)!.GetValue(form)!;
            var openLog = (System.Windows.Forms.Button)typeof(MainForm).GetField("_openLog", flags)!.GetValue(form)!;
            Assert(form.Text.StartsWith("PSeeSetup 1.2.6"), "Product branding is wrong");
            Assert(tabs.TabCount == 4 && tabs.TabPages[0].Text == "1  Applications"
                && tabs.TabPages[1].Text == "2  Tweaks" && tabs.TabPages[2].Text == "3  Uninstall Apps"
                && tabs.TabPages[3].Text == "4  Execute", "Four-tab workflow is wrong");
            Assert(!FindControls<System.Windows.Forms.Control>(form).Any(x => x.Text.Contains("simulat", StringComparison.OrdinalIgnoreCase)
                || x.Text.Contains("Joe", StringComparison.OrdinalIgnoreCase) || x.Text.Contains("preset", StringComparison.OrdinalIgnoreCase)), "Removed UI is still present");
            Assert(start.Text == MainForm.LiveSetupButtonText && !openLog.Enabled, "Initial execute/log state is wrong");
            form.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            form.Location = new System.Drawing.Point(-30000, -30000);
            form.ShowInTaskbar = false;
            form.Show();
            System.Windows.Forms.Application.DoEvents();
            for (var index = 0; index < 3; index++)
            {
                tabs.SelectedIndex = index;
                var next = FindControls<System.Windows.Forms.Button>(tabs.TabPages[index]).Single(x => x.Text == MainForm.NextButtonText);
                next.PerformClick();
                Assert(tabs.SelectedIndex == index + 1, "Next does not advance to the correct page");
            }
            var removals = (System.Windows.Forms.DataGridView)typeof(MainForm).GetField("_uninstallGrid", flags)!.GetValue(form)!;
            Assert(removals.Rows.Count == 24 && removals.Rows.Cast<System.Windows.Forms.DataGridViewRow>().All(x => !Convert.ToBoolean(x.Cells[0].Value)), "Removals were selected automatically");
            var paint = removals.Rows.Cast<System.Windows.Forms.DataGridViewRow>().Single(x => ((UninstallDefinition)x.Tag!).Key == "paint");
            var missing = removals.Rows.Cast<System.Windows.Forms.DataGridViewRow>().Single(x => ((UninstallDefinition)x.Tag!).Key == "glidex");
            Assert(!paint.Cells[0].ReadOnly && missing.Cells[0].ReadOnly && missing.DefaultCellStyle.ForeColor == System.Drawing.Color.DimGray, "Installed/missing removal rows are wrong");
            paint.Cells[0].Value = true;
            var selectedProfile = (SetupProfile)typeof(MainForm).GetField("_profile", flags)!.GetValue(form)!;
            Assert(selectedProfile.UninstallApplications.SetEquals(["paint"]), "Removal checkbox does not update selections");
            paint.Cells[0].Value = false;
            File.WriteAllText(log, "UI test log");
            typeof(MainForm).GetMethod("SetLogFile", flags)!.Invoke(form, [log]);
            Assert(openLog.Enabled && openLog.Parent == exit.Parent, "Open log is not available beside Exit");
            typeof(MainForm).GetMethod("SetRunCompleted", flags)!.Invoke(form, null);
            Assert(!start.Enabled && !start.Visible && exit.Enabled && openLog.Enabled, "Completion actions are wrong");
            if (Environment.GetEnvironmentVariable("PCSETUP_TEST_PREVIEW_DIR") is { Length: > 0 } preview)
            {
                // Render the ready-to-execute layout without starting a real run.
                start.Visible = true;
                start.Enabled = true;
                openLog.Enabled = false;
                var logLabel = (System.Windows.Forms.Label)typeof(MainForm).GetField("_logPath", flags)!.GetValue(form)!;
                logLabel.Text = "Run log becomes available after execution starts.";
                Directory.CreateDirectory(preview);
                for (var index = 0; index < tabs.TabCount; index++)
                {
                    tabs.SelectedIndex = index;
                    form.PerformLayout();
                    System.Windows.Forms.Application.DoEvents();
                    using var bitmap = new System.Drawing.Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
                    bitmap.Save(Path.Combine(preview, $"PSeeSetup-tab-{index + 1}.png"));
                }
            }
        }
        catch (Exception exception) { failure = exception; }
        finally { if (File.Exists(log)) File.Delete(log); }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    Assert(thread.Join(TimeSpan.FromSeconds(30)), "Main window UI test timed out");
    if (failure is not null) throw new InvalidOperationException("Main window UI test failed: " + failure.Message, failure);
}
static IEnumerable<T> FindControls<T>(System.Windows.Forms.Control parent) where T : System.Windows.Forms.Control
{
    foreach (System.Windows.Forms.Control child in parent.Controls)
    {
        if (child is T match) yield return match;
        foreach (var descendant in FindControls<T>(child)) yield return descendant;
    }
}
