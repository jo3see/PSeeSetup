using PCSetup;
using PCSetup.Services;
using PCSetup.UI;

public static class RetryPickerTests
{
    public static IEnumerable<(string, Action)> All() => new (string, Action)[]
    {
        ("Legacy simulation flags cannot restore simulation in version 1.2.6", () => WithFile("Legacy selections.json", path =>
        {
            File.WriteAllText(path, "{\"Applications\":[\"tor\"],\"DryRun\":true}");
            var profile = StateStore.ImportApplicationRetry(path);
            Check(profile.Applications.SetEquals(["tor"]) && profile.ApplicationsOnly);
            Check(typeof(SetupProfile).GetProperty("DryRun") is null);
            Check(!Enum.GetNames<EngineOutcome>().Contains("SimulationComplete"));
        })),
        ("Retry picker displays every JSON filename", () =>
        {
            using var picker = MainForm.CreateRetryFileDialog();
            Check(picker.Filter.Split('|')[1] == "*.json");
            Check(picker.CheckFileExists && picker.DefaultExt == "json");
        }),
        ("Supplied retry profile imports without requiring the name state.json", () => WithFile("Retry failed apps.json", path =>
        {
            StateStore.ExportProfile(new SetupProfile { Applications = ["tor"] }, path);
            var before = File.ReadAllText(path);
            var result = StateStore.ImportApplicationRetry(path);
            Check(result.Applications.SetEquals(["tor"]) && result.ApplicationsOnly);
            Check(File.ReadAllText(path) == before);
        })),
        ("Renamed run state imports only latest failed applications", () => WithFile("Previous run.json", path =>
        {
            var state = new SetupState
            {
                StatePath = path,
                TargetArchitecture = ArchitecturePolicy.NativeArchitecture,
                Profile = new SetupProfile { Applications = ["tor", "steam"], Tweaks = ["telemetry"], BlockRazerDomains = true },
                Steps = [new() { Key = "app:tor", Result = ResultKind.Failed },
                    new() { Key = "app:steam", Result = ResultKind.Failed },
                    new() { Key = "app:steam", Result = ResultKind.Succeeded }]
            };
            WriteRunFixture(state, path);
            var before = File.ReadAllText(path);
            var result = StateStore.ImportApplicationRetry(path);
            Check(result.Applications.SetEquals(["tor"]) && result.ApplicationsOnly);
            Check(result.Tweaks.Count == 0 && result.Features.Count == 0 && !result.BlockRazerDomains);
            Check(File.ReadAllText(path) == before);
        })),
        ("Retry imports strip system changes and normalize application keys", () => WithFile("Custom selection.json", path =>
        {
            StateStore.ExportProfile(new SetupProfile { Applications = ["TOR"], Tweaks = ["telemetry"],
                Features = ["netfx3"], BlockRazerDomains = true, UninstallApplications = ["paint"] }, path);
            var result = StateStore.ImportApplicationRetry(path);
            Check(result.Applications.Contains("tor") && result.ApplicationsOnly);
            Check(result.Tweaks.Count == 0 && result.Features.Count == 0 && !result.BlockRazerDomains && result.UninstallApplications.Count == 0);
        })),
        ("Unrelated JSON and mismatched architecture cannot become retry selections", () => WithFile("Invalid.json", path =>
        {
            File.WriteAllText(path, "{\"SomethingElse\":true}");
            Reject(() => StateStore.ImportApplicationRetry(path));
            StateStore.ExportProfile(new SetupProfile { Applications = ["unknown-app"] }, path);
            Reject(() => StateStore.ImportApplicationRetry(path));
            var state = new SetupState { StatePath = path, TargetArchitecture = "different-architecture" };
            WriteRunFixture(state, path);
            Reject(() => StateStore.ImportApplicationRetry(path));
        }))
    };

    private static void WithFile(string name, Action<string> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), "PCSetup-RetryTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        try { test(path); }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".new")) File.Delete(path + ".new");
            Directory.Delete(directory);
        }
    }
    private static void WriteRunFixture(SetupState state, string path) => File.WriteAllText(path,
        System.Text.Json.JsonSerializer.Serialize(state, new System.Text.Json.JsonSerializerOptions
        { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } }));
    private static void Check(bool value) { if (!value) throw new Exception("Retry picker regression failed"); }
    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new Exception("Invalid retry JSON was accepted");
    }
}
