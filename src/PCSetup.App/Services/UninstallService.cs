using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace PCSetup.Services;

public enum RemovalAvailability { NotInstalled, Installed, Unsupported, Unknown, PendingReboot }
public sealed record InstalledStoreApp(string Name, string PackageFullName, string Publisher,
    bool IsFramework = false, bool NonRemovable = false, string[]? DisplayNames = null);
public sealed record InstalledDesktopApp(string Id, string DisplayName, string Publisher,
    string UninstallString, string QuietUninstallString, bool WindowsInstaller = false, string? ProductCode = null);
public sealed class RemovalInventory
{
    public InstalledStoreApp[] Packages { get; set; } = [];
    public InstalledDesktopApp[] Programs { get; set; } = [];
    public string[] StartNames { get; set; } = [];
    public string[] Errors { get; set; } = [];
    public bool PackagesAvailable { get; set; } = true;
    public bool DesktopAvailable { get; set; } = true;
    public bool StartNamesAvailable { get; set; } = true;
    public bool FeaturesAvailable { get; set; } = true;
    public string? RecallState { get; set; }
}
public sealed record RemovalStatus(RemovalAvailability Availability, string Description)
{
    public bool CanRemove => Availability == RemovalAvailability.Installed;
}
public sealed record UninstallCommand(string Executable, string[] Arguments);

public sealed class UninstallService
{
    private readonly ProcessRunner _process;
    private readonly RunLogger _log;
    private readonly Func<CancellationToken, Task<RemovalInventory>> _scan;
    private readonly Func<string, IEnumerable<string>, CancellationToken, Task<ProcessResult>> _run;
    public UninstallService(ProcessRunner process, RunLogger log,
        Func<CancellationToken, Task<RemovalInventory>>? scan = null,
        Func<string, IEnumerable<string>, CancellationToken, Task<ProcessResult>>? run = null)
    {
        _process = process; _log = log; _scan = scan ?? ScanCoreAsync;
        _run = run ?? ((file, args, token) => process.RunAsync(file, args, TimeSpan.FromMinutes(30), token));
    }
    public Task<RemovalInventory> ScanAsync(CancellationToken token = default) => _scan(token);

    public static bool NameMatches(string actual, IEnumerable<string> names) => names.Any(x =>
        actual.Equals(x, StringComparison.OrdinalIgnoreCase));
    public static IEnumerable<InstalledStoreApp> StoreMatches(UninstallDefinition app, RemovalInventory inventory) =>
        inventory.Packages.Where(x => NameMatches(x.Name, app.PackageNames) ||
            app.Category == "ASUS" && NameMatchesAny(x.DisplayNames ?? [], app.DisplayNames));
    public static IEnumerable<InstalledDesktopApp> DesktopMatches(UninstallDefinition app, RemovalInventory inventory) =>
        inventory.Programs.Where(x => NameMatches(x.DisplayName, app.DisplayNames));
    private static bool NameMatchesAny(IEnumerable<string> actual, IEnumerable<string> names) => actual.Any(x => NameMatches(x, names));

    public static RemovalStatus Status(UninstallDefinition app, RemovalInventory inventory)
    {
        if (!app.StandaloneRemoval)
        {
            if (NameMatchesAny(inventory.StartNames, app.DisplayNames) || inventory.Programs.Any(x => x.DisplayName.Contains("OneNote", StringComparison.OrdinalIgnoreCase)))
                return new(RemovalAvailability.Unsupported, "Part of OneNote; cannot remove separately. OneNote will be kept.");
            return new(inventory.StartNamesAvailable && inventory.DesktopAvailable ? RemovalAvailability.NotInstalled : RemovalAvailability.Unknown,
                "Part of OneNote; no separate supported uninstaller.");
        }
        if (app.FeatureName is not null)
        {
            if (!inventory.FeaturesAvailable) return new(RemovalAvailability.Unknown, "Windows feature detection failed; refresh to retry.");
            if (inventory.RecallState is "DisablePending" or "EnablePending") return new(RemovalAvailability.PendingReboot, "Windows feature change pending; restart and refresh.");
            return inventory.RecallState is "Enabled" or "Disabled"
                ? new(RemovalAvailability.Installed, "Windows optional feature; removal affects this PC and may require a restart.")
                : new(RemovalAvailability.NotInstalled, "Not installed / feature payload already removed.");
        }
        var packages = StoreMatches(app, inventory).ToArray();
        var programs = DesktopMatches(app, inventory).ToArray();
        if (packages.Any(x => x.IsFramework || x.NonRemovable))
            return new(RemovalAvailability.Unsupported, "Windows marks this package as protected or shared; removal is unavailable.");
        if (packages.Length + programs.Length > 0)
            return new(RemovalAvailability.Installed, programs.Length > 0
                ? "Registered desktop uninstaller; may affect all users and show a vendor confirmation."
                : "Microsoft Store app; removes it for the signed-in user.");
        if (app.PackageNames.Length > 0 && !inventory.PackagesAvailable ||
            app.DisplayNames.Length > 0 && !inventory.DesktopAvailable ||
            app.Category == "ASUS" && !inventory.StartNamesAvailable)
            return new(RemovalAvailability.Unknown, "Detection unavailable; refresh to retry.");
        return new(RemovalAvailability.NotInstalled, "Not installed.");
    }

