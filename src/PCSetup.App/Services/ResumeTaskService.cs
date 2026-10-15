namespace PCSetup.Services;

public sealed class ResumeTaskService
{
    private const int TaskCreateOrUpdate = 6;
    private const int TaskLogonInteractiveToken = 3;
    private const int TaskTriggerLogon = 9;
    private const int TaskActionExecute = 0;
    private const int TaskRunLevelHighest = 1;
    private readonly RunLogger _log;

    public ResumeTaskService(RunLogger log) => _log = log;

    public static string TaskName(Guid runId) => $"PCSetup-Resume-{runId:N}";

    public void Register(SetupState state)
    {
        if (string.IsNullOrWhiteSpace(state.StagedExecutable) || string.IsNullOrWhiteSpace(state.StatePath))
            throw new InvalidOperationException("The setup executable and state must be staged before resume is registered.");

        dynamic service = CreateService();
        service.Connect();
        dynamic root = service.GetFolder("\\");
        dynamic definition = service.NewTask(0);
        definition.RegistrationInfo.Description = "Resumes PSeeSetup after a required reboot.";
        definition.Settings.Enabled = true;
        definition.Settings.StartWhenAvailable = true;
        definition.Settings.DisallowStartIfOnBatteries = false;
        definition.Settings.StopIfGoingOnBatteries = false;
        definition.Settings.AllowHardTerminate = true;
        definition.Settings.MultipleInstances = 2; // Ignore a second launch.
        definition.Settings.ExecutionTimeLimit = "PT12H";

        dynamic principal = definition.Principal;
        principal.UserId = state.InteractiveUser;
        principal.LogonType = TaskLogonInteractiveToken;
        principal.RunLevel = TaskRunLevelHighest;

        dynamic trigger = definition.Triggers.Create(TaskTriggerLogon);
        trigger.UserId = state.InteractiveUser;
        trigger.Enabled = true;

        dynamic action = definition.Actions.Create(TaskActionExecute);
        action.Path = state.StagedExecutable;
        action.Arguments = $"--resume \"{state.StatePath}\"";
        action.WorkingDirectory = Path.GetDirectoryName(state.StagedExecutable);

        root.RegisterTaskDefinition(TaskName(state.RunId), definition, TaskCreateOrUpdate, state.InteractiveUser, null,
            TaskLogonInteractiveToken, null);
        _log.Info($"Registered reboot-resume task {TaskName(state.RunId)} for {state.InteractiveUser}.");
    }

    public void Remove(Guid runId)
    {
        try
        {
            dynamic service = CreateService();
            service.Connect();
            dynamic root = service.GetFolder("\\");
            root.DeleteTask(TaskName(runId), 0);
            _log.Info($"Removed reboot-resume task {TaskName(runId)}.");
        }
        catch (Exception exception)
        {
            _log.Warn($"Resume task cleanup was not needed or could not complete: {exception.Message}");
        }
    }

    private static dynamic CreateService()
    {
        var type = Type.GetTypeFromProgID("Schedule.Service")
            ?? throw new InvalidOperationException("Windows Task Scheduler automation is unavailable.");
        return Activator.CreateInstance(type)!;
    }
}
