using PCSetup.Services;

namespace PCSetup.UI;

public sealed class BitLockerPreparationForm : Form
{
    private readonly BitLockerPreparationService _service;
    private BitLockerGateResult _currentGate;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Label _headline = new()
    {
        Text = "Disabling BitLocker…",
        Dock = DockStyle.Top,
        Height = 48,
        Font = new Font("Segoe UI Semibold", 16),
        Padding = new Padding(0, 8, 0, 0)
    };
    private readonly Label _details = new()
    {
        Dock = DockStyle.Top,
        Height = 58,
        Text = "Keep the computer plugged in. PSeeSetup will wait for full decryption before restarting Windows."
    };
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Top, Height = 28, Minimum = 0, Maximum = 100 };
    private readonly ListView _volumes = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
    private readonly Button _close = new() { Text = "Close window", AutoSize = true, Height = 34 };
    private readonly Label _footer = new()
    {
        Text = "If you close this window, Windows continues decrypting. Reopen PSeeSetup later to resume checking progress.",
        AutoSize = true,
        ForeColor = Color.DimGray,
        Padding = new Padding(8, 8, 0, 0)
    };
    private bool _restartScheduled;

    public BitLockerPreparationForm(BitLockerPreparationService service, BitLockerGateResult initialGate)
    {
        _service = service;
        _currentGate = initialGate;
        Text = "PSeeSetup — BitLocker preparation";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(720, 430);
        Size = new Size(820, 500);
        Font = new Font("Segoe UI", 9.5f);
        Padding = new Padding(22);

        _volumes.Columns.Add("Drive", 90);
        _volumes.Columns.Add("Status", 180);
        _volumes.Columns.Add("Encrypted", 110);
        _volumes.Columns.Add("Protection", 140);
        _volumes.Columns.Add("Details", 260);
        _close.Click += (_, _) => Close();

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 56,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 10, 0, 0)
        };
        bottom.Controls.Add(_close);
        bottom.Controls.Add(_footer);
        Controls.Add(_volumes);
        Controls.Add(_progress);
        Controls.Add(_details);
        Controls.Add(_headline);
        Controls.Add(bottom);
        UpdateDisplay(_currentGate);
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        try
        {
            await _service.StartOrContinueDecryptionAsync(_currentGate, _cancellation.Token);
            while (!_cancellation.IsCancellationRequested)
            {
                _currentGate = await Task.Run(_service.Check, _cancellation.Token);
                UpdateDisplay(_currentGate);
                if (_currentGate.IsSafe)
                {
                    _headline.Text = "BitLocker is fully decrypted";
                    _details.Text = "Windows will restart in about 20 seconds. PSeeSetup will reopen automatically after you sign in.";
                    _progress.Value = 100;
                    _close.Enabled = false;
                    await _service.ScheduleRestartAsync(_cancellation.Token);
                    _restartScheduled = true;
                    await Task.Delay(1500);
                    Close();
                    return;
                }
                await Task.Delay(TimeSpan.FromSeconds(10), _cancellation.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            _headline.Text = "BitLocker decryption needs attention";
            _details.Text = exception.Message;
            _details.ForeColor = Color.Firebrick;
            MessageBox.Show(this, exception.Message, "BitLocker could not be disabled", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_restartScheduled)
        {
            var answer = MessageBox.Show(this,
                "Windows will continue decrypting in the background, but the automatic restart will not occur while this window is closed. You can reopen PSeeSetup later to continue.\n\nClose the progress window?",
                "Continue decryption in the background", MessageBoxButtons.YesNo, MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) { e.Cancel = true; return; }
        }
        _cancellation.Cancel();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _cancellation.Dispose();
        base.Dispose(disposing);
    }

    private void UpdateDisplay(BitLockerGateResult gate)
    {
        if (InvokeRequired)
        {
            if (IsDisposed || Disposing || !IsHandleCreated) return;
            BeginInvoke(() => UpdateDisplay(gate));
            return;
        }
        _volumes.Items.Clear();
        foreach (var volume in gate.Volumes)
        {
            var item = new ListViewItem([
                volume.Drive,
                volume.ConversionStatus,
                volume.EncryptionPercentage < 0 ? "Unknown" : volume.EncryptionPercentage + "%",
                volume.ProtectionStatus,
                volume.Details
            ]);
            item.ForeColor = volume.Safe ? Color.FromArgb(20, 111, 72) : Color.DarkOrange;
            _volumes.Items.Add(item);
        }
        var known = gate.Volumes.Where(x => x.EncryptionPercentage >= 0).ToArray();
        if (known.Length > 0)
        {
            var encryptedAverage = (int)Math.Round(known.Average(x => x.EncryptionPercentage));
            _progress.Value = Math.Clamp(100 - encryptedAverage, 0, 100);
            _headline.Text = encryptedAverage == 0 ? "Verifying BitLocker is fully disabled…" : $"Disabling BitLocker — {100 - encryptedAverage}% decrypted";
        }
    }
}
