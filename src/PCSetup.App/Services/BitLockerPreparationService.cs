namespace PCSetup.Services;

public sealed class BitLockerPreparationService
{
    public const string ResumeArgument = "--bitlocker-resume";
    public const string PreparationTaskName = "PCSetup-BitLockerPreparation";
    private const int TaskCreateOrUpdate = 6;
    private const int TaskLogonInteractiveToken = 3;
    private const int TaskTriggerLogon = 9;
    private const int TaskActionExecute = 0;
    private const int TaskRunLevelHighest = 1;
    private readonly RunLogger _log;
    private readonly ProcessRunner _process;
    private readonly BitLockerGate _gate;

    public BitLockerPreparationService(RunLogger log)
    {
        _log = log;
        _process = new ProcessRunner(log);
        _gate = new BitLockerGate(log, _process);
    }

    public static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PCSetup");

    public static string StagedExecutable => Path.Combine(Root, "bin", "PSeeSetup.exe");

    public static string LogPath => Path.Combine(Root, "BitLocker Preparation.log");

    public static string PendingMarkerPath => Path.Combine(Root, "BitLocker Preparation.pending");

    public static bool PreparationPending => File.Exists(PendingMarkerPath);

    public bool IsRunningFromStagedExecutable()
    {
        var current = Path.GetFullPath(Environment.ProcessPath ?? "");
        return current.Equals(Path.GetFullPath(StagedExecutable), StringComparison.OrdinalIgnoreCase);
    }

    public void StageAndRegister(string interactiveUser)
    {
        var source = Environment.ProcessPath ?? throw new InvalidOperationException("The running application path is unavailable.");
        if (Path.GetFileName(source).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("BitLocker preparation requires the published PCSetup executable.");

        Directory.CreateDirectory(Path.GetDirectoryName(StagedExecutable)!);
        if (!Path.GetFullPath(source).Equals(Path.GetFullPath(StagedExecutable), StringComparison.OrdinalIgnoreCase))
            File.Copy(source, StagedExecutable, true);
        RegisterTask(interactiveUser);
        File.WriteAllText(PendingMarkerPath,
            $"BitLocker preparation requested {DateTimeOffset.Now:O}{Environment.NewLine}");
        _log.Info($"Staged PCSetup and registered {PreparationTaskName} for {interactiveUser}.");
    }

    public async Task StartOrContinueDecryptionAsync(BitLockerGateResult current,
        CancellationToken cancellationToken)
    {
        foreach (var volume in current.Volumes.Where(x => !x.Safe))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (volume.Drive == "(none)")
                throw new InvalidOperationException(volume.Details);
            if (IsExplicitlyLocked(volume))
                throw new InvalidOperationException($"{volume.Drive} is locked and cannot be decrypted until it is unlocked.");
            if (volume.ConversionStatus.Contains("Decrypting", StringComparison.OrdinalIgnoreCase))
            {
                _log.Info($"{volume.Drive} is already decrypting.");
                continue;
            }

            _log.Warn($"Starting full BitLocker decryption on {volume.Drive} at the user's request.");
            var result = await _process.RunAsync(Path.Combine(Environment.SystemDirectory, "manage-bde.exe"),
                ["-off", volume.Drive], TimeSpan.FromMinutes(2), cancellationToken).ConfigureAwait(false);
            if (!result.Success)
                throw new InvalidOperationException($"Windows could not start BitLocker decryption on {volume.Drive} (exit {result.ExitCode}). {result.StandardError}");
        }
    }

    public BitLockerGateResult Check() => _gate.CheckAllInternalFixedDrives();

    public static bool IsExplicitlyLocked(BitLockerVolumeStatus volume) => volume.IsLocked;

    public async Task ScheduleRestartAsync(CancellationToken cancellationToken)
    {
        _log.Info("BitLocker decryption is complete. Scheduling the required restart.");
        var result = await _process.RunAsync("shutdown.exe",
            ["/r", "/t", "20", "/d", "p:2:4", "/c",
                "PCSetup: BitLocker is fully decrypted. PSeeSetup will reopen after sign-in."],
            TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false);
        if (!result.Success) throw new InvalidOperationException("Windows did not accept the restart request.");
    }

    public void RemoveResumeTask()
    {
        try
        {
            dynamic service = CreateTaskService();
            service.Connect();
            dynamic root = service.GetFolder("\\");
            root.DeleteTask(PreparationTaskName, 0);
            _log.Info($"Removed {PreparationTaskName}.");
        }
        catch (Exception exception)
        {
            _log.Info($"BitLocker preparation task cleanup was not needed: {exception.Message}");
        }

        try
        {
            if (File.Exists(PendingMarkerPath)) File.Delete(PendingMarkerPath);
        }
        catch (Exception exception)
        {
            _log.Warn($"Could not remove the BitLocker preparation marker: {exception.Message}");
        }
    }

    private void RegisterTask(string interactiveUser)
    {
        dynamic service = CreateTaskService();
        service.Connect();
        dynamic root = service.GetFolder("\\");
        dynamic definition = service.NewTask(0);
        definition.RegistrationInfo.Description = "Reopens PSeeSetup after BitLocker decryption and restart.";
        definition.Settings.Enabled = true;
        definition.Settings.StartWhenAvailable = true;
        definition.Settings.DisallowStartIfOnBatteries = false;
        definition.Settings.StopIfGoingOnBatteries = false;
        definition.Settings.AllowHardTerminate = true;
        definition.Settings.MultipleInstances = 2;
        definition.Settings.ExecutionTimeLimit = "PT24H";

        dynamic principal = definition.Principal;
        principal.UserId = interactiveUser;
        principal.LogonType = TaskLogonInteractiveToken;
        principal.RunLevel = TaskRunLevelHighest;

        dynamic trigger = definition.Triggers.Create(TaskTriggerLogon);
        trigger.UserId = interactiveUser;
        trigger.Enabled = true;

        dynamic action = definition.Actions.Create(TaskActionExecute);
        action.Path = StagedExecutable;
        action.Arguments = ResumeArgument;
        action.WorkingDirectory = Path.GetDirectoryName(StagedExecutable);

        root.RegisterTaskDefinition(PreparationTaskName, definition, TaskCreateOrUpdate, interactiveUser, null,
            TaskLogonInteractiveToken, null);
    }

    private static dynamic CreateTaskService()
    {
        var type = Type.GetTypeFromProgID("Schedule.Service")
            ?? throw new InvalidOperationException("Windows Task Scheduler automation is unavailable.");
        return Activator.CreateInstance(type)!;
    }
}
