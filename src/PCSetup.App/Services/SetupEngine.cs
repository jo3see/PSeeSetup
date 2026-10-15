namespace PCSetup.Services;

public sealed class SetupEngine
{
    private readonly SetupState _state;
    private readonly RunLogger _log;
    private readonly ProcessRunner _process;
    private readonly BitLockerGate _bitLocker;
    private readonly UacService _uac;
    private readonly WingetService _winget;
    private readonly ApplicationInstaller _apps;
    private readonly RestorePointService _restorePoint;
    private readonly ResumeTaskService _resume;
    private readonly StagingService _staging;
    private readonly WindowsFeatureService _features;
    private readonly HostsFileService _hosts;
    private readonly UninstallService _uninstaller;
    private int _completed;
    private int _total;

    public SetupEngine(SetupState state, RunLogger log)
    {
        _state = state;
        _log = log;
        _process = new ProcessRunner(log);
        _bitLocker = new BitLockerGate(log, _process);
        _uac = new UacService(log);
        _winget = new WingetService(_process, log);
        var downloads = new DownloadService(_process, log);
        _apps = new ApplicationInstaller(_winget, downloads, _process, log);
        _restorePoint = new RestorePointService(_process, log);
        _resume = new ResumeTaskService(log);
        _staging = new StagingService(log);
        _features = new WindowsFeatureService(_process, log);
        _hosts = new HostsFileService(_process, log);
        _uninstaller = new UninstallService(_process, log);
    }

