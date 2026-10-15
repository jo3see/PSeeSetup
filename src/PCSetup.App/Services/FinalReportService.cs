using System.Text;

namespace PCSetup.Services;

public static class FinalReportService
{
    public static string Write(SetupState state, BitLockerGateResult bitLocker, RunLogger log)
    {
        var reportPath = Path.Combine(Path.GetDirectoryName(state.StatePath)!, "Final Report.txt");
        var builder = new StringBuilder();
        builder.AppendLine("PSEESETUP - FINAL REPORT");
        builder.AppendLine(new string('=', 56));
        builder.AppendLine($"Run ID: {state.RunId}");
        builder.AppendLine($"Started: {state.CreatedUtc.LocalDateTime}");
        builder.AppendLine($"Finished: {DateTimeOffset.Now.LocalDateTime}");
        builder.AppendLine($"Reboots: {state.RebootCount}");
        builder.AppendLine($"Interactive user: {state.InteractiveUser}");
        builder.AppendLine($"Windows architecture: {ArchitecturePolicy.OSArchitecture}; process: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
        builder.AppendLine("APPLICATION COMPATIBILITY");
        foreach (var app in Catalog.Applications.Where(x => state.Profile.Applications.Contains(x.Key)))
            builder.AppendLine($"{app.Name}: selected installer={state.InstallerArchitectures.GetValueOrDefault(app.Key, "Not recorded")}; policy={ArchitecturePolicy.For(app).Label} — {ArchitecturePolicy.For(app).Reason}");
        builder.AppendLine($"BitLocker gate: {(bitLocker.IsSafe ? "PASSED" : "FAILED")}");
        builder.AppendLine("SELECTED APP REMOVALS");
        foreach (var removal in UninstallCatalog.Applications.Where(x => state.Profile.UninstallApplications.Contains(x.Key)))
            builder.AppendLine($"{removal.Name}: result={state.UninstallVerification.GetValueOrDefault(removal.Key, ResultKind.NeedsVerification)}");
        builder.AppendLine("Store removals affect the signed-in user. Desktop uninstallers and Recall may affect the whole PC.");
        builder.AppendLine("UAC final requirement: disabled permanently");
        builder.AppendLine();
        builder.AppendLine("ATTEMPT HISTORY (retries are separate entries)");
        builder.AppendLine(new string('-', 56));
        foreach (var step in state.Steps.OrderBy(x => x.StartedUtc))
            builder.AppendLine($"[{step.Result}] {step.Name} (attempt {step.Attempt}{(step.ExitCode is null ? "" : $", exit {step.ExitCode}")}) - {step.Message}");
        builder.AppendLine();
        var final = RunResults.Final(state);
        builder.AppendLine("FINAL RESULTS (one result per component; application detection overrides attempts)");
        foreach (var (key, result) in final) builder.AppendLine($"[{result}] {key}");
        builder.AppendLine($"Succeeded: {final.Values.Count(x => x is ResultKind.Succeeded or ResultKind.RebootRequired)}");
        builder.AppendLine($"Skipped: {final.Values.Count(x => x == ResultKind.Skipped)}");
        builder.AppendLine($"Failed: {final.Values.Count(x => x == ResultKind.Failed)}");
        builder.AppendLine($"Needs verification: {final.Values.Count(x => x == ResultKind.NeedsVerification)}");
        builder.AppendLine();
        builder.AppendLine("No Adobe domains were blocked. No location, MPO, Defender, Firewall, or Windows Update settings were changed.");
        File.WriteAllText(reportPath, builder.ToString(), new System.Text.UTF8Encoding(false));
        log.Info($"Wrote final report to {reportPath}.");
        return reportPath;
    }
}
