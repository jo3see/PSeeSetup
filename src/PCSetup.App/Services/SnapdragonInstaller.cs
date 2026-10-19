using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace PCSetup.Services;

public sealed class SnapdragonInstaller(DownloadService downloads, ProcessRunner process, RunLogger log,
    Func<bool>? eligible = null, Func<bool>? runtimePresent = null,
    Func<VendorFallback, string, CancellationToken, Task<string>>? download = null,
    Func<string, string[], CancellationToken, Task<ProcessResult>>? run = null)
{
    public static readonly VendorFallback Runtime = new(
        "https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/9.0.21/windowsdesktop-runtime-9.0.21-win-arm64.exe",
        "windowsdesktop-runtime-9.0.21-win-arm64.exe", "/install /quiet /norestart", ["Microsoft Corporation"],
        "ED63B8CB723F1B0D872B31FC318AA2CC89EE9797E8772C28C2990BCDDA849389");
    public static readonly VendorFallback ControlPanel = new(
        "https://softwarecenter.qualcomm.com/api/download/software/tools/SnapdragonControlPanel/Windows/ARM64/2026.3.0.0/Snapdragon_Control_Panel_2026.3.0.0.zip",
        "Snapdragon_Control_Panel_2026.3.0.0.zip", "", ["Qualcomm Inc"],
        "A10841A43EE4FEE51BFA13F7BDB5FB872C64070155DAAF4BE08625C9ADC2408F", PortableZip: true);
    public const string ArchiveEntry = "2026.3.0.0/SnapdragonControlPanel_2026.3.0.0_ARM64.msix";
    private static string PowerShell => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        "System32", "WindowsPowerShell", "v1.0", "powershell.exe");

    public bool HasRuntime()
    {
        if (runtimePresent is not null) return runtimePresent();
        // ProgramW6432 selects the native installation even in an emulated process.
        var root = Environment.GetEnvironmentVariable("ProgramW6432") ??
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var dotnet = Path.Combine(root, "dotnet", "dotnet.exe");
        if (BinaryArchitecture.Read(dotnet) != System.Runtime.InteropServices.Architecture.Arm64) return false;
        try
        {
            var result = process.Run(dotnet, ["--list-runtimes"], TimeSpan.FromSeconds(30), false);
            return result.Success && SnapdragonSupport.HasRequiredRuntime(result.StandardOutput);
        }
        catch (Exception exception) { log.Warn("ARM64 Desktop Runtime detection: " + exception.Message); return false; }
    }

    public async Task<ResultKind> EnsureRuntimeAsync(string cache, CancellationToken token)
    {
        if (!(eligible?.Invoke() ?? SnapdragonSupport.IsEligibleSystem())) throw new InvalidOperationException("Snapdragon Control Panel requires a Snapdragon ARM64 PC running Windows 11 24H2 or later.");
        if (HasRuntime()) return ResultKind.Skipped;
        var installer = await Download(Runtime, cache, token).ConfigureAwait(false);
        // Microsoft's ARM64 runtime uses an x86 Burn bootstrapper. The pinned
        // vendor hash identifies the ARM64 payload; verify the native runtime afterward.
        var result = await Run(installer, ["/install", "/quiet", "/norestart"], token).ConfigureAwait(false);
        if (!result.Success) throw new InvalidOperationException($"ARM64 .NET Desktop Runtime installation failed: {result.ExitCode}.");
        if (result.RebootRequired) return ResultKind.RebootRequired;
        if (!HasRuntime()) throw new InvalidOperationException("ARM64 .NET Desktop Runtime 9.0.21 or newer 9.0 patch was not detected after installation.");
        return ResultKind.Succeeded;
    }

    public bool IsInstalled()
    {
        try
        {
            var result = process.Run(PowerShell, Arguments(DetectionScript), TimeSpan.FromSeconds(45), false);
            return result.Success && result.StandardOutput.Split(['\r', '\n']).Any(x => x.Trim() == "PCSETUP_SNAPDRAGON_INSTALLED");
        }
        catch (Exception exception) { log.Warn("Snapdragon package detection: " + exception.Message); return false; }
    }

    public static string DetectionScript =>
        "$ErrorActionPreference='Stop'; Import-Module Appx -ErrorAction Stop; " +
        "Get-AppxPackage -Name 'AdrenoControlPanel' | Where-Object { " +
        "$_.Publisher -eq '" + SnapdragonSupport.Publisher + "' -and $_.Architecture -eq 'Arm64' -and " +
        "$_.Version -ge [version]'2026.3.0.0' -and $_.Status -eq 'Ok' -and $_.InstallLocation -and " +
        "(Test-Path -LiteralPath $_.InstallLocation) } | ForEach-Object { 'PCSETUP_SNAPDRAGON_INSTALLED' }";

    public async Task<ResultKind> InstallAsync(string cache, CancellationToken token)
    {
        if (!(eligible?.Invoke() ?? SnapdragonSupport.IsEligibleSystem())) throw new InvalidOperationException("This PC is not eligible for Snapdragon Control Panel.");
        if (!HasRuntime()) throw new InvalidOperationException("The required native ARM64 .NET 9 Desktop Runtime is missing; Control Panel was not installed.");
        if (IsInstalled()) return ResultKind.Skipped;
        var archive = await Download(ControlPanel, cache, token).ConfigureAwait(false);
        var package = ExtractPackage(archive);
        downloads.VerifyAuthenticode(package, ControlPanel.AllowedSignerFragments);
        ValidateManifest(package);
        var result = await process.RunAsync(PowerShell, Arguments(BuildInstallScript(package)),
            TimeSpan.FromMinutes(15), token).ConfigureAwait(false);
        if (!result.Success) throw new InvalidOperationException("Snapdragon Control Panel installation failed. " + result.StandardError.Trim());
        return IsInstalled() ? ResultKind.Succeeded : ResultKind.NeedsVerification;
    }

    public static string ExtractPackage(string archive)
    {
        using var input = File.OpenRead(archive);
        if (!Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(input)).Equals(ControlPanel.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Qualcomm archive hash mismatch.");
        using var zip = ZipFile.OpenRead(archive);
        var matches = zip.Entries.Where(x => x.FullName == ArchiveEntry).ToArray();
        if (matches.Length != 1 || matches[0].Length == 0) throw new InvalidDataException("Expected Qualcomm MSIX missing or duplicated.");
        var destination = archive + ".msix";
        matches[0].ExtractToFile(destination, true);
        return destination;
    }

    public static void ValidateManifest(string package)
    {
        using var zip = ZipFile.OpenRead(package);
        var entry = zip.GetEntry("AppxManifest.xml") ?? throw new InvalidDataException("MSIX manifest missing.");
        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        XNamespace ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
        var identity = document.Root?.Element(ns + "Identity");
        if ((string?)identity?.Attribute("Name") != SnapdragonSupport.PackageName ||
            (string?)identity?.Attribute("Publisher") != SnapdragonSupport.Publisher ||
            (string?)identity?.Attribute("ProcessorArchitecture") != "arm64" ||
            (string?)identity?.Attribute("Version") != SnapdragonSupport.PackageVersion.ToString())
            throw new InvalidDataException("Unexpected Qualcomm MSIX identity, publisher, architecture or version.");
    }

    public static string BuildInstallScript(string package) =>
        "$ErrorActionPreference='Stop'; Import-Module Appx -ErrorAction Stop; " +
        // Windows checks all manifest dependencies as part of deployment. Report missing
        // frameworks explicitly instead of downloading unverified replacement packages.
        "foreach ($name in @('Microsoft.VCLibs.140.00.UWPDesktop','Microsoft.VCLibs.140.00')) { " +
        "if (-not (Get-AppxPackage -Name $name | Where-Object { $_.Architecture -eq 'Arm64' -and " +
        "$_.Publisher -eq 'CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US' -and " +
        "$_.Version -ge [version]'14.0.24217.0' -and $_.Status -eq 'Ok' })) { " +
        "throw ('Missing ARM64 framework: ' + $name + '. Update Microsoft Store apps and App Installer, then retry.') } }; " +
        "Add-AppxPackage -Path '" + package.Replace("'", "''") + "' -ErrorAction Stop";

    private static string[] Arguments(string script) => ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
        "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script))];

    private Task<string> Download(VendorFallback fallback, string cache, CancellationToken token) =>
        download?.Invoke(fallback, cache, token) ?? downloads.DownloadAndVerifyAsync(fallback, cache, token);
    private Task<ProcessResult> Run(string file, string[] args, CancellationToken token) =>
        run?.Invoke(file, args, token) ?? process.RunAsync(file, args, TimeSpan.FromMinutes(20), token);
}
