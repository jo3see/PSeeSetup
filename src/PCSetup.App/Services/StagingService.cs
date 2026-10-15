namespace PCSetup.Services;

public sealed class StagingService
{
    private readonly RunLogger _log;

    public StagingService(RunLogger log) => _log = log;

    public string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PCSetup");

    public void Stage(SetupState state)
    {
        var source = Environment.ProcessPath ?? throw new InvalidOperationException("The running application path is unavailable.");
        if (Path.GetFileName(source).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Live setup cannot be staged from a developer-hosted build. Publish the application first.");

        var binaryDirectory = Path.Combine(Root, "bin");
        var runDirectory = Path.Combine(Root, "Runs", state.RunId.ToString("N"));
        Directory.CreateDirectory(binaryDirectory);
        Directory.CreateDirectory(runDirectory);
        var staged = Path.Combine(binaryDirectory, "PSeeSetup.exe");
        if (!Path.GetFullPath(source).Equals(Path.GetFullPath(staged), StringComparison.OrdinalIgnoreCase))
            File.Copy(source, staged, true);

        state.StagedExecutable = staged;
        state.StatePath = Path.Combine(runDirectory, "state.json");
        _log.Info($"Staged the resumable executable at {staged}.");
    }

    public static string LogPath(Guid runId) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PCSetup", "Runs", runId.ToString("N"), "PSeeSetup.log");
}
