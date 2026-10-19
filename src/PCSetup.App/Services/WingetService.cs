namespace PCSetup.Services;

public sealed class WingetService
{
    private readonly ProcessRunner _process;
    private readonly RunLogger _log;
    private readonly Func<string?> _locate;
    private readonly Func<string, IReadOnlyList<string>, TimeSpan, CancellationToken, Task<ProcessResult>> _run;
    public string? LastInstallerArchitecture { get; private set; }

    public WingetService(ProcessRunner process, RunLogger log,
        Func<string?>? locate = null,
        Func<string, IReadOnlyList<string>, TimeSpan, CancellationToken, Task<ProcessResult>>? run = null)
    {
        _process = process;
        _log = log;
        _locate = locate ?? Locate;
        _run = run ?? ((file, args, timeout, token) => _process.RunAsync(file, args, timeout, token));
    }

    public string? Locate()
    {
        var localAlias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps", "winget.exe");
        if (File.Exists(localAlias)) return localAlias;

        var windowsApps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
        try
        {
            var packaged = Directory.EnumerateDirectories(windowsApps, ArchitecturePolicy.AppInstallerPattern(ArchitecturePolicy.OSArchitecture))
                .OrderByDescending(x => x, StringComparer.OrdinalIgnoreCase)
                .Select(x => Path.Combine(x, "winget.exe"))
                .FirstOrDefault(File.Exists);
            if (packaged is not null) return packaged;
        }
        catch (UnauthorizedAccessException) { }

        return null;
    }

    public bool IsUsable()
    {
        var path = Locate();
        if (path is null) return false;
        try { return _process.Run(path, ["--version"], TimeSpan.FromSeconds(30), false).ExitCode == 0; }
        catch { return false; }
    }

    public async Task EnsureAsync(CancellationToken cancellationToken)
    {
        if (!IsUsable())
        {
            _log.Warn("WinGet is missing or not registered. Requesting the supported App Installer registration.");
            var register = "Add-AppxPackage -RegisterByFamilyName -MainPackage Microsoft.DesktopAppInstaller_8wekyb3d8bbwe";
            await _process.RunAsync(GetWindowsPowerShell(), ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", register],
                TimeSpan.FromMinutes(5), cancellationToken).ConfigureAwait(false);
        }

        if (!IsUsable())
        {
            _log.Warn("App Installer registration did not restore WinGet. Running Microsoft's WinGet repair workflow.");
            var repair = "$ProgressPreference='SilentlyContinue';" +
                         "Install-PackageProvider -Name NuGet -Force | Out-Null;" +
                         "Install-Module -Name Microsoft.WinGet.Client -Force -Repository PSGallery -Scope AllUsers | Out-Null;" +
                         "Repair-WinGetPackageManager -AllUsers";
            var result = await _process.RunAsync(GetWindowsPowerShell(), ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", repair],
                TimeSpan.FromMinutes(20), cancellationToken).ConfigureAwait(false);
            if (!result.Success || !IsUsable())
                throw new InvalidOperationException("WinGet could not be installed or repaired. No application installs were attempted.");
        }

        var winget = Locate()!;
        var update = await _process.RunAsync(winget, ["source", "update", "--disable-interactivity"], TimeSpan.FromMinutes(10), cancellationToken).ConfigureAwait(false);
        if (!update.Success) _log.Warn("WinGet source refresh returned an error; package installs will still be attempted.");
    }

    public async Task<ProcessResult> InstallAsync(AppDefinition app, bool interactiveUser,
        CancellationToken cancellationToken, bool force = false)
    {
        if (app.Key == SnapdragonSupport.AppKey) throw new InvalidOperationException("Snapdragon Control Panel must use its verified Qualcomm MSIX installer.");
        var winget = _locate() ?? throw new InvalidOperationException("WinGet is unavailable.");
        var plan = ArchitecturePolicy.For(app);
        if (!plan.CanInstall) throw new InvalidOperationException(plan.Reason);
        LastInstallerArchitecture = null;
        var arguments = BuildInstallArguments(app, force).ToList();
        var source = app.WingetSource ?? "winget";
        // The query and install use the same architecture and scope. A missing native
        // installer must never silently turn into an emulated driver installation.
        if (!source.Equals("msstore", StringComparison.OrdinalIgnoreCase))
        {
            ProcessResult? lastQuery = null;
            var found = false;
            foreach (var candidate in ArchitecturePolicy.InstallerArchitectures(app))
            {
                var query = await _run(winget, BuildShowArguments(app, installerArchitecture: candidate),
                    TimeSpan.FromMinutes(2), cancellationToken).ConfigureAwait(false);
                lastQuery = query;
                if (HasApplicableInstaller(query))
                {
                    arguments = BuildInstallArguments(app, force, installerArchitecture: candidate).ToList();
                    LastInstallerArchitecture = candidate;
                    _log.Info($"{app.Name}: selected applicable {candidate} installer.");
                    found = true;
                    break;
                }
                _log.Info($"{app.Name}: no applicable {candidate} installer for this scope; checking the next allowed architecture.");
            }
            if (!found)
                return new ProcessResult(lastQuery?.Success == false ? lastQuery.ExitCode : unchecked((int)0x8A150010),
                    lastQuery?.StandardOutput ?? "",
                    "No applicable installer URL and SHA256 were found for the permitted architectures and scope. " + lastQuery?.StandardError);
        }
        else LastInstallerArchitecture = "Microsoft Store selected";

        if (interactiveUser)
        {
            var exitCode = await Task.Run(() => _process.RunAsInteractiveUser(winget, arguments, TimeSpan.FromMinutes(45)), cancellationToken)
                .ConfigureAwait(false);
            return new ProcessResult(exitCode, "", "");
        }
        return await _run(winget, arguments, TimeSpan.FromMinutes(45), cancellationToken).ConfigureAwait(false);
    }

