namespace PCSetup.Services;

public sealed class RestorePointService
{
    private readonly ProcessRunner _process;
    private readonly RunLogger _log;

    public RestorePointService(ProcessRunner process, RunLogger log)
    {
        _process = process;
        _log = log;
    }

    public async Task TryCreateAsync(Guid runId, CancellationToken cancellationToken)
    {
        var command = "$ErrorActionPreference='Stop';" +
                      "Enable-ComputerRestore -Drive ($env:SystemDrive + '\\');" +
                      $"Checkpoint-Computer -Description 'PCSetup {runId:N}' -RestorePointType MODIFY_SETTINGS";
        try
        {
            var result = await _process.RunAsync(GetWindowsPowerShell(),
                ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", command],
                TimeSpan.FromMinutes(10), cancellationToken).ConfigureAwait(false);
            if (!result.Success) _log.Warn("Windows did not create a restore point; setup will continue and record this result.");
        }
        catch (Exception exception)
        {
            _log.Warn($"Windows did not create a restore point: {exception.Message}");
        }
    }

    private static string GetWindowsPowerShell() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
}
