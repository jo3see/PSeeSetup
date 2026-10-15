using Microsoft.Win32;

namespace PCSetup.Services;

public sealed class ApplicationInstaller
{
    private readonly WingetService _winget;
    private readonly DownloadService _downloads;
    private readonly ProcessRunner _process;
    private readonly RunLogger _log;
    private readonly Func<bool> _spotifyStoreInstalled;
    private readonly Func<bool> _bluebubblesStoreInstalled;
    private readonly Func<AppDefinition, bool> _isInstalled;
    private readonly Func<AppDefinition, VendorFallback, string, CancellationToken, Task<ResultKind>> _installFallback;
    public string? LastInstallerArchitecture { get; private set; }

    public ApplicationInstaller(WingetService winget, DownloadService downloads, ProcessRunner process, RunLogger log,
        Func<bool>? spotifyStoreInstalled = null, Func<AppDefinition, bool>? isInstalled = null,
        Func<AppDefinition, VendorFallback, string, CancellationToken, Task<ResultKind>>? installFallback = null,
        Func<bool>? bluebubblesStoreInstalled = null)
    {
        _winget = winget;
        _downloads = downloads;
        _process = process;
        _log = log;
        _spotifyStoreInstalled = spotifyStoreInstalled ?? (() => HasStorePackage("SpotifyAB.SpotifyMusic"));
        _bluebubblesStoreInstalled = bluebubblesStoreInstalled ?? (() => HasStorePackage("23344BlueBubbles.BlueBubbles"));
        _isInstalled = isInstalled ?? DetectInstalled;
        _installFallback = installFallback ?? InstallFallbackAsync;
    }

