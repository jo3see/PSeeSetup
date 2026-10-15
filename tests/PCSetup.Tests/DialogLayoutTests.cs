using System.Drawing;
using System.Windows.Forms;
using PCSetup;
using PCSetup.UI;

public static class DialogLayoutTests
{
    public static void Verify()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                foreach (var scale in new[] { 1f, 1.25f, 1.5f, 2f })
                {
                    var gate = new BitLockerGateResult();
                    gate.Volumes.Add(new BitLockerVolumeStatus("C:", false, "Fully encrypted", 100,
                        "Protection off", "Fully encrypted; 100% encrypted; Protection off; Unlocked."));
                    using var form = new BitLockerChoiceDialog(gate);
                    // Render the synthetic dialog off-screen; no production startup,
                    // encryption checks, decryption or button actions are invoked.
                    form.StartPosition = FormStartPosition.Manual;
                    form.ShowInTaskbar = false;
                    form.Location = new Point(-30000, -30000);
                    form.Show();
                    form.Scale(new SizeF(scale, scale));
                    form.Font = new Font("Segoe UI", 9.5f * scale);
                    foreach (var heading in Descendants(form).OfType<Label>().Where(x => x.Text.StartsWith("Device Encryption")))
                        heading.Font = new Font("Segoe UI Semibold", 14 * scale);
                    foreach (var width in new[] { 740, 620 })
                    {
                        form.ClientSize = new Size((int)(width * scale), (int)(460 * scale));
                        form.PerformLayout();
                        Application.DoEvents();
                        foreach (var control in Descendants(form))
                        {
                            control.PerformLayout();
                            if (control is not Label and not Button) continue;
                            var location = control.Location;
                            for (var parent = control.Parent; parent is not null && parent != form; parent = parent.Parent)
                                location.Offset(parent.Location);
                            var bounds = new Rectangle(location, control.Size);
                            Require(bounds.Left >= 0 && bounds.Right <= form.ClientSize.Width,
                                $"{scale:P0}/{width}: {control.Text} extends beyond the dialog horizontally ({bounds}).");
                            Require(bounds.Top >= 0 && bounds.Bottom <= form.ClientSize.Height,
                                $"{scale:P0}/{width}: {control.Text} extends beyond the dialog vertically ({bounds}).");
                            if (control is Label label)
                            {
                                var required = label.GetPreferredSize(new Size(label.Width, 0));
                                Require(label.Height >= required.Height,
                                    $"{scale:P0}/{width}: label text is clipped vertically.");
                            }
                            if (control is Button button)
                            {
                                var textSize = TextRenderer.MeasureText(button.Text, button.Font);
                                Require(button.Width >= textSize.Width + 8,
                                    $"{scale:P0}/{width}: button text is clipped.");
                            }
                        }
                    }
                    Require(form.AcceptButton is Button { DialogResult: DialogResult.OK }, "Decryption confirmation changed.");
                    Require(form.CancelButton is Button { DialogResult: DialogResult.Cancel }, "Close behavior changed.");
                    var imageDirectory = Environment.GetEnvironmentVariable("PCSETUP_TEST_PREVIEW_DIR");
                    if (!string.IsNullOrWhiteSpace(imageDirectory))
                    {
                        Directory.CreateDirectory(imageDirectory);
                        using var bitmap = new Bitmap(form.Width, form.Height);
                        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                        bitmap.Save(Path.Combine(imageDirectory, $"encryption-dialog-{scale * 100:0}.png"));
                    }
                }
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Require(thread.Join(TimeSpan.FromSeconds(45)), "Dialog layout test timed out.");
        if (failure is not null) throw failure;
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            yield return control;
            foreach (var descendant in Descendants(control)) yield return descendant;
        }
    }
    private static void Require(bool condition, string reason)
    {
        if (!condition) throw new InvalidOperationException(reason);
    }
}
