using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace PCSetup.Services;

public sealed class RegistryTweakService
{
    public const string PowerShell7ProfileGuid = "{574e775e-4f2a-5b96-ac1e-a2962a402336}";
    private readonly SetupState _state;
    private readonly ProcessRunner _process;
    private readonly RunLogger _log;

    public RegistryTweakService(SetupState state, ProcessRunner process, RunLogger log)
    {
        _state = state;
        _process = process;
        _log = log;
    }

    public async Task ApplyAsync(TweakDefinition tweak, CancellationToken cancellationToken)
    {
        switch (tweak.Key)
        {
            case "telemetry": ApplyTelemetryMinimum(); break;
            case "activity_history":
                SetDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 0);
                SetDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", 0);
                break;
            case "powershell_telemetry":
                SetString(Registry.CurrentUser, "Environment", "POWERSHELL_TELEMETRY_OPTOUT", "1");
                Environment.SetEnvironmentVariable("POWERSHELL_TELEMETRY_OPTOUT", "1", EnvironmentVariableTarget.User);
                break;
            case "folder_discovery":
                SetString(Registry.CurrentUser, @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\Bags\AllFolders\Shell", "FolderType", "NotSpecified");
                break;
            case "classic_menu":
                SetString(Registry.CurrentUser, @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32", "", "");
                break;
            case "remove_home": RemoveExplorerHome(); break;
            case "remove_gallery": RemoveExplorerGallery(); break;
            case "hide_extensions":
                SetDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "HideFileExt", 1);
                break;
            case "disable_gamedvr":
                SetDword(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", 0);
                SetDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", 0);
                break;
            case "disable_hibernation":
                EnsureSuccess(await _process.RunAsync("powercfg.exe", ["/hibernate", "off"], TimeSpan.FromMinutes(2), cancellationToken));
                SetDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", 0);
                break;
            case "power_button_sleep":
                EnsureSuccess(await _process.RunAsync("powercfg.exe", ["/setacvalueindex", "SCHEME_CURRENT", "SUB_BUTTONS", "PBUTTONACTION", "1"], TimeSpan.FromMinutes(2), cancellationToken));
                EnsureSuccess(await _process.RunAsync("powercfg.exe", ["/setdcvalueindex", "SCHEME_CURRENT", "SUB_BUTTONS", "PBUTTONACTION", "1"], TimeSpan.FromMinutes(2), cancellationToken));
                EnsureSuccess(await _process.RunAsync("powercfg.exe", ["/setactive", "SCHEME_CURRENT"], TimeSpan.FromMinutes(2), cancellationToken));
                break;
            case "disable_storage_sense":
                SetDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy", "01", 0);
                break;
            case "taskbar_end_task":
                SetDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings", "TaskbarEndTask", 1);
                break;
            case "terminal_ps7": SetTerminalDefault(); break;
            case "disable_web_search":
                SetDword(Registry.CurrentUser, @"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1);
                break;
            case "disable_start_recommendations":
                SetDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Start_ShowRecommendations", 0);
                break;
            case "disable_snap": DisableSnap(); break;
            case "disable_dynamic_lighting":
                SetDword(Registry.CurrentUser, @"Software\Microsoft\Lighting", "AmbientLightingEnabled", 0);
                break;
            case "disable_sticky_hotkey":
                SetString(Registry.CurrentUser, @"Control Panel\Accessibility\StickyKeys", "Flags", "506");
                break;
            case "hide_task_view":
                SetDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowTaskViewButton", 0);
                break;
            case "detailed_bsod":
                SetDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\CrashControl", "DisplayParameters", 1);
                break;
            case "disk_cleanup":
                var systemDrive = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\') ?? "C:";
                var cleanup = await _process.RunAsync(Path.Combine(Environment.SystemDirectory, "cleanmgr.exe"),
                    ["/VERYLOWDISK", "/d", systemDrive], TimeSpan.FromMinutes(30), cancellationToken);
                EnsureSuccess(cleanup);
                break;
            case "edge_lean": ApplyLeanEdgePolicies(); break;
            default: throw new InvalidOperationException($"Unknown tweak: {tweak.Key}");
        }
        _log.Info($"Applied tweak: {tweak.Name}.");
    }

    private void ApplyTelemetryMinimum()
    {
        using var currentVersion = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var edition = Convert.ToString(currentVersion?.GetValue("EditionID")) ?? "";
        var enterpriseMinimum = edition.Contains("Enterprise", StringComparison.OrdinalIgnoreCase)
                                || edition.Contains("Education", StringComparison.OrdinalIgnoreCase)
                                || edition.Contains("Server", StringComparison.OrdinalIgnoreCase);
        SetDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", enterpriseMinimum ? 0 : 1);
    }

    private void DisableSnap()
    {
        var advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        SetDword(Registry.CurrentUser, advanced, "EnableSnapAssistFlyout", 0);
        SetDword(Registry.CurrentUser, advanced, "SnapAssist", 0);
        SetDword(Registry.CurrentUser, advanced, "SnapFill", 0);
        SetDword(Registry.CurrentUser, advanced, "EnableSnapBar", 0);
        SetDword(Registry.CurrentUser, advanced, "EnableTaskGroups", 0);
        SetString(Registry.CurrentUser, @"Control Panel\Desktop", "WindowArrangementActive", "0");
    }

    private void RemoveExplorerHome()
    {
        SetDword(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", "HubMode", 1);
        DeleteKey(Registry.LocalMachine,
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace_36354489\{f874310e-b6b7-47dc-bc84-b9e6b38f5903}");
    }

    private void RemoveExplorerGallery() => DeleteKey(Registry.LocalMachine,
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace_41040327\{e88865ea-0e1c-4e20-9aa6-edcd0212c87c}");

    private void SetTerminalDefault()
    {
        var powerShell7 = LocatePowerShell7()
            ?? throw new InvalidOperationException("PowerShell 7 was not found after installation; the Windows Terminal default was not changed.");
        var validation = _process.Run(powerShell7, PowerShellValidationArguments(), TimeSpan.FromSeconds(30), false);
        if (!validation.Success)
            throw new InvalidOperationException($"PowerShell 7 was found but could not run (exit {validation.ExitCode}); the Windows Terminal default was not changed.");
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = new List<string>
        {
            Path.Combine(localAppData,
                "Packages", "Microsoft.WindowsTerminal_8wekyb3d8bbwe", "LocalState", "settings.json"),
            Path.Combine(localAppData,
                "Packages", "Microsoft.WindowsTerminalPreview_8wekyb3d8bbwe", "LocalState", "settings.json"),
            Path.Combine(localAppData,
                "Microsoft", "Windows Terminal", "settings.json")
        };

        var packages = Path.Combine(localAppData, "Packages");
        if (Directory.Exists(packages))
        {
            foreach (var package in Directory.EnumerateDirectories(packages, "Microsoft.WindowsTerminal*"))
            {
                var localState = Path.Combine(package, "LocalState");
                Directory.CreateDirectory(localState);
                var candidate = Path.Combine(localState, "settings.json");
                if (!candidates.Contains(candidate, StringComparer.OrdinalIgnoreCase)) candidates.Add(candidate);
            }
        }

        var path = candidates.FirstOrDefault(File.Exists)
                   ?? candidates.FirstOrDefault(candidate => Directory.Exists(Path.GetDirectoryName(candidate)!));
        if (path is null)
            throw new InvalidOperationException("Windows Terminal is not installed or its settings directory is unavailable.");

        var original = File.Exists(path)
            ? File.ReadAllText(path)
            : "{\n    \"profiles\": { \"defaults\": {}, \"list\": [] }\n}\n";
        var updated = SetPowerShell7DefaultInSettingsJson(original);
        File.WriteAllText(path, updated, new System.Text.UTF8Encoding(false));
        _log.Info($"Set Windows Terminal's default profile to PowerShell 7 ({powerShell7}).");
    }

    public static string SetPowerShell7DefaultInSettingsJson(string original)
    {
        _ = JsonNode.Parse(original, documentOptions: new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        }) ?? throw new InvalidDataException("Windows Terminal settings are empty.");

        var replacement = $"\"defaultProfile\": \"{PowerShell7ProfileGuid}\"";
        const string pattern = "\\\"defaultProfile\\\"\\s*:\\s*\\\"[^\\\"]*\\\"";
        if (Regex.IsMatch(original, pattern))
            return new Regex(pattern).Replace(original, replacement, 1);

        var brace = original.IndexOf('{');
        if (brace < 0) throw new InvalidDataException("Windows Terminal settings do not contain a JSON object.");
        return original.Insert(brace + 1, Environment.NewLine + "    " + replacement + ",");
    }

    public static string[] PowerShellValidationArguments() =>
        ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", "exit 0"];

    private static string? LocatePowerShell7()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell");
        var standard = Path.Combine(root, "7", "pwsh.exe");
        if (File.Exists(standard)) return standard;
        if (Directory.Exists(root))
        {
            try
            {
                var installed = Directory.EnumerateDirectories(root)
                    .Select(directory => Path.Combine(directory, "pwsh.exe"))
                    .Where(File.Exists)
                    .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (installed is not null) return installed;
            }
            catch (UnauthorizedAccessException) { }
        }

        var windowsApps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps");
        var aliases = new[]
        {
            Path.Combine(windowsApps, "pwsh.exe"),
            Path.Combine(windowsApps, "Microsoft.PowerShell_8wekyb3d8bbwe", "pwsh.exe")
        };
        return aliases.FirstOrDefault(File.Exists);
    }

    private void ApplyLeanEdgePolicies()
    {
        const string edge = @"SOFTWARE\Policies\Microsoft\Edge";
        SetDwordIfAbsent(Registry.LocalMachine, edge, "StartupBoostEnabled", 0);
        SetDwordIfAbsent(Registry.LocalMachine, edge, "BackgroundModeEnabled", 0);
        SetDwordIfAbsent(Registry.LocalMachine, edge, "EdgeShoppingAssistantEnabled", 0);
        SetDwordIfAbsent(Registry.LocalMachine, edge, "ShowMicrosoftRewards", 0);
        SetDwordIfAbsent(Registry.LocalMachine, edge, "EdgeCollectionsEnabled", 0);
        SetDwordIfAbsent(Registry.LocalMachine, edge, "HubsSidebarEnabled", 0);
        SetDwordIfAbsent(Registry.LocalMachine, edge, "EdgeSidebarCustomizeEnabled", 0);
        SetDwordIfAbsent(Registry.LocalMachine, edge, "SearchbarAllowed", 0);
        SetDwordIfAbsent(Registry.LocalMachine, edge, "PersonalizationReportingEnabled", 0);
        SetDwordIfAbsent(Registry.LocalMachine, edge, "UserFeedbackAllowed", 0);
        SetDwordIfAbsent(Registry.LocalMachine, edge, "DiagnosticData", 1); // Required only; optional data is off.
        SetDwordIfAbsent(Registry.LocalMachine, edge, "HideFirstRunExperience", 1);
        SetDwordIfAbsent(Registry.LocalMachine, edge, "SpotlightExperiencesAndRecommendationsEnabled", 0);
        SetDwordIfAbsent(Registry.LocalMachine, edge, "ExternalProtocolDialogShowAlwaysOpenCheckbox", 1);
        SetDwordIfAbsent(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\EdgeUpdate", "CreateDesktopShortcutDefault", 0);
    }

    private void SetDword(RegistryKey hive, string path, string name, int value) => SetValue(hive, path, name, value, RegistryValueKind.DWord);
    private void SetString(RegistryKey hive, string path, string name, string value) => SetValue(hive, path, name, value, RegistryValueKind.String);

    private void SetDwordIfAbsent(RegistryKey hive, string path, string name, int value)
    {
        using var existing = hive.OpenSubKey(path, false);
        if (existing?.GetValue(name, null) is not null)
        {
            _log.Info($"Preserved an existing managed policy: {hive.Name}\\{path} [{name}].");
            return;
        }
        SetDword(hive, path, name, value);
    }

    private void SetValue(RegistryKey hive, string path, string name, object value, RegistryValueKind kind)
    {
        RecordOriginal(hive, path, name);
        using var key = hive.CreateSubKey(path, true) ?? throw new InvalidOperationException($"Registry key could not be opened: {hive.Name}\\{path}");
        key.SetValue(name, value, kind);
    }

    private void RecordOriginal(RegistryKey hive, string path, string name)
    {
        var recordKey = $"{hive.Name}\\{path}|{name}";
        if (_state.OriginalRegistryValues.ContainsKey(recordKey)) return;
        using var key = hive.OpenSubKey(path, false);
        var value = key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        _state.OriginalRegistryValues[recordKey] = value is null ? null : $"{key!.GetValueKind(name)}:{value}";
    }

    private void DeleteKey(RegistryKey hive, string path)
    {
        var recordKey = $"{hive.Name}\\{path}|<key>";
        if (!_state.OriginalRegistryValues.ContainsKey(recordKey))
        {
            using var existing = hive.OpenSubKey(path, false);
            _state.OriginalRegistryValues[recordKey] = existing is null ? null : "Key existed before PCSetup";
        }
        try { hive.DeleteSubKeyTree(path, false); }
        catch (UnauthorizedAccessException) { throw; }
    }

    private static void EnsureSuccess(ProcessResult result)
    {
        if (!result.Success) throw new InvalidOperationException($"Windows command failed with exit code {result.ExitCode}: {result.StandardError}");
    }
}