    public async Task<ResultKind> InstallAsync(AppDefinition app, string cacheDirectory,
        CancellationToken cancellationToken)
    {
        LastInstallerArchitecture = null;
        var plan = ArchitecturePolicy.For(app);
        if (!plan.CanInstall)
        {
            _log.Warn($"{app.Name}: {plan.Label}. {plan.Reason}");
            return plan.Kind == CompatibilityKind.Skipped ? ResultKind.Skipped : ResultKind.NeedsVerification;
        }
        _log.Info($"{app.Name}: {plan.Label}; initial preference={plan.Architecture ?? "WinGet automatic"}.");
        if (app.Key == "powerbi" && ArchitecturePolicy.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64
            && Environment.OSVersion.Version.Build < 26100)
        {
            _log.Warn("Power BI requires a current, fully updated Windows 11 ARM installation.");
            return ResultKind.NeedsVerification;
        }
        if (app.Key == "equalizerapo" && HasWrongArmEqualizer())
        {
            _log.Warn("An x64/x86 Equalizer APO installation exists on ARM. Remove it manually before installing the native version.");
            return ResultKind.NeedsVerification;
        }
        if (IsInstalled(app) && !app.UpgradeIfInstalled)
        {
            LastInstallerArchitecture = app.Key == "teamviewer"
                ? $"{InstalledTeamViewerArchitecture()?.ToString().ToLowerInvariant() ?? "unknown"} (existing installation, unchanged)"
                : "Existing installation (unchanged)";
            _log.Info($"{app.Name} is already installed; its installer was skipped.");
            return ResultKind.Skipped;
        }

        // The old WinGet Unraid manifest hashes a moving download. Use the
        // independently pinned and signed versioned vendor release on either OS.
        if (app.Key == "unraidusb")
        {
            if (IsInstalled(Catalog.AppByKey("rpiimager")))
            {
                _log.Warn("Unraid USB Creator was held: its current vendor installer shares Raspberry Pi Imager installation identifiers and can uninstall that application. No installer was started. Install Unraid only after resolving this vendor packaging conflict.");
                return ResultKind.NeedsVerification;
            }
            return await _installFallback(app, app.Fallback!, cacheDirectory, cancellationToken).ConfigureAwait(false);
        }

        var preferredNative = ArchitecturePolicy.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64
            ? ArchitecturePolicy.PreferredArmFallbackFor(app) : null;
        if (preferredNative is not null)
        {
            try
            {
                _log.Info($"{app.Name}: trying the official native ARM64 installer before Intel emulation.");
                var nativeResult = await _installFallback(app, preferredNative, cacheDirectory, cancellationToken).ConfigureAwait(false);
                LastInstallerArchitecture = "arm64 (official vendor payload)";
                if (nativeResult is ResultKind.Succeeded or ResultKind.RebootRequired) return nativeResult;
                if (app.Key == "teamviewer")
                {
                    _log.Warn("TeamViewer ARM installation needs verification; an Intel installer will not be launched over it.");
                    return ResultKind.NeedsVerification;
                }
                _log.Warn($"{app.Name}: native installer did not confirm installation; trying WinGet architecture selection.");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) { _log.Warn($"{app.Name}: native vendor install unavailable: {exception.Message}; trying WinGet."); }
        }

        var userContext = app.Context == InstallContext.User;
        var result = await _winget.InstallAsync(app, userContext, cancellationToken).ConfigureAwait(false);
        LastInstallerArchitecture = _winget.LastInstallerArchitecture;
        var installed = IsInstalled(app);
        if (result.Success && app.RequireKnownInstallPath && !installed)
        {
            _log.Warn($"WinGet recorded {app.Name}, but its required program file is missing. Forcing a repair installation.");
            result = await _winget.InstallAsync(app, userContext, cancellationToken, true).ConfigureAwait(false);
            LastInstallerArchitecture = _winget.LastInstallerArchitecture;
            installed = IsInstalled(app);
        }
        var wingetCompleted = WingetInstallCountsAsCompleted(app, result, installed);
        if (wingetCompleted)
        {
            _log.Info($"WinGet completed {app.Name}; manifest hash validation was enforced by WinGet.");
            return result.RebootRequired ? ResultKind.RebootRequired : ResultKind.Succeeded;
        }

        var fallback = ArchitecturePolicy.FallbackFor(app);
        if (fallback is null)
        {
            throw new InvalidOperationException($"{app.Name}: WinGet failed (0x{unchecked((uint)result.ExitCode):X8}). {result.StandardError.Trim()} See the WinGet installer log for details.");
        }

        // Prevent a partially successful Teams install from being followed by the bootstrapper.
        if (app.Key.Equals("teams", StringComparison.OrdinalIgnoreCase) && IsInstalled(app))
            return ResultKind.Succeeded;

        _log.Warn($"WinGet could not install {app.Name}; trying its official, signature-checked fallback.");
        return await _installFallback(app, fallback, cacheDirectory, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ResultKind> InstallFallbackAsync(AppDefinition app, VendorFallback fallback, string cacheDirectory,
        CancellationToken cancellationToken)
    {
        var installer = await _downloads.DownloadAndVerifyAsync(fallback, cacheDirectory, cancellationToken).ConfigureAwait(false);
        LastInstallerArchitecture = ArchitecturePolicy.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64
            && (ArchitecturePolicy.PreferredArmFallbackFor(app) is not null || app.Key is "equalizerapo" or "jdownloader2" or "translucenttb")
            ? "arm64 (official vendor payload)" : "Official vendor installer";
        if (fallback.PortableZip)
        {
            _downloads.InstallPortable(fallback, installer, app.Context);
            return ResultKind.Succeeded;
        }

        var executable = installer;
        var arguments = SplitArguments(fallback.SilentArguments);
        if (fallback.MsiArchiveEntry is not null)
        {
            installer = _downloads.ExtractVerifiedArmMsi(fallback, installer);
            executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "msiexec.exe");
            var installerLog = Path.Combine(string.IsNullOrEmpty(_log.Path) ? cacheDirectory : Path.GetDirectoryName(_log.Path)!,
                "TeamViewer-ARM64-install.log");
            arguments = MsiPackage.SilentInstallArguments(installer, installerLog);
            _log.Info($"TeamViewer ARM MSI log: {installerLog}");
        }
        if (app.Key == "unraidusb") LastInstallerArchitecture = "x64 (official vendor payload; x86 bootstrapper)";
        ProcessResult vendor;
        try
        {
            vendor = await _process.RunAsync(executable, arguments,
                TimeSpan.FromMinutes(45), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (app.Key == "teamviewer")
        {
            _log.Warn($"TeamViewer installer execution could not be confirmed: {exception.Message}. No second installer will be started.");
            return ResultKind.NeedsVerification;
        }
        if (app.Key == "teamviewer")
        {
            // File/registry/service registration can finish after the installer exits.
            // A failed or unconfirmed started ARM install must not trigger a second architecture.
            var confirmed = vendor.Success && await WaitForTeamViewerAsync(fallback.MsiArchiveEntry is not null, cancellationToken).ConfigureAwait(false);
            if (!confirmed)
            {
                _log.Warn($"TeamViewer installer exit {vendor.ExitCode}; native program detection is unconfirmed. See its installer log.");
                return vendor.RebootRequired ? ResultKind.RebootRequired : ResultKind.NeedsVerification;
            }
        }
        else if (vendor.Success && !IsInstalled(app))
        {
            _log.Warn($"{app.Name}'s installer exited successfully but the application was not detected.");
            return ResultKind.NeedsVerification;
        }
        if (!vendor.Success) throw new InvalidOperationException($"{app.Name}: vendor installer failed (0x{unchecked((uint)vendor.ExitCode):X8}). {vendor.StandardError.Trim()}");
        return vendor.RebootRequired ? ResultKind.RebootRequired : ResultKind.Succeeded;
    }

    private async Task<bool> WaitForTeamViewerAsync(bool requireArm, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 16; attempt++)
        {
            if (IsInstalled(Catalog.AppByKey("teamviewer")) && (!requireArm ||
                InstalledTeamViewerArchitecture() == System.Runtime.InteropServices.Architecture.Arm64)) return true;
            if (attempt < 15) await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
        }
        return false;
    }

    public static System.Runtime.InteropServices.Architecture? InstalledTeamViewerArchitecture(IEnumerable<string>? paths = null) =>
        (paths ?? KnownInstallPaths(Catalog.AppByKey("teamviewer")))
        .Select(BinaryArchitecture.Read).FirstOrDefault(x => x is not null);

    public bool IsInstalled(AppDefinition app) => _isInstalled(app);

    private bool DetectInstalled(AppDefinition app)
    {
        if (app.Key == "teamviewer") return InstalledTeamViewerArchitecture() is not null;
        if (app.Key == "spotify" && _spotifyStoreInstalled()) return true;
        if (app.Key == "bluebubbles" && _bluebubblesStoreInstalled()) return true;
        if (app.Key == "equalizerapo" && ArchitecturePolicy.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64)
            return BinaryArchitecture.Read(EqualizerLibraryPath()) == System.Runtime.InteropServices.Architecture.Arm64;
        if (app.RequireKnownInstallPath) return HasUsableKnownInstallPath(app);
        if (HasKnownInstallPath(app)) return true;
        if (IsRegisteredApplication(app)) return true;
        return _winget.IsInstalled(app);
    }

    private bool HasStorePackage(string identityName)
    {
        try
        {
            var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
            // Current signed-in user only: provisioning alone is not an installed app.
            var script = "Import-Module Appx -ErrorAction Stop;" +
                "Get-AppxPackage -Name '" + identityName.Replace("'", "''") + "' -ErrorAction Stop | Where-Object { $_.Status -eq 'Ok' -and $_.InstallLocation -and (Test-Path -LiteralPath $_.InstallLocation) } | ForEach-Object { 'PCSETUP_STORE_INSTALLED' }";
            var result = _process.Run(powershell, ["-NoProfile", "-NonInteractive", "-Command", script], TimeSpan.FromSeconds(45), false);
            return result.Success && result.StandardOutput.Split(['\r', '\n']).Any(x => x.Trim() == "PCSETUP_STORE_INSTALLED");
        }
        catch (Exception exception) { _log.Warn($"Store detection ({identityName}): {exception.Message}"); return false; }
    }

    private bool HasUsableKnownInstallPath(AppDefinition app)
    {
        foreach (var path in KnownInstallPaths(app))
        {
            if (app.Key == "powershell" && ArchitecturePolicy.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64
                && !path.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase)
                && BinaryArchitecture.Read(path) != System.Runtime.InteropServices.Architecture.Arm64) continue;
            if (Directory.Exists(path) && app.ValidationArguments is null) return true;
            if (!File.Exists(path)) continue;
            if (app.ValidationArguments is null) return true;
            try
            {
                var validation = _process.Run(path, app.ValidationArguments, TimeSpan.FromSeconds(30), false);
                if (validation.Success) return true;
                _log.Warn($"{app.Name} was found at {path}, but its validation command returned exit {validation.ExitCode}.");
            }
            catch (Exception exception)
            {
                _log.Warn($"{app.Name} was found at {path}, but could not run: {exception.Message}");
            }
        }
        return false;
    }

    public static bool WingetInstallCountsAsCompleted(AppDefinition app, ProcessResult result, bool installed) =>
        app.RequireKnownInstallPath ? installed : result.Success || installed;

    public static bool HasKnownInstallPath(AppDefinition app)
    {
        foreach (var path in KnownInstallPaths(app))
        {
            if (File.Exists(path) || Directory.Exists(path)) return true;
        }
        return false;
    }

    public static IEnumerable<string> KnownInstallPaths(AppDefinition app, string? desktopDirectory = null)
    {
        foreach (var path in BinaryArchitecture.ExpandInstallPaths(app.DetectionPaths ?? [])) yield return path;
        if (app.Key == "tor")
            yield return Path.Combine(desktopDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "Tor Browser", "Browser", "firefox.exe");
    }

    private static bool IsRegisteredApplication(AppDefinition app)
    {
        var name = app.DetectionName ?? app.Name;
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null) continue;
                foreach (var childName in uninstall.GetSubKeyNames())
                {
                    using var child = uninstall.OpenSubKey(childName);
                    var displayName = Convert.ToString(child?.GetValue("DisplayName")) ?? "";
                    if (displayName.Equals(name, StringComparison.OrdinalIgnoreCase)
                        || displayName.StartsWith(name + " ", StringComparison.OrdinalIgnoreCase)
                        || displayName.StartsWith(name + " (", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (System.Security.SecurityException) { }
        }
        return false;
    }

    public void OpenEqualizerConfiguratorIfPresent()
    {
        if (ArchitecturePolicy.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64
            && BinaryArchitecture.Read(EqualizerLibraryPath()) != System.Runtime.InteropServices.Architecture.Arm64) return;
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "EqualizerAPO", "DeviceSelector.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "EqualizerAPO", "Configurator.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "EqualizerAPO", "Editor.exe")
        };
        var path = candidates.FirstOrDefault(File.Exists);
        if (path is null) return;
        try
        {
            System.Diagnostics.Process.Start(BuildEqualizerStartInfo(path));
            _log.Info("Opened the Equalizer APO configuration tool.");
        }
        catch (Exception exception) { _log.Warn($"Equalizer APO is installed, but its configuration tool did not open: {exception.Message}"); }
    }

    private static string EqualizerLibraryPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "EqualizerAPO", "EqualizerAPO.dll");

    private static bool HasWrongArmEqualizer() =>
        ArchitecturePolicy.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64
        && File.Exists(EqualizerLibraryPath())
        && BinaryArchitecture.Read(EqualizerLibraryPath()) != System.Runtime.InteropServices.Architecture.Arm64;

    public static System.Diagnostics.ProcessStartInfo BuildEqualizerStartInfo(string executablePath) => new(executablePath)
    {
        UseShellExecute = true,
        WorkingDirectory = Path.GetDirectoryName(executablePath) ?? ""
    };

    private static string[] SplitArguments(string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments)) return [];
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        foreach (var character in arguments)
        {
            if (character == '"') { quoted = !quoted; continue; }
            if (char.IsWhiteSpace(character) && !quoted)
            {
                if (current.Length > 0) { result.Add(current.ToString()); current.Clear(); }
            }
            else current.Append(character);
        }
        if (current.Length > 0) result.Add(current.ToString());
        return result.ToArray();
    }
}
