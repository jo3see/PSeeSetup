namespace PCSetup.UI;

public sealed class BitLockerChoiceDialog : Form
{
    public BitLockerChoiceDialog(BitLockerGateResult gate)
    {
        Text = "Disk encryption prerequisite";
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5f);
        ClientSize = new Size(740, 460);
        MinimumSize = new Size(620, 450);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22),
            ColumnCount = 1,
            RowCount = 6,
            AutoScroll = true
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles[5] = new RowStyle(SizeType.Absolute, 48);

        var heading = new Label
        {
            Text = "Device Encryption / BitLocker must be fully disabled before PSeeSetup can continue",
            Font = new Font("Segoe UI Semibold", 14),
            ForeColor = Color.FromArgb(157, 57, 36),
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 10)
        };
        var summary = new Label
        {
            Text = "Windows reports encryption on the following internal drive(s):",
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 8)
        };
        var details = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            WordWrap = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            MinimumSize = new Size(0, 85),
            Text = string.Join(Environment.NewLine, gate.Volumes.Where(x => !x.Safe).Select(x => $"{x.Drive} — {x.Details}")),
            BackColor = Color.White
        };
        var closeExplanation = new Label
        {
            Text = "Option 1 closes PSeeSetup without making any change.",
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 14, 0, 4)
        };
        var decryptExplanation = new Label
        {
            Text = "Option 2 starts full decryption, displays progress, waits until every listed drive reaches 0%, then restarts Windows and reopens PSeeSetup automatically after sign-in. No applications, UAC changes, or tweaks run during this prerequisite.",
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 14)
        };
        var buttons = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty
        };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var close = new Button
        {
            Text = "Close PSeeSetup",
            AutoSize = false,
            Dock = DockStyle.Fill,
            MinimumSize = new Size(0, 38),
            Margin = new Padding(0, 0, 6, 0),
            DialogResult = DialogResult.Cancel
        };
        var decrypt = new Button
        {
            Text = "Decrypt drive(s) and continue",
            AutoSize = false,
            Dock = DockStyle.Fill,
            MinimumSize = new Size(0, 38),
            Margin = new Padding(6, 0, 0, 0),
            DialogResult = DialogResult.OK
        };
        buttons.Controls.Add(close, 0, 0);
        buttons.Controls.Add(decrypt, 1, 0);
        layout.Controls.Add(heading, 0, 0);
        layout.Controls.Add(summary, 0, 1);
        layout.Controls.Add(details, 0, 2);
        layout.Controls.Add(closeExplanation, 0, 3);
        layout.Controls.Add(decryptExplanation, 0, 4);
        layout.Controls.Add(buttons, 0, 5);
        Controls.Add(layout);

        // AutoSize labels must have an explicit available width to wrap inside
        // their cell. Recompute it after DPI scaling and window resizing.
        var labels = new[] { heading, summary, closeExplanation, decryptExplanation };
        layout.SizeChanged += (_, _) =>
        {
            var width = Math.Max(1, layout.ClientSize.Width - layout.Padding.Horizontal);
            foreach (var label in labels)
            {
                var maximum = new Size(Math.Max(1, width - label.Margin.Horizontal), 0);
                if (label.MaximumSize != maximum) label.MaximumSize = maximum;
            }
        };
        layout.PerformLayout();
        AcceptButton = decrypt;
        CancelButton = close;
    }
}
