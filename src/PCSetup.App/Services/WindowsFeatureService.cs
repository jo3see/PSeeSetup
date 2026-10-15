namespace PCSetup.Services;

public sealed class WindowsFeatureService
{
    private readonly ProcessRunner _process;
    private readonly RunLogger _log;

    public WindowsFeatureService(ProcessRunner process, RunLogger log)
    {
        _process = process;
        _log = log;
    }

    public async Task<ResultKind> EnableAsync(FeatureDefinition feature, CancellationToken cancellationToken)
    {
        if (feature.Key == "netfx3" && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 28000))
        {
            _log.Warn("This Windows version requires the standalone .NET Framework 3.5 installer. NetFx3 was not enabled; install the Microsoft standalone package separately if needed.");
            return ResultKind.NeedsVerification;
        }
        var info = await _process.RunAsync("dism.exe",
            ["/English", "/Online", "/Get-FeatureInfo", $"/FeatureName:{feature.FeatureName}"],
            TimeSpan.FromMinutes(5), cancellationToken, logOutput: false);
        if (!info.Success || info.StandardOutput.Contains("Feature name", StringComparison.OrdinalIgnoreCase) == false)
        {
            _log.Warn($"{feature.Name} is not available on this Windows edition; it was skipped.");
            return ResultKind.Skipped;
        }
        if (info.StandardOutput.Contains("State : Enabled", StringComparison.OrdinalIgnoreCase))
        {
            _log.Info($"{feature.Name} is already enabled.");
            return ResultKind.Skipped;
        }

        var enable = await _process.RunAsync("dism.exe",
            ["/English", "/Online", "/Enable-Feature", $"/FeatureName:{feature.FeatureName}", "/All", "/NoRestart"],
            TimeSpan.FromMinutes(45), cancellationToken);
        if (!enable.Success)
        {
            _log.Warn($"{feature.Name} could not be enabled on this edition or hardware (exit {enable.ExitCode}).");
            return ResultKind.Failed;
        }
        return enable.RebootRequired || enable.StandardOutput.Contains("restart", StringComparison.OrdinalIgnoreCase)
            ? ResultKind.RebootRequired
            : ResultKind.Succeeded;
    }
}
