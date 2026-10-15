using System.Text.Json;
using System.Text.Json.Serialization;

namespace PCSetup.Services;

public static class StateStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void Save(SetupState state)
    {
        if (string.IsNullOrWhiteSpace(state.StatePath))
            throw new InvalidOperationException("The run does not have a state path.");

        state.UpdatedUtc = DateTimeOffset.UtcNow;
        Directory.CreateDirectory(Path.GetDirectoryName(state.StatePath)!);
        var temporary = state.StatePath + ".new";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state, Options), new System.Text.UTF8Encoding(false));
        File.Move(temporary, state.StatePath, true);
    }

    public static SetupState Load(string path)
    {
        var state = JsonSerializer.Deserialize<SetupState>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException("The setup state file is empty or invalid.");
        state.StatePath = path;
        return state;
    }

    public static void ExportProfile(SetupProfile profile, string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(profile, Options), new System.Text.UTF8Encoding(false));

    public static SetupProfile ImportProfile(string path) =>
        JsonSerializer.Deserialize<SetupProfile>(File.ReadAllText(path), Options)
        ?? throw new InvalidDataException("The profile is empty or invalid.");

    public static SetupProfile ImportApplicationRetry(string path)
    {
        var json = File.ReadAllText(path);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Select a PSeeSetup run state or application profile JSON file.");
        var properties = document.RootElement.EnumerateObject().ToArray();
        SetupProfile imported;
        if (properties.Any(x => x.Name.Equals("Profile", StringComparison.OrdinalIgnoreCase)))
        {
            if (!properties.Any(x => x.Name.Equals("Steps", StringComparison.OrdinalIgnoreCase) && x.Value.ValueKind == JsonValueKind.Array))
                throw new InvalidDataException("The run state is missing its step results.");
            var previous = JsonSerializer.Deserialize<SetupState>(json, Options)
                ?? throw new InvalidDataException("The run state is invalid.");
            if (previous.TargetArchitecture is not null && !previous.TargetArchitecture.Equals(ArchitecturePolicy.NativeArchitecture, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The previous run used a different Windows architecture.");
            imported = RunResults.RetryProfile(previous);
        }
        else
        {
            if (!properties.Any(x => x.Name.Equals("Applications", StringComparison.OrdinalIgnoreCase) && x.Value.ValueKind == JsonValueKind.Array))
                throw new InvalidDataException("The profile is missing its application selections.");
            imported = JsonSerializer.Deserialize<SetupProfile>(json, Options)
                ?? throw new InvalidDataException("The application profile is invalid.");
        }
        if (imported.Applications is null || imported.Applications.Count == 0)
            throw new InvalidDataException("The selected file has no applications to retry.");
        var catalog = Catalog.Applications.ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);
        if (imported.Applications.Any(x => x is null || !catalog.ContainsKey(x)))
            throw new InvalidDataException("The file contains unknown application selections.");
        // Create fresh application selections; never resume imported state or replay its tweaks.
        return new SetupProfile
        {
            Name = "Retry applications",
            ApplicationsOnly = true,
            Applications = imported.Applications.Select(x => catalog[x].Key).ToHashSet(StringComparer.OrdinalIgnoreCase)
        };
    }
}