    public static IReadOnlyList<string> BuildInstallArguments(AppDefinition app, bool force = false,
        System.Runtime.InteropServices.Architecture? architecture = null, string? installerArchitecture = null)
    {
        if (app.Key == SnapdragonSupport.AppKey) throw new InvalidOperationException("Snapdragon Control Panel has no WinGet package; use Qualcomm's verified MSIX.");
        var source = string.IsNullOrWhiteSpace(app.WingetSource) ? "winget" : app.WingetSource;
        var plan = ArchitecturePolicy.For(app, architecture);
        if (!plan.CanInstall) throw new InvalidOperationException(plan.Reason);
        if ((architecture ?? ArchitecturePolicy.OSArchitecture) == System.Runtime.InteropServices.Architecture.X64 && installerArchitecture == "arm64")
            throw new InvalidOperationException("ARM64 packages must not be selected on Intel/AMD Windows.");
        if (plan.Kind == CompatibilityKind.Native && plan.Architecture == "arm64"
            && installerArchitecture is not null && installerArchitecture != "arm64")
            throw new InvalidOperationException("Native ARM integrations cannot be downgraded to emulated installers.");
        var arguments = new List<string>
        {
            "install", "--id", app.WingetId, "--exact", "--silent", "--disable-interactivity",
            "--accept-source-agreements", "--accept-package-agreements", "--source", source
        };
        // Microsoft Store packages are already installed per user; --scope can make
        // Store product-ID installs report that no applicable installer was found.
        if (!source.Equals("msstore", StringComparison.OrdinalIgnoreCase))
        {
            var selectedArchitecture = installerArchitecture ?? plan.Architecture;
            // OBS publishes ARM64 as a portable ZIP, which has no machine scope.
            if (!(app.Key == "obs" && selectedArchitecture == "arm64") && app.Key != "unraidusb")
            {
                arguments.Add("--scope");
                arguments.Add(app.Context == InstallContext.User ? "user" : "machine");
            }
            if (selectedArchitecture is not null)
            {
                arguments.Add("--architecture");
                arguments.Add(selectedArchitecture);
            }
        }
        if (force) arguments.Add("--force");
        if (app.Key == "parsec" && (architecture ?? ArchitecturePolicy.OSArchitecture) == System.Runtime.InteropServices.Architecture.Arm64)
        {
            arguments.Add("--custom");
            arguments.Add("/silent /percomputer /norun /nocleanuser /nodrivers");
        }
        return arguments;
    }

    public static bool HasApplicableInstaller(ProcessResult result)
    {
        // `show` can return exit 0 with "No applicable installer found".
        // Require positive evidence; unfamiliar/localized output fails closed.
        return result.Success
            && !result.StandardOutput.Contains("No applicable installer", StringComparison.OrdinalIgnoreCase)
            && System.Text.RegularExpressions.Regex.IsMatch(result.StandardOutput,
                @"(?im)^\s*Installer Url:\s*https://\S+\s*$")
            && System.Text.RegularExpressions.Regex.IsMatch(result.StandardOutput,
                @"(?im)^\s*Installer SHA256:\s*[0-9a-f]{64}\s*$");
    }

    public static IReadOnlyList<string> BuildShowArguments(AppDefinition app,
        System.Runtime.InteropServices.Architecture? architecture = null, string? installerArchitecture = null) =>
        ShowFromInstallArguments(BuildInstallArguments(app, false, architecture, installerArchitecture));

    private static IReadOnlyList<string> ShowFromInstallArguments(IReadOnlyList<string> install)
    {
        var query = new List<string> { "show" };
        for (var index = 1; index < install.Count; index++)
        {
            if (install[index] == "--custom") { index++; continue; }
            if (install[index] is not "--silent" and not "--force" and not "--accept-package-agreements") query.Add(install[index]);
        }
        return query;
    }

    public bool IsInstalled(AppDefinition app)
    {
        var winget = Locate();
        if (winget is null) return false;
        try
        {
            var result = _process.Run(winget, ["list", "--id", app.WingetId, "--exact", "--accept-source-agreements", "--disable-interactivity"],
                TimeSpan.FromMinutes(2), false);
            return result.ExitCode == 0 && result.StandardOutput.Contains(app.WingetId, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static string GetWindowsPowerShell() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
}