    public async Task<EngineOutcome> RunAsync(IProgress<ProgressUpdate>? progress, CancellationToken cancellationToken)
    {
        try
        {
            ValidateProfile();
            if (_state.Phase != SetupPhase.NotStarted &&
                (_state.TargetArchitecture != ArchitecturePolicy.NativeArchitecture || _state.SetupVersion != "1.2.6"))
                throw new InvalidOperationException("This saved run belongs to a different PSeeSetup version or architecture. Start a new 1.2.6 setup; profile files can still be imported.");
            if (!SystemContext.IsSupportedWindows(out var platformReason)) throw new InvalidOperationException(platformReason);
            if (!SystemContext.IsAdministrator()) throw new InvalidOperationException("Administrator rights are required.");
            var interactiveUser = SystemContext.InteractiveIdentity();
            if (!SystemContext.SameIdentity(SystemContext.CurrentIdentity(), interactiveUser))
                throw new InvalidOperationException($"The administrator identity ({SystemContext.CurrentIdentity()}) must match the signed-in user ({interactiveUser}).");

            var bitLocker = _bitLocker.CheckAllInternalFixedDrives();
            if (!bitLocker.IsSafe)
                throw new InvalidOperationException("BITLOCKER HARD STOP: " + bitLocker.Summary);

            _total = 8 + _state.Profile.Applications.Count + _state.Profile.Tweaks.Count + _state.Profile.Features.Count + _state.Profile.UninstallApplications.Count;

            if (_state.Phase == SetupPhase.Failed && _state.ResumePhase is not null)
            {
                _state.Phase = _state.ResumePhase.Value;
                _state.LastError = null;
            }

            if (_state.Phase == SetupPhase.NotStarted)
            {
                _state.TargetArchitecture = ArchitecturePolicy.NativeArchitecture;
                _state.SetupVersion = "1.2.6";
                _state.InteractiveUser = interactiveUser;
                _state.UacWasInitiallyEnabled = _uac.IsEnabled();
                _staging.Stage(_state);
                _state.Phase = SetupPhase.Prepare;
                StateStore.Save(_state);
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                switch (_state.Phase)
                {
                    case SetupPhase.Prepare:
                        Report(progress, "Safety", "System restore point", ResultKind.Planned, "Creating a restore point before changes.");
                        if (!_state.RestorePointAttempted)
                        {
                            await _restorePoint.TryCreateAsync(_state.RunId, cancellationToken).ConfigureAwait(false);
                            _state.RestorePointAttempted = true;
                        }
                        _state.Phase = SetupPhase.EnsureUacForUserWork;
                        StateStore.Save(_state);
                        break;

                    case SetupPhase.EnsureUacForUserWork:
                        var needsUserWork = SelectedApps(InstallContext.User).Any(app => !_apps.IsInstalled(app));
                        if (!_uac.IsEnabled() && (needsUserWork || !_winget.IsUsable()))
                        {
                            _uac.SetEnabled(true);
                            _state.UacTemporarilyEnabled = true;
                            _state.Phase = SetupPhase.EnsureWinget;
                            return await ScheduleRebootAsync(progress, "Temporarily enabling UAC for user-context installs", cancellationToken).ConfigureAwait(false);
                        }
                        _state.Phase = SetupPhase.EnsureWinget;
                        StateStore.Save(_state);
                        break;

                    case SetupPhase.EnsureWinget:
                        if (!_winget.IsUsable() && !_uac.IsEnabled())
                        {
                            _uac.SetEnabled(true);
                            _state.UacTemporarilyEnabled = true;
                            return await ScheduleRebootAsync(progress, "Temporarily enabling UAC to repair WinGet", cancellationToken).ConfigureAwait(false);
                        }
                        await ExecuteMandatoryAsync("mandatory:winget", "Install or repair WinGet",
                            () => _winget.EnsureAsync(cancellationToken), progress).ConfigureAwait(false);
                        _state.Phase = SetupPhase.InstallUserApplications;
                        StateStore.Save(_state);
                        break;

                    case SetupPhase.InstallUserApplications:
                        var userApps = SelectedApps(InstallContext.User).ToArray();
                        if (userApps.Any(app => !_apps.IsInstalled(app)) && !_uac.IsEnabled())
                            throw new InvalidOperationException("User-context applications cannot be installed until the temporary UAC reboot has completed.");
                        foreach (var app in userApps)
                            await ExecuteOptionalAsync("app:" + app.Key, app.Name,
                                () => _apps.InstallAsync(app, CacheDirectory(), cancellationToken), "Applications", progress).ConfigureAwait(false);
                        _state.Phase = SetupPhase.DisableUac;
                        StateStore.Save(_state);
                        break;

                    case SetupPhase.DisableUac:
                        if (_uac.IsEnabled())
                        {
                            _uac.SetEnabled(false);
                            _state.Phase = SetupPhase.InstallMachineApplications;
                            return await ScheduleRebootAsync(progress, "Disabling UAC permanently", cancellationToken).ConfigureAwait(false);
                        }
                        _state.Phase = SetupPhase.InstallMachineApplications;
                        StateStore.Save(_state);
                        break;

                    case SetupPhase.InstallMachineApplications:
                        foreach (var app in SelectedApps(InstallContext.Machine))
                            await ExecuteOptionalAsync("app:" + app.Key, app.Name,
                                () => _apps.InstallAsync(app, CacheDirectory(), cancellationToken), "Applications", progress).ConfigureAwait(false);
                        _state.Phase = RunResults.AfterMachineApps(_state.Profile);
                        StateStore.Save(_state);
                        break;

                    case SetupPhase.ApplyTweaks:
                        var tweaks = new RegistryTweakService(_state, _process, _log);
                        foreach (var tweak in SelectedTweaks())
                            await ExecuteOptionalAsync("tweak:" + tweak.Key, tweak.Name,
                                async () => { await tweaks.ApplyAsync(tweak, cancellationToken); return ResultKind.Succeeded; },
                                "Windows tweaks", progress).ConfigureAwait(false);
                        await ExecuteOptionalAsync("tweak:razer_hosts", "Optional Razer domain block",
                            async () => { await _hosts.ApplyRazerBlockAsync(_state, cancellationToken); return _state.Profile.BlockRazerDomains ? ResultKind.Succeeded : ResultKind.Skipped; },
                            "Windows tweaks", progress).ConfigureAwait(false);
                        _state.Phase = SetupPhase.EnableWindowsFeatures;
                        StateStore.Save(_state);
                        break;

                    case SetupPhase.EnableWindowsFeatures:
                        var featureReboot = false;
                        foreach (var feature in SelectedFeatures())
                        {
                            var result = await ExecuteOptionalAsync("feature:" + feature.Key, feature.Name,
                                () => _features.EnableAsync(feature, cancellationToken), "Windows features", progress).ConfigureAwait(false);
                            featureReboot |= result == ResultKind.RebootRequired;
                        }
                        _state.Phase = SetupPhase.UninstallApplications;
                        StateStore.Save(_state);
                        if (featureReboot)
                            return await ScheduleRebootAsync(progress, "Completing Windows feature changes", cancellationToken).ConfigureAwait(false);
                        break;

                    case SetupPhase.UninstallApplications:
                        var removalReboot = false;
                        foreach (var removal in UninstallCatalog.Applications.Where(x => _state.Profile.UninstallApplications.Contains(x.Key)))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var removalResult = await ExecuteOptionalAsync("uninstall:" + removal.Key, removal.Name,
                                () => _uninstaller.UninstallAsync(removal, Path.GetDirectoryName(_state.StatePath)!, cancellationToken),
                                "Uninstall applications", progress, retry: false).ConfigureAwait(false);
                            removalReboot |= removalResult == ResultKind.RebootRequired;
                        }
                        _state.Phase = SetupPhase.Verify;
                        StateStore.Save(_state);
                        if (removalReboot)
                            return await ScheduleRebootAsync(progress, "Completing selected app removals", cancellationToken).ConfigureAwait(false);
                        break;

                    case SetupPhase.Verify:
                        if (_uac.IsEnabled()) throw new InvalidOperationException("Final verification found UAC enabled; the permanent-disable requirement was not met.");
                        var finalBitLocker = _bitLocker.CheckAllInternalFixedDrives();
                        if (!finalBitLocker.IsSafe) throw new InvalidOperationException("BitLocker was enabled during the run. Setup stopped.");
                        await VerifyApplicationsAsync(progress, cancellationToken).ConfigureAwait(false);
                        if (_state.Profile.UninstallApplications.Count > 0)
                        {
                            var removalInventory = await _uninstaller.ScanAsync(cancellationToken).ConfigureAwait(false);
                            foreach (var removal in UninstallCatalog.Applications.Where(x => _state.Profile.UninstallApplications.Contains(x.Key)))
                            {
                                var removalStatus = UninstallService.Status(removal, removalInventory);
                                var removalResult = removalStatus.Availability == RemovalAvailability.NotInstalled ? ResultKind.Succeeded : ResultKind.NeedsVerification;
                                _state.UninstallVerification[removal.Key] = removalResult;
                                Report(progress, "Removal verification", removal.Name, removalResult, removalStatus.Description);
                            }
                        }
                        FinalReportService.Write(_state, finalBitLocker, _log);
                        if (_state.Profile.Applications.Contains("equalizerapo")) _apps.OpenEqualizerConfiguratorIfPresent();
                        _state.Phase = SetupPhase.Complete;
                        StateStore.Save(_state);
                        _resume.Remove(_state.RunId);
                        TryRemoveCache();
                        var hasIssues = RunResults.HasIssues(_state);
                        Report(progress, "Complete", "Setup", hasIssues ? ResultKind.NeedsVerification : ResultKind.Succeeded,
                            hasIssues ? "Setup finished with failures or items needing verification. Review the final report." : "All selected work has finished. Review the final report.");
                        return hasIssues ? EngineOutcome.CompleteWithIssues : EngineOutcome.Complete;

                    case SetupPhase.Complete:
                        _resume.Remove(_state.RunId);
                        return RunResults.HasIssues(_state) ? EngineOutcome.CompleteWithIssues : EngineOutcome.Complete;
                    case SetupPhase.Cancelled:
                        _resume.Remove(_state.RunId);
                        return EngineOutcome.Cancelled;
                    default:
                        throw new InvalidOperationException($"The saved setup phase is not resumable: {_state.Phase}.");
                }
            }

            _state.ResumePhase = _state.Phase;
            _state.Phase = SetupPhase.Cancelled;
            StateStore.Save(_state);
            _resume.Remove(_state.RunId);
            return EngineOutcome.Cancelled;
        }
        catch (OperationCanceledException)
        {
            if (!string.IsNullOrWhiteSpace(_state.StatePath))
            {
                _state.ResumePhase = _state.Phase;
                _state.Phase = SetupPhase.Cancelled;
                StateStore.Save(_state);
                _resume.Remove(_state.RunId);
            }
            return EngineOutcome.Cancelled;
        }
        catch (Exception exception)
        {
            _log.Error(exception.ToString());
            if (!string.IsNullOrWhiteSpace(_state.StatePath))
            {
                _state.ResumePhase = _state.Phase == SetupPhase.Failed ? _state.ResumePhase : _state.Phase;
                _state.Phase = SetupPhase.Failed;
                _state.LastError = exception.Message;
                StateStore.Save(_state);
                _resume.Remove(_state.RunId);
            }
            return EngineOutcome.Failed;
        }
    }

    private async Task ExecuteMandatoryAsync(string key, string name, Func<Task> operation,
        IProgress<ProgressUpdate>? progress)
    {
        if (IsComplete(key)) { Report(progress, "Required", name, ResultKind.Skipped, "Already completed in this run."); return; }
        var attempt = _state.Steps.Count(x => x.Key == key) + 1;
        var record = StartRecord(key, name, attempt);
        Report(progress, "Required", name, ResultKind.Planned, "Starting required step.");
        try
        {
            await operation().ConfigureAwait(false);
            FinishRecord(record, ResultKind.Succeeded, "Completed.");
        }
        catch (Exception exception)
        {
            FinishRecord(record, ResultKind.Failed, exception.Message);
            throw;
        }
        finally { StateStore.Save(_state); }
        Report(progress, "Required", name, ResultKind.Succeeded, "Completed.");
    }

    private async Task<ResultKind> ExecuteOptionalAsync(string key, string name, Func<Task<ResultKind>> operation,
        string stage, IProgress<ProgressUpdate>? progress, bool retry = true)
    {
        if (IsComplete(key))
        {
            Report(progress, stage, name, ResultKind.Skipped, "Already completed in this run.");
            return ResultKind.Skipped;
        }
        var priorAttempts = _state.Steps.Count(x => x.Key == key);
        var maximumAttempts = retry ? 2 : 1;
        if (priorAttempts >= maximumAttempts)
        {
            Report(progress, stage, name, ResultKind.Skipped, "Attempt limit reached; see the report.");
            return ResultKind.Skipped;
        }

        ResultKind lastResult = ResultKind.Failed;
        for (var attempt = priorAttempts + 1; attempt <= maximumAttempts; attempt++)
        {
            var record = StartRecord(key, name, attempt);
            Report(progress, stage, name, ResultKind.Planned, attempt == 1 ? "Starting." : "Retrying once after the first failure.");
            try
            {
                lastResult = await operation().ConfigureAwait(false);
                if (key.StartsWith("app:", StringComparison.Ordinal) && _apps.LastInstallerArchitecture is { } selectedArchitecture)
                    _state.InstallerArchitectures[key[4..]] = selectedArchitecture;
                FinishRecord(record, lastResult, lastResult == ResultKind.Failed ? "The step returned an error." : "Completed.");
            }
            catch (OperationCanceledException)
            {
                FinishRecord(record, ResultKind.NeedsVerification, "Cancelled; completion was not confirmed.");
                throw;
            }
            catch (Exception exception)
            {
                lastResult = ResultKind.Failed;
                FinishRecord(record, ResultKind.Failed, exception.Message);
            }
            StateStore.Save(_state);
            Report(progress, stage, name, lastResult, record.Message);
            if (lastResult != ResultKind.Failed) return lastResult;
        }
        _log.Warn($"{name} failed after {maximumAttempts} attempt(s); remaining optional work can continue.");
        return lastResult;
    }

    private async Task VerifyApplicationsAsync(IProgress<ProgressUpdate>? progress, CancellationToken cancellationToken)
    {
        foreach (var app in SelectedApps(null))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ArchitecturePolicy.For(app).CanInstall)
            {
                _state.ApplicationVerification[app.Key] = ArchitecturePolicy.For(app).Kind == CompatibilityKind.Skipped ? ResultKind.Skipped : ResultKind.NeedsVerification;
                Report(progress, "Verification", app.Name, _state.ApplicationVerification[app.Key], ArchitecturePolicy.For(app).Reason);
                continue;
            }
            var installed = _apps.IsInstalled(app);
            var needsVerification = _state.Steps.LastOrDefault(x => x.Key == "app:" + app.Key)?.Result == ResultKind.NeedsVerification && !installed;
            if (needsVerification)
            {
                _state.ApplicationVerification[app.Key] = ResultKind.NeedsVerification;
                Report(progress, "Verification", app.Name, ResultKind.NeedsVerification, "Review the installer log; usable installation has not been confirmed.");
                continue;
            }
            _state.ApplicationVerification[app.Key] = installed ? ResultKind.Succeeded : ResultKind.Failed;
            Report(progress, "Verification", app.Name, _state.ApplicationVerification[app.Key],
                installed ? "Installation was recorded or detected." : "The application was not detected; review its installer log.");
        }
        await Task.CompletedTask;
    }

    private async Task<EngineOutcome> ScheduleRebootAsync(IProgress<ProgressUpdate>? progress, string reason,
        CancellationToken cancellationToken)
    {
        if (_state.RebootCount >= _state.MaximumReboots)
            throw new InvalidOperationException("The setup reached its five-reboot safety limit and stopped to prevent a loop.");
        cancellationToken.ThrowIfCancellationRequested();
        _state.RebootCount++;
        StateStore.Save(_state);
        _resume.Register(_state);
        Report(progress, "Reboot workflow", reason, ResultKind.RebootRequired,
            $"Restart {_state.RebootCount} of {_state.MaximumReboots} is scheduled. Setup resumes at sign-in.");
        var shutdown = await _process.RunAsync("shutdown.exe",
            ["/r", "/t", "20", "/d", "p:2:4", "/c", $"PCSetup: {reason}. The setup will resume after sign-in."],
            TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false);
        if (!shutdown.Success) throw new InvalidOperationException("Windows did not accept the restart request.");
        return EngineOutcome.RebootScheduled;
    }

    private StepRecord StartRecord(string key, string name, int attempt)
    {
        var record = new StepRecord { Key = key, Name = name, Attempt = attempt, Result = ResultKind.Planned, StartedUtc = DateTimeOffset.UtcNow };
        _state.Steps.Add(record);
        return record;
    }

    private void FinishRecord(StepRecord record, ResultKind result, string message)
    {
        record.Result = result;
        record.Message = message;
        record.FinishedUtc = DateTimeOffset.UtcNow;
    }

    private bool IsComplete(string key) => _state.Steps.Any(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase)
        && x.Result is ResultKind.Succeeded or ResultKind.Skipped or ResultKind.RebootRequired or ResultKind.NeedsVerification);

    private IEnumerable<AppDefinition> SelectedApps(InstallContext? context) => Catalog.Applications
        .Where(x => _state.Profile.Applications.Contains(x.Key) && (context is null || x.Context == context));
    private IEnumerable<TweakDefinition> SelectedTweaks() => Catalog.Tweaks.Where(x => _state.Profile.Tweaks.Contains(x.Key));
    private IEnumerable<FeatureDefinition> SelectedFeatures() => Catalog.Features.Where(x => _state.Profile.Features.Contains(x.Key));

    private string CacheDirectory() => Path.Combine(Path.GetDirectoryName(_state.StatePath)!, "Cache");

    private void Report(IProgress<ProgressUpdate>? progress, string stage, string item, ResultKind result, string message)
    {
        if (result != ResultKind.Planned) _completed++;
        progress?.Report(new ProgressUpdate(stage, item, result, message, Math.Min(_completed, _total), Math.Max(1, _total)));
        _log.Info($"{stage} | {item} | {result} | {message}");
    }

    private void TryRemoveCache()
    {
        try
        {
            var cache = CacheDirectory();
            if (Directory.Exists(cache)) Directory.Delete(cache, true);
            _log.Info("Removed downloaded installer cache.");
        }
        catch (Exception exception) { _log.Warn($"Installer cache cleanup did not complete: {exception.Message}"); }
    }

    private void ValidateProfile()
    {
        RunResults.ValidateRetryProfile(_state.Profile);
        var appKeys = Catalog.Applications.Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tweakKeys = Catalog.Tweaks.Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var featureKeys = Catalog.Features.Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removalKeys = UninstallCatalog.Applications.Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknown = _state.Profile.Applications.Where(x => !appKeys.Contains(x))
            .Concat(_state.Profile.Tweaks.Where(x => !tweakKeys.Contains(x)))
            .Concat(_state.Profile.Features.Where(x => !featureKeys.Contains(x)))
            .Concat(_state.Profile.UninstallApplications.Where(x => !removalKeys.Contains(x))).ToArray();
        if (unknown.Length > 0) throw new InvalidDataException("The profile contains unknown selections: " + string.Join(", ", unknown));
        _state.Profile.UninstallApplications = _state.Profile.UninstallApplications.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
