using System.Text.Json.Serialization;

namespace PCSetup;

public enum InstallContext
{
    Machine,
    User
}

public enum SetupPhase
{
    NotStarted,
    Prepare,
    EnsureUacForUserWork,
    EnsureWinget,
    InstallUserApplications,
    DisableUac,
    InstallMachineApplications,
    ApplyTweaks,
    EnableWindowsFeatures,
    Verify,
    Complete,
    Failed,
    Cancelled,
    UninstallApplications
}

public enum ResultKind
{
    Planned,
    Succeeded,
    Skipped,
    Failed,
    RebootRequired,
    NeedsVerification
}

public sealed record VendorFallback(
    string Url,
    string FileName,
    string SilentArguments,
    string[] AllowedSignerFragments,
    string? ExpectedSha256 = null,
    bool AllowPinnedHashWithoutSignature = false,
    bool PortableZip = false,
    string? PortableExecutable = null,
    string? InstallDirectoryName = null,
    string? MsiArchiveEntry = null);

public sealed record AppDefinition(
    string Key,
    string Name,
    string Category,
    string WingetId,
    InstallContext Context,
    bool DefaultSelection,
    string Description,
    VendorFallback? Fallback = null,
    string? DetectionName = null,
    string[]? DetectionPaths = null,
    bool UpgradeIfInstalled = false,
    string? WingetSource = null,
    bool RequireKnownInstallPath = false,
    string[]? ValidationArguments = null);

public sealed record TweakDefinition(
    string Key,
    string Name,
    string Category,
    bool DefaultSelection,
    string Description,
    bool RequiresExplorerRestart = false);

public sealed record FeatureDefinition(
    string Key,
    string Name,
    string FeatureName,
    bool DefaultSelection,
    string Description);

public sealed class SetupProfile
{
    public string Name { get; set; } = "Default selection";
    public HashSet<string> Applications { get; set; } = [];
    public HashSet<string> Tweaks { get; set; } = [];
    public HashSet<string> Features { get; set; } = [];
    public HashSet<string> UninstallApplications { get; set; } = [];
    public bool BlockRazerDomains { get; set; }
    public bool ApplicationsOnly { get; set; }

    public static SetupProfile DefaultSelection(System.Runtime.InteropServices.Architecture? architecture = null)
    {
        var profile = new SetupProfile
    {
        Name = "Default selection",
        Applications = Catalog.Applications.Where(x => x.DefaultSelection).Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase),
        Tweaks = Catalog.Tweaks.Where(x => x.DefaultSelection).Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase),
        Features = Catalog.Features.Where(x => x.DefaultSelection).Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase),
        BlockRazerDomains = false,
        };
        Services.ArchitecturePolicy.ApplyDefaults(profile, architecture);
        return profile;
    }
}

public sealed class StepRecord
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public int Attempt { get; set; }
    public ResultKind Result { get; set; }
    public int? ExitCode { get; set; }
    public string Message { get; set; } = "";
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset FinishedUtc { get; set; }
}

public sealed class SetupState
{
    public string? TargetArchitecture { get; set; }
    public string? SetupVersion { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public Guid RunId { get; set; } = Guid.NewGuid();
    public string InteractiveUser { get; set; } = "";
    public SetupProfile Profile { get; set; } = SetupProfile.DefaultSelection();
    public SetupPhase Phase { get; set; } = SetupPhase.NotStarted;
    public SetupPhase? ResumePhase { get; set; }
    public int RebootCount { get; set; }
    public int MaximumReboots { get; set; } = 5;
    public bool UacWasInitiallyEnabled { get; set; }
    public bool UacTemporarilyEnabled { get; set; }
    public bool RestorePointAttempted { get; set; }
    public string StagedExecutable { get; set; } = "";
    public string StatePath { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<StepRecord> Steps { get; set; } = [];
    public Dictionary<string, ResultKind> ApplicationVerification { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, ResultKind> UninstallVerification { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> InstallerArchitectures { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string?> OriginalRegistryValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? LastError { get; set; }

    [JsonIgnore]
    public bool IsTerminal => Phase is SetupPhase.Complete or SetupPhase.Cancelled;
}

public sealed record BitLockerVolumeStatus(
    string Drive,
    bool Safe,
    string ConversionStatus,
    int EncryptionPercentage,
    string ProtectionStatus,
    string Details,
    bool IsLocked = false);

public sealed class BitLockerGateResult
{
    public List<BitLockerVolumeStatus> Volumes { get; } = [];
    public bool IsSafe => Volumes.Count > 0 && Volumes.All(x => x.Safe);
    public string Summary => IsSafe
        ? "BitLocker is fully decrypted and protection is off on every mounted internal drive."
        : string.Join(Environment.NewLine, Volumes.Where(x => !x.Safe).Select(x => $"{x.Drive}: {x.Details}"));
}

public sealed record ProgressUpdate(
    string Stage,
    string Item,
    ResultKind Result,
    string Message,
    int Completed,
    int Total);

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Success => ExitCode is 0 or 1641 or 3010;
    public bool RebootRequired => ExitCode is 1641 or 3010;
}

public enum EngineOutcome
{
    RebootScheduled,
    Complete,
    Failed,
    Cancelled,
    CompleteWithIssues
}