    public async Task<ResultKind> UninstallAsync(UninstallDefinition app, string runDirectory, CancellationToken token)
    {
        // Resolve fresh installed identities; never execute addresses from a saved JSON file.
        var inventory = await ScanAsync(token).ConfigureAwait(false);
        var status = Status(app, inventory);
        _log.Info($"Uninstall {app.Name}: {status.Availability}; {status.Description}");
        if (status.Availability == RemovalAvailability.NotInstalled) return ResultKind.Skipped;
        if (!status.CanRemove) return ResultKind.NeedsVerification;
        var reboot = false;
        if (app.FeatureName is not null)
        {
            var result = await _run(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "dism.exe"),
                ["/English", "/Online", "/Disable-Feature", "/FeatureName:" + app.FeatureName, "/Remove", "/NoRestart"], token).ConfigureAwait(false);
            if (!result.Success) throw new InvalidOperationException($"Recall removal failed: exit {result.ExitCode}. {result.StandardError}");
            reboot = result.RebootRequired;
        }
        else
        {
            foreach (var package in StoreMatches(app, inventory))
            {
                _log.Info($"Removing exact Store package {package.PackageFullName} for the signed-in user.");
                var result = await _run(WindowsPowerShell(), PowerShellArguments(BuildStoreRemovalScript(package.PackageFullName)), token).ConfigureAwait(false);
                if (!result.Success) throw new InvalidOperationException($"{app.Name}: Store removal failed: {result.StandardError}");
            }
            foreach (var program in DesktopMatches(app, inventory).DistinctBy(x => x.UninstallString))
            {
                token.ThrowIfCancellationRequested();
                var command = BuildDesktopCommand(app, program, Path.Combine(runDirectory, "Uninstall-" + app.Key + ".log"));
                _log.Info($"Using the registered uninstaller for {program.DisplayName}; registered publisher={program.Publisher}.");
                var result = await _run(command.Executable, command.Arguments, token).ConfigureAwait(false);
                if (!result.Success) throw new InvalidOperationException($"{app.Name}: uninstaller exit {result.ExitCode}. {result.StandardError}");
                reboot |= result.RebootRequired;
            }
        }
        var final = Status(app, await ScanAsync(token).ConfigureAwait(false));
        if (reboot || final.Availability == RemovalAvailability.PendingReboot) return ResultKind.RebootRequired;
        if (final.Availability == RemovalAvailability.NotInstalled) return ResultKind.Succeeded;
        _log.Warn($"{app.Name}: removal is unconfirmed ({final.Availability}). Refresh after closing any vendor dialog.");
        return ResultKind.NeedsVerification;
    }

    public static string BuildStoreRemovalScript(string packageFullName) =>
        "$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue';Import-Module Appx -ErrorAction Stop;" +
        "Remove-AppxPackage -Package '" + packageFullName.Replace("'", "''") + "' -ErrorAction Stop";

    public static UninstallCommand BuildDesktopCommand(UninstallDefinition app, InstalledDesktopApp program, string log, string? wingetExecutable = null)
    {
        if (!NameMatches(program.DisplayName, app.DisplayNames))
            throw new InvalidDataException("The desktop entry does not match the selected app.");
        if (app.Key == "deno")
        {
            using var logger = new RunLogger();
            return new(wingetExecutable ?? new WingetService(new ProcessRunner(logger), logger).Locate()
                ?? throw new InvalidOperationException("WinGet is needed to remove Deno."),
                ["uninstall", "--id", "DenoLand.Deno", "--exact", "--silent", "--disable-interactivity", "--accept-source-agreements"]);
        }
        if (program.WindowsInstaller)
        {
            if (!Guid.TryParse(program.ProductCode, out var code)) throw new InvalidDataException("MSI product identity is missing.");
            return new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "msiexec.exe"),
                ["/x", code.ToString("B"), "/qn", "/norestart", "/L*v", Path.GetFullPath(log)]);
        }
        var line = string.IsNullOrWhiteSpace(program.QuietUninstallString) ? program.UninstallString : program.QuietUninstallString;
        var parts = ParseCommandLine(Environment.ExpandEnvironmentVariables(line));
        if (parts.Length == 0 || !Path.IsPathFullyQualified(parts[0]) || !File.Exists(parts[0]) ||
            !parts[0].EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            new[] { "cmd.exe", "powershell.exe", "pwsh.exe", "wscript.exe", "cscript.exe", "rundll32.exe", "msiexec.exe" }.Contains(Path.GetFileName(parts[0]), StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("No supported, directly executable vendor uninstaller was registered.");
        return new(parts[0], parts.Skip(1).ToArray());
    }

    public static string[] ParseCommandLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return [];
        var pointer = CommandLineToArgvW(line, out var count);
        if (pointer == IntPtr.Zero) throw new InvalidDataException("The registered uninstaller command is invalid.");
        try { return Enumerable.Range(0, count).Select(x => Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer, x * IntPtr.Size))!).ToArray(); }
        finally { LocalFree(pointer); }
    }
    private static string WindowsPowerShell() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
    private static string[] PowerShellArguments(string script) => ["-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script))];

    private async Task<RemovalInventory> ScanCoreAsync(CancellationToken token)
    {
        var result = await _process.RunAsync(WindowsPowerShell(), PowerShellArguments(InventoryScript), TimeSpan.FromMinutes(2), token, logOutput: false).ConfigureAwait(false);
        var line = result.StandardOutput.Split(['\r', '\n']).LastOrDefault(x => x.StartsWith("PCSETUP_UNINSTALL_INVENTORY:"));
        var inventory = result.Success && line is not null
            ? JsonSerializer.Deserialize<RemovalInventory>(line["PCSETUP_UNINSTALL_INVENTORY:".Length..], new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!
            : new RemovalInventory { PackagesAvailable = false, StartNamesAvailable = false, FeaturesAvailable = false, Errors = ["Windows app inventory could not be queried."] };
        var programs = new List<InstalledDesktopApp>();
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null) continue;
                foreach (var key in uninstall.GetSubKeyNames())
                {
                    using var entry = uninstall.OpenSubKey(key);
                    var name = Convert.ToString(entry?.GetValue("DisplayName")) ?? "";
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    var command = Convert.ToString(entry?.GetValue("UninstallString")) ?? "";
                    var isMsi = Convert.ToString(entry?.GetValue("WindowsInstaller")) == "1";
                    var code = Guid.TryParse(key, out var id) ? id.ToString("B") : null;
                    programs.Add(new($"{hive}/{view}/{key}", name, Convert.ToString(entry?.GetValue("Publisher")) ?? "",
                        command, Convert.ToString(entry?.GetValue("QuietUninstallString")) ?? "", isMsi, code));
                }
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
            { inventory.DesktopAvailable = false; _log.Warn("Desktop inventory: " + exception.Message); }
        }
        inventory.Programs = programs.ToArray();
        foreach (var error in inventory.Errors) _log.Warn("Uninstall inventory: " + error);
        return inventory;
    }

    public const string InventoryScript = """
        $ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue'
        $data=@{Packages=@();StartNames=@();Errors=@();PackagesAvailable=$true;StartNamesAvailable=$true;FeaturesAvailable=$true;RecallState=$null}
        $start=@()
        try { Import-Module StartLayout -ErrorAction Stop; $start=@(Get-StartApps -ErrorAction Stop);$data.StartNames=@($start | ForEach-Object {$_.Name}) }
        catch {$data.StartNamesAvailable=$false;$data.Errors+=('Start menu inventory: '+$_.Exception.Message)}
        try {
          Import-Module Appx -ErrorAction Stop
          $data.Packages=@(Get-AppxPackage -ErrorAction Stop | ForEach-Object {
            $package=$_;$family=$package.PackageFamilyName+'!'
            @{Name=$package.Name;PackageFullName=$package.PackageFullName;Publisher=$package.Publisher;
              IsFramework=[bool]$package.IsFramework;NonRemovable=[bool]$package.NonRemovable;
              DisplayNames=@($start | Where-Object {$_.AppID.StartsWith($family,[StringComparison]::OrdinalIgnoreCase)} | ForEach-Object {$_.Name})}
          })
        } catch {$data.PackagesAvailable=$false;$data.Errors+=('Store inventory: '+$_.Exception.Message)}
        try {Import-Module Dism -ErrorAction Stop;$feature=Get-WindowsOptionalFeature -Online -ErrorAction Stop | Where-Object FeatureName -eq 'Recall';if($feature){$data.RecallState=[string]$feature.State}}
        catch {$data.FeaturesAvailable=$false;$data.Errors+=('Recall inventory: '+$_.Exception.Message)}
        'PCSETUP_UNINSTALL_INVENTORY:'+($data | ConvertTo-Json -Depth 6 -Compress)
        """;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CommandLineToArgvW(string command, out int count);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
}
