using System.Text;

namespace PCSetup.Services;

public sealed class HostsFileService
{
    private const string Begin = "# PCSetup Razer block - begin";
    private const string End = "# PCSetup Razer block - end";
    private readonly ProcessRunner _process;
    private readonly RunLogger _log;

    public HostsFileService(ProcessRunner process, RunLogger log)
    {
        _process = process;
        _log = log;
    }

    public async Task ApplyRazerBlockAsync(SetupState state, CancellationToken cancellationToken)
    {
        if (!state.Profile.BlockRazerDomains)
        {
            _log.Info("Razer domain blocking was not selected; the hosts file was not changed.");
            return;
        }

        var hosts = Path.Combine(Environment.SystemDirectory, "drivers", "etc", "hosts");
        var current = File.ReadAllText(hosts);
        if (current.Contains(Begin, StringComparison.Ordinal))
        {
            _log.Info("The PCSetup-managed Razer domain block is already present.");
            return;
        }

        var backup = Path.Combine(Path.GetDirectoryName(state.StatePath)!, "hosts.before");
        if (!File.Exists(backup)) File.Copy(hosts, backup, false);
        var block = new[]
        {
            "", Begin,
            "0.0.0.0 downloads.razer.com",
            "0.0.0.0 dl.razerzone.com",
            End, ""
        };
        File.AppendAllLines(hosts, block, new UTF8Encoding(false));
        var flush = await _process.RunAsync("ipconfig.exe", ["/flushdns"], TimeSpan.FromMinutes(2), cancellationToken);
        if (!flush.Success) throw new InvalidOperationException("The hosts file was updated, but the DNS cache could not be flushed.");
        _log.Info("Blocked only downloads.razer.com and dl.razerzone.com; the original hosts file was backed up.");
    }
}
