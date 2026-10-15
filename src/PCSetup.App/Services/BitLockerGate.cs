using System.Collections;
using System.Globalization;

namespace PCSetup.Services;

public sealed class BitLockerGate
{
    private readonly RunLogger _log;
    private readonly ProcessRunner _process;

    public BitLockerGate(RunLogger log, ProcessRunner process)
    {
        _log = log;
        _process = process;
    }

    public BitLockerGateResult CheckAllInternalFixedDrives()
    {
        _log.Info("Checking BitLocker on every mounted internal fixed drive.");
        var fixedDrives = DriveInfo.GetDrives()
            .Where(x => x.DriveType == DriveType.Fixed && x.IsReady)
            .Select(x => x.Name.TrimEnd('\\').ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var result = new BitLockerGateResult();
        if (fixedDrives.Length == 0)
        {
            result.Volumes.Add(new BitLockerVolumeStatus("(none)", false, "Unknown", -1, "Unknown",
                "No mounted internal fixed drive could be examined."));
            return result;
        }

        Dictionary<string, BitLockerVolumeStatus> wmiResults;
        try
        {
            wmiResults = QueryWmi();
        }
        catch (Exception exception)
        {
            _log.Warn($"The BitLocker WMI provider could not be queried: {exception.Message}");
            wmiResults = new Dictionary<string, BitLockerVolumeStatus>(StringComparer.OrdinalIgnoreCase);
        }

        foreach (var drive in fixedDrives)
        {
            if (wmiResults.TryGetValue(drive, out var status))
                result.Volumes.Add(status);
            else
                result.Volumes.Add(QueryManageBde(drive));
        }

        foreach (var volume in result.Volumes)
            _log.Write(volume.Safe ? "INFO" : "ERROR", $"BitLocker {volume.Drive}: {volume.Details}");
        return result;
    }

    private static Dictionary<string, BitLockerVolumeStatus> QueryWmi()
    {
        var statuses = new Dictionary<string, BitLockerVolumeStatus>(StringComparer.OrdinalIgnoreCase);
        var locatorType = Type.GetTypeFromProgID("WbemScripting.SWbemLocator")
            ?? throw new InvalidOperationException("Windows WMI automation is unavailable.");
        dynamic locator = Activator.CreateInstance(locatorType)!;
        dynamic services = locator.ConnectServer(".", @"root\CIMV2\Security\MicrosoftVolumeEncryption");
        IEnumerable volumes = services.ExecQuery("SELECT DriveLetter FROM Win32_EncryptableVolume");

        foreach (dynamic volume in volumes)
        {
            string drive = Convert.ToString(volume.DriveLetter, CultureInfo.InvariantCulture) ?? "";
            if (string.IsNullOrWhiteSpace(drive)) continue;
            drive = drive.ToUpperInvariant();

            dynamic conversionOutput = volume.ExecMethod_("GetConversionStatus");
            var conversion = Convert.ToInt32(conversionOutput.Properties_.Item("ConversionStatus").Value, CultureInfo.InvariantCulture);
            var percentage = Convert.ToInt32(conversionOutput.Properties_.Item("EncryptionPercentage").Value, CultureInfo.InvariantCulture);
            dynamic protectionOutput = volume.ExecMethod_("GetProtectionStatus");
            var protection = Convert.ToInt32(protectionOutput.Properties_.Item("ProtectionStatus").Value, CultureInfo.InvariantCulture);
            dynamic lockOutput = volume.ExecMethod_("GetLockStatus");
            var lockStatus = Convert.ToInt32(lockOutput.Properties_.Item("LockStatus").Value, CultureInfo.InvariantCulture);

            var safe = conversion == 0 && percentage == 0 && protection == 0 && lockStatus == 0;
            var conversionName = conversion switch
            {
                0 => "Fully decrypted",
                1 => "Fully encrypted",
                2 => "Encrypting",
                3 => "Decrypting",
                4 => "Encryption paused",
                5 => "Decryption paused",
                _ => $"Unknown ({conversion})"
            };
            var protectionName = protection == 0 ? "Protection off" : protection == 1 ? "Protection on" : $"Unknown ({protection})";
            var lockName = lockStatus == 0 ? "Unlocked" : lockStatus == 1 ? "Locked" : $"Unknown ({lockStatus})";
            statuses[drive] = new BitLockerVolumeStatus(drive, safe, conversionName, percentage, protectionName,
                $"{conversionName}; {percentage}% encrypted; {protectionName}; {lockName}.", lockStatus == 1);
        }
        return statuses;
    }

    private BitLockerVolumeStatus QueryManageBde(string drive)
    {
        try
        {
            var response = _process.Run(Path.Combine(Environment.SystemDirectory, "manage-bde.exe"), ["-status", drive],
                TimeSpan.FromSeconds(30), false);
            var text = response.StandardOutput + Environment.NewLine + response.StandardError;
            var fullyDecrypted = text.Contains("Fully Decrypted", StringComparison.OrdinalIgnoreCase);
            var zeroPercent = text.Contains("Percentage Encrypted: 0", StringComparison.OrdinalIgnoreCase)
                              || text.Contains("Percentage Encrypted:    0", StringComparison.OrdinalIgnoreCase);
            var protectionOff = text.Contains("Protection Off", StringComparison.OrdinalIgnoreCase);
            var safe = response.ExitCode == 0 && fullyDecrypted && zeroPercent && protectionOff;
            return new BitLockerVolumeStatus(drive, safe,
                fullyDecrypted ? "Fully decrypted" : "Unknown", zeroPercent ? 0 : -1,
                protectionOff ? "Protection off" : "Unknown",
                safe ? "Fully decrypted; 0% encrypted; protection off."
                    : "Windows could not prove that this drive is fully decrypted with protection off. The setup is blocked.");
        }
        catch (Exception exception)
        {
            return new BitLockerVolumeStatus(drive, false, "Unknown", -1, "Unknown",
                $"BitLocker status could not be determined ({exception.Message}). The setup is blocked.");
        }
    }
}
