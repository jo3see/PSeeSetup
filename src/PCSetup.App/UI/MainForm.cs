using PCSetup.Services;

namespace PCSetup.UI;

public sealed class MainForm : Form
{
    public const string HeaderSubtitle = "Choose your installations, tweaks and app removals, then select \"Execute selections\".";
    public const string LiveSetupButtonText = "Execute selections";
    public const string ExitButtonText = "Exit";
    public const string NextButtonText = "Next";

    private readonly SetupState? _resumeState;
    private SetupProfile _profile;
    private readonly BitLockerGateResult _startupGate;
    private readonly TabControl _tabs = new()
    {
        Dock = DockStyle.Fill,
        Appearance = TabAppearance.FlatButtons,
        DrawMode = TabDrawMode.OwnerDrawFixed,
        SizeMode = TabSizeMode.Normal,
        Padding = new Point(12, 4)
    };
    private readonly TextBox _appSearch = new() { PlaceholderText = "Search applications…", Width = 260 };
    private readonly ComboBox _appCategory = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 };
    private readonly DataGridView _appsGrid = new();
    private readonly DataGridView _tweaksGrid = new();
    private readonly DataGridView _uninstallGrid = new();
    private readonly Button _refreshUninstalls = new() { Text = "Refresh installed apps", AutoSize = true };
    private readonly Func<Task<RemovalInventory>> _inventoryScan;
    private RemovalInventory? _removalInventory;
    private bool _scanningRemovals;
    private readonly CheckedListBox _featuresList = new() { CheckOnClick = true, Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
    private readonly CheckBox _blockRazer = new()
    {
        Text = "Block only downloads.razer.com and dl.razerzone.com",
        AutoSize = true
    };
    private readonly ListView _status = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Top, Height = 24 };
    private readonly Label _current = new() { Dock = DockStyle.Top, Height = 44, Padding = new Padding(4, 10, 4, 4), AutoEllipsis = true };
    private readonly Button _start = new() { Text = LiveSetupButtonText, AutoSize = true, Height = 38 };
    private readonly Button _exit = new() { Text = ExitButtonText, AutoSize = true, Height = 38 };
    private readonly Button _openLog = new() { Text = "Open log", AutoSize = true, Height = 38, Enabled = false };
    private readonly Button _retry = new() { Text = "Retry failed apps…", AutoSize = true, Height = 38 };
    private string? _currentLogFile;
    private readonly Label _logPath = new() { AutoSize = true, ForeColor = Color.DimGray };
    private CancellationTokenSource? _runCancellation;
    private bool _updatingSelections;
    private bool _autoStarted;
    private bool _runCompleted;

    public MainForm(SetupState? resumeState, BitLockerGateResult startupGate, Func<Task<RemovalInventory>>? inventoryScan = null)
    {
        _resumeState = resumeState;
        _startupGate = startupGate;
        _profile = resumeState?.Profile ?? SetupProfile.DefaultSelection();
        _inventoryScan = inventoryScan ?? (async () =>
        {
            using var log = new RunLogger();
            return await new UninstallService(new ProcessRunner(log), log).ScanAsync().ConfigureAwait(false);
        });
        Text = $"PSeeSetup 1.2.6 — {ArchitecturePolicy.NativeArchitecture.ToUpperInvariant()}";
        MinimumSize = new Size(980, 680);
        Size = new Size(1180, 820);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);
        BackColor = Color.FromArgb(246, 248, 251);

        Controls.Add(_tabs);
        Controls.Add(BuildHeader());
        _tabs.DrawItem += DrawTab;
        BuildApplicationsPage();
        BuildTweaksPage();
        BuildUninstallPage();
        BuildRunPage();
        ApplyProfileToControls();

        if (_resumeState is not null)
        {
            SetLogFile(StagingService.LogPath(_resumeState.RunId));
            _start.Text = _resumeState.Phase == SetupPhase.Failed ? "Retry Saved Setup" : "Resume Setup";
            _tabs.SelectedIndex = _tabs.TabPages.Count - 1;
            if (_resumeState.Phase == SetupPhase.Complete) SetRunCompleted();
        }
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_resumeState is null) await RefreshUninstallInventoryAsync();
        if (IsDisposed || Disposing) return;
        if (_resumeState is not null && !_autoStarted && _resumeState.Phase != SetupPhase.Failed && !_resumeState.IsTerminal)
        {
            _autoStarted = true;
            await StartRunAsync(true);
        }
    }

    private Control BuildHeader()
    {
        var panel = new Panel { Dock = DockStyle.Top, Height = 88, BackColor = Color.FromArgb(27, 54, 93), Padding = new Padding(24, 12, 24, 10) };
        var title = new Label
        {
            Text = "PSeeSetup",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 20),
            Dock = DockStyle.Top,
            Height = 40
        };
        var subtitle = new Label
        {
            Text = HeaderSubtitle,
            ForeColor = Color.FromArgb(218, 228, 242),
            Dock = DockStyle.Fill
        };
        panel.Controls.Add(subtitle);
        panel.Controls.Add(title);
        return panel;
    }

    private void BuildApplicationsPage()
    {
        var page = NewPage("1  Applications");
        ConfigureGrid(_appsGrid);
        _appsGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Selected", HeaderText = "Install", Width = 64 });
        _appsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Application", Width = 230, ReadOnly = true });
        _appsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Category", HeaderText = "Category", Width = 150, ReadOnly = true });
        _appsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Compatibility", HeaderText = "Compatibility", Width = 140, ReadOnly = true });
        _appsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Description", HeaderText = "What it does", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ReadOnly = true });
        _appsGrid.CurrentCellDirtyStateChanged += (_, _) => { if (_appsGrid.IsCurrentCellDirty) _appsGrid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _appsGrid.CellValueChanged += AppsGridCellValueChanged;

        _appSearch.TextChanged += (_, _) => PopulateApplications();
        _appCategory.Items.Add("All categories");
        foreach (var category in Catalog.Applications.Select(x => x.Category).Distinct()) _appCategory.Items.Add(category);
        _appCategory.SelectedIndex = 0;
        _appCategory.SelectedIndexChanged += (_, _) => PopulateApplications();
        var all = new Button { Text = "Select visible", AutoSize = true };
        var none = new Button { Text = "Clear visible", AutoSize = true };
        all.Click += (_, _) => SetVisibleApplications(true);
        none.Click += (_, _) => SetVisibleApplications(false);
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(8), WrapContents = false };
        toolbar.Controls.Add(_appSearch);
        toolbar.Controls.Add(_appCategory);
        toolbar.Controls.Add(all);
        toolbar.Controls.Add(none);
        page.Controls.Add(_appsGrid);
        page.Controls.Add(toolbar);
        page.Controls.Add(BuildNextFooter(1));
    }

    private void BuildTweaksPage()
    {
        var page = NewPage("2  Tweaks");
        ConfigureGrid(_tweaksGrid);
        _tweaksGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Selected", HeaderText = "Apply", Width = 64 });
        _tweaksGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Tweak", Width = 260, ReadOnly = true });
        _tweaksGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Category", HeaderText = "Category", Width = 140, ReadOnly = true });
        _tweaksGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Description", HeaderText = "Effect", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ReadOnly = true });
        _tweaksGrid.CurrentCellDirtyStateChanged += (_, _) => { if (_tweaksGrid.IsCurrentCellDirty) _tweaksGrid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _tweaksGrid.CellValueChanged += TweaksGridCellValueChanged;

        _featuresList.ItemCheck += (_, e) =>
        {
            if (_updatingSelections) return;
            var feature = (FeatureDefinition)_featuresList.Items[e.Index]!;
            if (e.NewValue == CheckState.Checked) _profile.Features.Add(feature.Key); else _profile.Features.Remove(feature.Key);
            RefreshSelectionSummary();
        };
        _featuresList.DisplayMember = nameof(FeatureDefinition.Name);
        foreach (var feature in Catalog.Features) _featuresList.Items.Add(feature);
        _blockRazer.CheckedChanged += (_, _) =>
        {
            if (_updatingSelections) return;
            _profile.BlockRazerDomains = _blockRazer.Checked;
            RefreshSelectionSummary();
        };

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 470 };
        split.Panel1.Controls.Add(_tweaksGrid);
        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8), ColumnCount = 2, RowCount = 2 };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        bottom.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        bottom.Controls.Add(new Label { Text = "Optional hosts-file choice", AutoSize = true, Font = new Font(Font, FontStyle.Bold) }, 0, 0);
        bottom.Controls.Add(new Label { Text = "Windows features", AutoSize = true, Font = new Font(Font, FontStyle.Bold) }, 1, 0);
        var razerPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 12, 12, 0) };
        razerPanel.Controls.Add(new Label
        {
            Text = "Unchecked means the hosts file is not touched at all. Checked adds an app-marked block for only the two agreed Razer download domains, backs up the original file, and flushes DNS. No permissions or ownership are changed.",
            Dock = DockStyle.Fill,
            AutoSize = false,
            Padding = new Padding(0, 36, 0, 0)
        });
        _blockRazer.Dock = DockStyle.Top;
        razerPanel.Controls.Add(_blockRazer);
        bottom.Controls.Add(razerPanel, 0, 1);
        bottom.Controls.Add(_featuresList, 1, 1);
        split.Panel2.Controls.Add(bottom);
        page.Controls.Add(split);
        page.Controls.Add(BuildNextFooter(2));
    }

    private void BuildUninstallPage()
    {
        var page = NewPage("3  Uninstall Apps");
        ConfigureGrid(_uninstallGrid);
        _uninstallGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Selected", HeaderText = "Remove", Width = 70 });
        _uninstallGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Application", Width = 230, ReadOnly = true });
        _uninstallGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Category", HeaderText = "Category", Width = 110, ReadOnly = true });
        _uninstallGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Status", Width = 140, ReadOnly = true });
        _uninstallGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Description", HeaderText = "Removal scope / details", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ReadOnly = true });
        _uninstallGrid.CurrentCellDirtyStateChanged += (_, _) => { if (_uninstallGrid.IsCurrentCellDirty) _uninstallGrid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _uninstallGrid.CellBeginEdit += (_, e) => { if (_uninstallGrid.Rows[e.RowIndex].Cells[0].ReadOnly) e.Cancel = true; };
        _uninstallGrid.CellValueChanged += (_, e) =>
        {
            if (_updatingSelections || e.RowIndex < 0 || e.ColumnIndex != 0) return;
            var row = _uninstallGrid.Rows[e.RowIndex];
            if (row.Tag is not UninstallDefinition app || row.Cells[0].ReadOnly) return;
            if (Convert.ToBoolean(row.Cells[0].Value)) _profile.UninstallApplications.Add(app.Key); else _profile.UninstallApplications.Remove(app.Key);
            RefreshSelectionSummary();
        };
        var clear = new Button { Text = "Clear removals", AutoSize = true };
        clear.Click += (_, _) => { _profile.UninstallApplications.Clear(); PopulateUninstalls(); RefreshSelectionSummary(); };
        _refreshUninstalls.Click += async (_, _) => await RefreshUninstallInventoryAsync();
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(8), WrapContents = false };
        toolbar.Controls.Add(_refreshUninstalls); toolbar.Controls.Add(clear);
        page.Controls.Add(_uninstallGrid); page.Controls.Add(toolbar);
        page.Controls.Add(new Label { Text = "Nothing is selected automatically. Store removals affect the signed-in user. Desktop apps and Recall may affect the whole PC.", Dock = DockStyle.Top, Height = 48, Padding = new Padding(8), AutoEllipsis = true });
        page.Controls.Add(BuildNextFooter(3));
    }

    private async Task RefreshUninstallInventoryAsync()
    {
        if (_scanningRemovals || _runCancellation is not null) return;
        _scanningRemovals = true; _refreshUninstalls.Enabled = false; _uninstallGrid.Enabled = false;
        try { _removalInventory = await _inventoryScan(); }
        catch (Exception exception)
        {
            _removalInventory = new RemovalInventory { PackagesAvailable = false, DesktopAvailable = false, FeaturesAvailable = false, StartNamesAvailable = false, Errors = [exception.Message] };
        }
        finally
        {
            _scanningRemovals = false;
            if (!IsDisposed && !Disposing)
            {
                _refreshUninstalls.Enabled = !_profile.ApplicationsOnly;
                _uninstallGrid.Enabled = !_profile.ApplicationsOnly;
                PopulateUninstalls(); RefreshSelectionSummary();
            }
        }
    }

    private void PopulateUninstalls()
    {
        var updating = _updatingSelections; _updatingSelections = true;
        try
        {
            _uninstallGrid.Rows.Clear();
            foreach (var app in UninstallCatalog.Applications)
            {
                var status = _removalInventory is null ? new RemovalStatus(RemovalAvailability.Unknown, "Checking installed apps…") : UninstallService.Status(app, _removalInventory);
                if (!status.CanRemove && _resumeState is null && !_runCompleted && _runCancellation is null) _profile.UninstallApplications.Remove(app.Key);
                var index = _uninstallGrid.Rows.Add(status.CanRemove && _profile.UninstallApplications.Contains(app.Key), app.Name, app.Category,
                    _removalInventory is null ? "Checking…" : status.Availability == RemovalAvailability.NotInstalled ? "Not installed" : status.Availability.ToString(), status.Description);
                var row = _uninstallGrid.Rows[index]; row.Tag = app;
                row.Cells[0].ReadOnly = !status.CanRemove || _profile.ApplicationsOnly || _resumeState is not null;
                if (!status.CanRemove) { row.DefaultCellStyle.ForeColor = Color.DimGray; row.DefaultCellStyle.BackColor = Color.FromArgb(235, 237, 240); }
            }
        }
        finally { _updatingSelections = updating; }
    }

    private void BuildRunPage()
    {
        var page = NewPage("4  Execute");
        _status.Columns.Add("Stage", 160);
        _status.Columns.Add("Item", 260);
        _status.Columns.Add("Result", 110);
        _status.Columns.Add("Details", 520);
        var footer = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 62, Padding = new Padding(10), ColumnCount = 2, RowCount = 1 };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.RightToLeft, Anchor = AnchorStyles.Right };
        _start.Click += async (_, _) => await StartRunAsync(false);
        _exit.Click += (_, _) => ExitOrCancelRun();
        _openLog.Click += (_, _) => OpenCurrentLog();
        _retry.Click += (_, _) => ImportFailedApplications();
        actions.Controls.Add(_start);
        actions.Controls.Add(_openLog);
        actions.Controls.Add(_exit);
        actions.Controls.Add(_retry);
        _logPath.Anchor = AnchorStyles.Left;
        footer.Controls.Add(_logPath, 0, 0);
        footer.Controls.Add(actions, 1, 0);
        page.Controls.Add(_status);
        page.Controls.Add(_current);
        page.Controls.Add(_progress);
        page.Controls.Add(footer);
        page.Controls.Add(new Label
        {
            Text = "Encryption must be fully disabled. UAC finishes disabled; required restarts resume automatically.",
            Dock = DockStyle.Top, Height = 36, Padding = new Padding(8), AutoEllipsis = true
        });
    }

    private async Task StartRunAsync(bool automaticResume)
    {
        if (_runCancellation is not null) return;
        if (!automaticResume)
        {
            var answer = MessageBox.Show(this,
                (_profile.ApplicationsOnly
                    ? "This applications-only retry will install the selected apps. No tweaks, Windows features or hosts changes will run. Encryption must remain disabled; UAC will finish disabled. Required restarts resume automatically (maximum five)."
                    : "This live run will install software, apply selected app removals and Windows settings, permanently disable UAC, and restart the PC as needed (maximum five). It resumes automatically after sign-in.") +
                    (_profile.UninstallApplications.Count == 0 ? "" : "\n\nApps selected for removal:\n" + string.Join("\n", _profile.UninstallApplications.Take(8).Select(x => UninstallCatalog.ByKey(x).Name)) +
                        (_profile.UninstallApplications.Count > 8 ? $"\n…and {_profile.UninstallApplications.Count - 8} more selected on Uninstall Apps." : "")) + "\n\nBegin the live setup?",
                "Confirm live setup", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;
        }

        var state = _resumeState ?? new SetupState { Profile = _profile, InteractiveUser = SystemContext.InteractiveIdentity() };
        var logFile = StagingService.LogPath(state.RunId);
        using var logger = new RunLogger(logFile);
        SetLogFile(logFile);
        logger.LineWritten += line =>
        {
            if (IsDisposed || Disposing || !IsHandleCreated) return;
            try { BeginInvoke(() => _current.Text = line); }
            catch (InvalidOperationException) when (IsDisposed || Disposing || !IsHandleCreated) { }
        };

        _runCancellation = new CancellationTokenSource();
        SetRunning(true);
        _status.Items.Clear();
        _progress.Value = 0;
        var progress = new Progress<ProgressUpdate>(update =>
        {
            _current.Text = $"{update.Stage}: {update.Item} — {update.Message}";
            _progress.Maximum = Math.Max(1, update.Total);
            _progress.Value = Math.Clamp(update.Completed, 0, _progress.Maximum);
            var row = new ListViewItem([update.Stage, update.Item, update.Result.ToString(), update.Message]);
            row.ForeColor = update.Result switch
            {
                ResultKind.Failed => Color.Firebrick,
                ResultKind.Succeeded => Color.FromArgb(20, 111, 72),
                ResultKind.RebootRequired => Color.DarkOrange,
                _ => ForeColor
            };
            _status.Items.Add(row);
            row.EnsureVisible();
        });

        try
        {
            var engine = new SetupEngine(state, logger);
            var outcome = await Task.Run(() => engine.RunAsync(progress, _runCancellation.Token));
            switch (outcome)
            {
                case EngineOutcome.RebootScheduled:
                    MessageBox.Show(this, "Windows will restart in about 20 seconds. PSeeSetup is saved and will resume automatically after you sign in.",
                        "Restart scheduled", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    break;
                case EngineOutcome.CompleteWithIssues:
                    SetRunCompleted();
                    MessageBox.Show(this, "Setup finished with failed applications or items needing verification. Review Final Report.txt. Use Retry failed apps to create a new applications-only run.",
                        "Setup completed with issues", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    break;
                case EngineOutcome.Complete:
                    SetRunCompleted();
                    MessageBox.Show(this, "Setup is complete. The final report and full log are saved with the run state.",
                        "Setup complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    break;
                case EngineOutcome.Failed:
                    MessageBox.Show(this, state.LastError ?? "Setup stopped. Review the log for details.",
                        "Setup stopped safely", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    _start.Text = "Retry Saved Setup";
                    break;
                case EngineOutcome.Cancelled:
                    MessageBox.Show(this, "Setup was cancelled. No further automatic restart or resume is scheduled.",
                        "Setup cancelled", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    break;
            }
        }
        finally
        {
            _runCancellation.Dispose();
            _runCancellation = null;
            SetRunning(false);
            if (_runCompleted && _profile.UninstallApplications.Count > 0) await RefreshUninstallInventoryAsync();
        }
    }

    private void SetRunning(bool running)
    {
        _retry.Enabled = !running;
        _start.Enabled = !running && !_runCompleted;
        _exit.Enabled = true;
        for (var i = 0; i < _tabs.TabPages.Count - 1; i++) _tabs.TabPages[i].Enabled = !running;
        if (running) _tabs.SelectedIndex = _tabs.TabPages.Count - 1;
    }

    private void SetRunCompleted()
    {
        _runCompleted = true;
        _start.Enabled = false;
        _start.Visible = false;
        _exit.Text = ExitButtonText;
        _exit.Enabled = true;
        _openLog.Enabled = _currentLogFile is not null && File.Exists(_currentLogFile);
    }

    private void SetLogFile(string path)
    {
        _currentLogFile = Path.GetFullPath(path);
        _logPath.Text = "Log: " + _currentLogFile;
        _logPath.AutoEllipsis = true;
        _logPath.MaximumSize = new Size(500, 40);
        _openLog.Enabled = File.Exists(_currentLogFile);
    }

    private void OpenCurrentLog()
    {
        try
        {
            if (_currentLogFile is null) throw new InvalidOperationException("Execute your selections to create a log first.");
            LogFileService.Open(_currentLogFile);
        }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Cannot open log", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void RefreshSelectionSummary()
    {
        if (_runCancellation is not null || _runCompleted) return;
        _current.Text = $"Ready: {_profile.Applications.Count} installations, {_profile.Tweaks.Count} tweaks, {_profile.Features.Count} Windows features, {_profile.UninstallApplications.Count} app removals.";
    }

    private void ExitOrCancelRun()
    {
        if (_runCancellation is null) { Close(); return; }
        var answer = MessageBox.Show(this,
            "Setup is currently running. Cancel the remaining work? PSeeSetup will stop safely before Exit closes the window.",
            "Cancel running setup", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (answer == DialogResult.Yes) _runCancellation.Cancel();
    }

    private Button CreateNextButton(int nextTabIndex)
    {
        var next = new Button { Text = NextButtonText, AutoSize = true, Height = 36, MinimumSize = new Size(92, 36), Anchor = AnchorStyles.Right };
        next.Click += (_, _) => _tabs.SelectedIndex = nextTabIndex;
        return next;
    }

    private Control BuildNextFooter(int nextTabIndex)
    {
        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 54,
            Padding = new Padding(10),
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        footer.Controls.Add(CreateNextButton(nextTabIndex));
        return footer;
    }

    private void DrawTab(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _tabs.TabPages.Count) return;
        var selected = e.Index == _tabs.SelectedIndex;
        var background = selected ? Color.White : Color.FromArgb(211, 220, 233);
        var foreground = selected ? Color.FromArgb(27, 54, 93) : Color.FromArgb(78, 91, 110);
        using var brush = new SolidBrush(background);
        e.Graphics.FillRectangle(brush, e.Bounds);
        ControlPaint.DrawBorder(e.Graphics, e.Bounds, Color.FromArgb(166, 177, 193), ButtonBorderStyle.Solid);
        TextRenderer.DrawText(e.Graphics, _tabs.TabPages[e.Index].Text, Font, e.Bounds, foreground,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
            TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
    }

    private void PopulateApplications()
    {
        var search = _appSearch.Text.Trim();
        var category = Convert.ToString(_appCategory.SelectedItem) ?? "All categories";
        var wasUpdatingSelections = _updatingSelections;
        _updatingSelections = true;
        try
        {
            _appsGrid.Rows.Clear();
            foreach (var app in Catalog.Applications.Where(x =>
                         (category == "All categories" || x.Category == category)
                         && (search.Length == 0 || x.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                             || x.Description.Contains(search, StringComparison.OrdinalIgnoreCase))))
            {
                var plan = ArchitecturePolicy.For(app);
                var index = _appsGrid.Rows.Add(_profile.Applications.Contains(app.Key), app.Name, app.Category,
                    plan.Label, app.Description + " " + plan.Reason);
                var row = _appsGrid.Rows[index];
                row.Tag = app;
                row.Cells[0].ReadOnly = !plan.CanInstall;
                if (!plan.CanInstall) row.DefaultCellStyle.ForeColor = Color.DimGray;
            }
        }
        finally { _updatingSelections = wasUpdatingSelections; }
    }

    private void PopulateTweaks()
    {
        var wasUpdatingSelections = _updatingSelections;
        _updatingSelections = true;
        try
        {
            _tweaksGrid.Rows.Clear();
            foreach (var tweak in Catalog.Tweaks)
            {
                var index = _tweaksGrid.Rows.Add(_profile.Tweaks.Contains(tweak.Key), tweak.Name, tweak.Category, tweak.Description);
                _tweaksGrid.Rows[index].Tag = tweak;
            }
        }
        finally { _updatingSelections = wasUpdatingSelections; }
    }

    private void AppsGridCellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_updatingSelections || e.RowIndex < 0 || e.ColumnIndex != 0) return;
        var row = _appsGrid.Rows[e.RowIndex];
        if (row.Tag is not AppDefinition app) return;
        if (!ArchitecturePolicy.For(app).CanInstall) return;
        if (Convert.ToBoolean(row.Cells[0].Value)) _profile.Applications.Add(app.Key); else _profile.Applications.Remove(app.Key);
        RefreshSelectionSummary();
    }

    private void TweaksGridCellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_updatingSelections || e.RowIndex < 0 || e.ColumnIndex != 0) return;
        var row = _tweaksGrid.Rows[e.RowIndex];
        if (row.Tag is not TweakDefinition tweak) return;
        if (Convert.ToBoolean(row.Cells[0].Value)) _profile.Tweaks.Add(tweak.Key); else _profile.Tweaks.Remove(tweak.Key);
        RefreshSelectionSummary();
    }

    private void SetVisibleApplications(bool selected)
    {
        foreach (DataGridViewRow row in _appsGrid.Rows)
        {
            if (row.Tag is not AppDefinition app) continue;
            if (selected && !ArchitecturePolicy.For(app).CanInstall) continue;
            if (selected) _profile.Applications.Add(app.Key); else _profile.Applications.Remove(app.Key);
        }
        PopulateApplications();
        RefreshSelectionSummary();
    }

    private void ApplyProfileToControls()
    {
        var wasUpdatingSelections = _updatingSelections;
        _updatingSelections = true;
        try
        {
            _blockRazer.Checked = _profile.BlockRazerDomains;
            _blockRazer.Enabled = !_profile.ApplicationsOnly;
            _tweaksGrid.Enabled = !_profile.ApplicationsOnly;
            _featuresList.Enabled = !_profile.ApplicationsOnly;
            PopulateApplications();
            PopulateTweaks();
            PopulateUninstalls();
            _uninstallGrid.Enabled = !_profile.ApplicationsOnly;
            _refreshUninstalls.Enabled = !_profile.ApplicationsOnly;
            for (var i = 0; i < _featuresList.Items.Count; i++)
            {
                var feature = (FeatureDefinition)_featuresList.Items[i]!;
                _featuresList.SetItemChecked(i, _profile.Features.Contains(feature.Key));
            }
        }
        finally { _updatingSelections = wasUpdatingSelections; }
        RefreshSelectionSummary();
    }

    private void ImportFailedApplications()
    {
        using var dialog = CreateRetryFileDialog();
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _profile = StateStore.ImportApplicationRetry(dialog.FileName);
            if (_resumeState is not null)
            {
                using var fresh = new MainForm(null, _startupGate);
                fresh._profile = _profile;
                fresh.ApplyProfileToControls();
                fresh.ShowDialog(this);
                return;
            }
            _runCompleted = false;
            _start.Visible = true;
            _start.Enabled = true;
            _start.Text = LiveSetupButtonText;
            ApplyProfileToControls();
        }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Cannot import failed applications", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    public static OpenFileDialog CreateRetryFileDialog() => new()
    {
        Filter = "PSeeSetup run state or profile (*.json)|*.json|All files (*.*)|*.*",
        InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PCSetup", "Runs"),
        Title = "Select a previous run state or a retry application profile",
        DefaultExt = "json",
        CheckFileExists = true
    };

    private TabPage NewPage(string title)
    {
        var page = new TabPage(title) { BackColor = BackColor };
        _tabs.TabPages.Add(page);
        return page;
    }

    private static void ConfigureGrid(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.RowHeadersVisible = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        grid.BackgroundColor = Color.White;
        grid.BorderStyle = BorderStyle.FixedSingle;
    }
}
