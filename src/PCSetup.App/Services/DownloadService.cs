using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PCSetup.Services;

public sealed class DownloadService
{
    private readonly HttpClient _client;
    private readonly ProcessRunner _process;
    private readonly RunLogger _log;

    public DownloadService(ProcessRunner process, RunLogger log)
    {
        _process = process;
        _log = log;
        _client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true })
        {
            Timeout = TimeSpan.FromMinutes(20)
        };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("PCSetup/1.0");
    }

    public async Task<string> DownloadAndVerifyAsync(VendorFallback fallback, string cacheDirectory,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(fallback.Url, UriKind.Absolute, out var source) || source.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Vendor fallbacks must use HTTPS.");

        Directory.CreateDirectory(cacheDirectory);
        var destination = Path.Combine(cacheDirectory, fallback.FileName);
        var partial = destination + ".partial";
        if (File.Exists(partial)) File.Delete(partial);

        _log.Info($"Downloading official vendor fallback from {source.Host}.");
        using var response = await _client.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var finalUri = response.RequestMessage?.RequestUri ?? source;
        if (finalUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("The vendor download redirected away from HTTPS.");
        if (response.Content.Headers.ContentType?.MediaType is "text/html" or "application/xhtml+xml")
            throw new InvalidDataException("The vendor returned a web page instead of an installer.");

        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, true))
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        try { ValidatePayload(partial, fallback.PortableZip || fallback.MsiArchiveEntry is not null); }
        catch { File.Delete(partial); throw; }
        File.Move(partial, destination, true);

        var sha256 = await ComputeSha256Async(destination, cancellationToken).ConfigureAwait(false);
        _log.Info($"Downloaded {fallback.FileName}; SHA256={sha256}; final host={finalUri.Host}.");
        var matchedPinnedHash = !string.IsNullOrWhiteSpace(fallback.ExpectedSha256)
                                && sha256.Equals(fallback.ExpectedSha256, StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(fallback.ExpectedSha256)
            && !matchedPinnedHash)
        {
            File.Delete(destination);
            throw new InvalidDataException("The vendor download did not match its pinned SHA256 hash.");
        }

        if (!fallback.PortableZip && fallback.MsiArchiveEntry is null)
        {
            if (fallback.AllowPinnedHashWithoutSignature && matchedPinnedHash)
                _log.Info($"Accepted {fallback.FileName} using its exact pinned SHA256 hash.");
            else
                VerifyAuthenticode(destination, fallback.AllowedSignerFragments);
        }
        return destination;
    }

    public string ExtractVerifiedArmMsi(VendorFallback fallback, string archive)
    {
        if (fallback.MsiArchiveEntry is null || string.IsNullOrEmpty(fallback.ExpectedSha256))
            throw new InvalidOperationException("An MSI archive requires an exact hash and entry name.");
        using (var input = File.OpenRead(archive))
            if (!Convert.ToHexString(SHA256.HashData(input)).Equals(fallback.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The MSI archive did not match its pinned SHA256 hash.");
        using var zip = ZipFile.OpenRead(archive);
        var matches = zip.Entries.Where(x => x.FullName == fallback.MsiArchiveEntry).ToArray();
        if (matches.Length != 1 || matches[0].Length == 0)
            throw new InvalidDataException("The vendor archive does not contain exactly one expected MSI.");
        // Extract only the named package; never execute or expand other archive entries.
        var destination = archive + ".msi";
        matches[0].ExtractToFile(destination, true);
        VerifyAuthenticode(destination, fallback.AllowedSignerFragments);
        if (MsiPackage.ReadArchitecture(destination) != System.Runtime.InteropServices.Architecture.Arm64)
            throw new InvalidDataException("The signed vendor MSI is not an ARM64 package.");
        return destination;
    }

    public static async Task<string> ComputeSha256Async(string path,
        CancellationToken cancellationToken = default)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false));
    }

    public static void ValidatePayload(string path, bool portableZip)
    {
        if (!portableZip)
        {
            if (BinaryArchitecture.Read(path) is null)
                throw new InvalidDataException("The download is not a valid Windows executable; a web page cannot be accepted as an installer.");
        }
        else
        {
            using var archive = ZipFile.OpenRead(path);
            if (!archive.Entries.Any(x => x.Length > 0))
                throw new InvalidDataException("The portable archive has no application files.");
        }
    }

    public string InstallPortable(VendorFallback fallback, string archive, InstallContext context)
    {
        if (!fallback.PortableZip || string.IsNullOrWhiteSpace(fallback.PortableExecutable))
            throw new InvalidOperationException("The fallback is not a portable ZIP definition.");

        var root = context == InstallContext.User
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs")
            : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var installDirectory = Path.Combine(root, fallback.InstallDirectoryName ?? Path.GetFileNameWithoutExtension(archive));
        var temporary = installDirectory + ".new";
        if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
        Directory.CreateDirectory(temporary);
        ZipFile.ExtractToDirectory(archive, temporary, true);
        var executable = Directory.EnumerateFiles(temporary, fallback.PortableExecutable, SearchOption.AllDirectories).FirstOrDefault()
            ?? throw new InvalidDataException($"{fallback.PortableExecutable} was not found inside the portable archive.");
        VerifyAuthenticode(executable, fallback.AllowedSignerFragments);
        if (BinaryArchitecture.Read(executable) != ArchitecturePolicy.OSArchitecture)
            throw new InvalidDataException("The portable fallback does not match the native Windows architecture.");

        if (Directory.Exists(installDirectory)) Directory.Delete(installDirectory, true);
        Directory.Move(temporary, installDirectory);
        executable = Directory.EnumerateFiles(installDirectory, fallback.PortableExecutable, SearchOption.AllDirectories).First();
        CreateStartupShortcut(executable, Path.GetFileNameWithoutExtension(fallback.PortableExecutable));
        _log.Info($"Installed portable application in {installDirectory} and created its Startup shortcut.");
        return executable;
    }

    public void VerifyAuthenticode(string path, string[] allowedSignerFragments)
    {
        var encodedCommand = BuildAuthenticodeEncodedCommand(path);
        var result = _process.Run(GetWindowsPowerShell(),
            ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encodedCommand],
            TimeSpan.FromMinutes(2), false);
        var lines = result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var payload = lines.LastOrDefault(x => x.StartsWith("PCSETUP_SIGNATURE:", StringComparison.Ordinal));
        var status = "Unknown";
        var signer = "";
        if (payload is not null)
        {
            using var document = JsonDocument.Parse(payload["PCSETUP_SIGNATURE:".Length..]);
            status = document.RootElement.GetProperty("Status").GetString() ?? "Unknown";
            signer = document.RootElement.GetProperty("Signer").GetString() ?? "";
        }
        if (result.ExitCode != 0 || !status.Equals("Valid", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Windows did not validate the digital signature on {Path.GetFileName(path)} (status {status}; exit {result.ExitCode}). {result.StandardError.Trim()} {result.StandardOutput.Trim()}");
        if (allowedSignerFragments.Length > 0
            && !allowedSignerFragments.Any(fragment => signer.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException($"The valid signature on {Path.GetFileName(path)} was from an unexpected publisher: {signer}");
        _log.Info($"Windows validated the digital signature on {Path.GetFileName(path)}: {signer}");
    }

    public static string BuildAuthenticodeEncodedCommand(string path)
    {
        var escapedPath = path.Replace("'", "''", StringComparison.Ordinal);
        var script = "Import-Module ($PSHOME + '\\Modules\\Microsoft.PowerShell.Security\\Microsoft.PowerShell.Security.psd1') -ErrorAction Stop;" +
                     "$s=Get-AuthenticodeSignature -ErrorAction Stop -LiteralPath '" + escapedPath + "';" +
                     "Write-Output ('PCSETUP_SIGNATURE:' + (@{Status=[string]$s.Status;Signer=[string]$s.SignerCertificate.Subject} | ConvertTo-Json -Compress))";
        return Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
    }

    private static void CreateStartupShortcut(string executable, string name)
    {
        var startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        Directory.CreateDirectory(startup);
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows Script Host shortcut automation is unavailable.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(Path.Combine(startup, name + ".lnk"));
        shortcut.TargetPath = executable;
        shortcut.WorkingDirectory = Path.GetDirectoryName(executable);
        shortcut.Description = $"Start {name} when signing in";
        shortcut.Save();
    }

    private static string GetWindowsPowerShell() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
}
