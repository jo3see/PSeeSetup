namespace PCSetup.Services;

public static class RunResults
{
    public static Dictionary<string, ResultKind> Final(SetupState state)
    {
        var results = state.Steps.GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Last().Result, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, result) in state.ApplicationVerification)
        {
            var appKey = "app:" + key;
            // Verification confirms presence without relabelling a skipped installer as a new installation.
            if (result == ResultKind.Succeeded && results.GetValueOrDefault(appKey) == ResultKind.Skipped) continue;
            results[appKey] = result;
        }
        foreach (var (key, result) in state.UninstallVerification)
        {
            var removalKey = "uninstall:" + key;
            if (result == ResultKind.Succeeded && results.GetValueOrDefault(removalKey) == ResultKind.Skipped) continue;
            results[removalKey] = result;
        }
        return results;
    }

    public static bool HasIssues(SetupState state) => Final(state).Values
        .Any(x => x is ResultKind.Failed or ResultKind.NeedsVerification or ResultKind.Planned);

    public static SetupProfile RetryProfile(SetupState previous)
    {
        var results = Final(previous);
        return new SetupProfile
        {
            Name = "Retry failed applications",
            ApplicationsOnly = true,
            Applications = Catalog.Applications.Where(x => previous.Profile.Applications.Contains(x.Key)
                && results.TryGetValue("app:" + x.Key, out var result)
                && result is ResultKind.Failed or ResultKind.NeedsVerification)
                .Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase)
        };
    }

    public static void ValidateRetryProfile(SetupProfile profile)
    {
        if (profile.ApplicationsOnly && (profile.Tweaks.Count != 0 || profile.Features.Count != 0 || profile.BlockRazerDomains || profile.UninstallApplications.Count != 0))
            throw new InvalidDataException("Applications-only retry cannot contain tweaks, Windows features or hosts changes.");
    }

    public static SetupPhase AfterMachineApps(SetupProfile profile) =>
        profile.ApplicationsOnly ? SetupPhase.Verify : SetupPhase.ApplyTweaks;
}
