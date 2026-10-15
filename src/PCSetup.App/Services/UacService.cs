using Microsoft.Win32;

namespace PCSetup.Services;

public sealed class UacService
{
    private const string PolicyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
    private readonly RunLogger _log;

    public UacService(RunLogger log) => _log = log;

    public bool IsEnabled()
    {
        using var key = Registry.LocalMachine.OpenSubKey(PolicyPath, false)
            ?? throw new InvalidOperationException("The Windows UAC policy registry key is missing.");
        return Convert.ToInt32(key.GetValue("EnableLUA", 1)) != 0;
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.LocalMachine.OpenSubKey(PolicyPath, true)
            ?? throw new InvalidOperationException("The Windows UAC policy registry key could not be opened for writing.");
        key.SetValue("EnableLUA", enabled ? 1 : 0, RegistryValueKind.DWord);
        _log.Warn(enabled
            ? "UAC has been temporarily enabled. A reboot is required before user-context work."
            : "UAC has been disabled permanently as required by this setup profile. A reboot is required.");
    }
}
