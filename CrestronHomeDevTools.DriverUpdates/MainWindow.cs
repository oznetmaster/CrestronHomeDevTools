// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License.
using System.ComponentModel;
using System.Diagnostics;
namespace CrestronHomeDevTools.DriverUpdates;
internal sealed class MainWindow : Form
    {
    private readonly ComboBox _profiles = new () { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
    private readonly TextBox _processor = new () { Width = 220, PlaceholderText = "Use saved processor" };
    private readonly Button _scan = new () { Text = "Scan drivers", AutoSize = true };
    private readonly Button _update = new () { Text = "Update selected", AutoSize = true, Enabled = false };
    private readonly CheckBox _selectAll = new () { Text = "Select all eligible updates", AutoSize = true, Enabled = false };
    private readonly Button _results = new () { Text = "Open run folder", AutoSize = true, Enabled = false };
    private readonly Label _summary = new () { AutoSize = true, Text = "Choose a saved profile and scan the processor." };
    private readonly Label _status = new () { Dock = DockStyle.Fill, AutoSize = true, Text = "Ready" };
    private readonly TextBox _detail = new () { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Color.FromArgb (243, 246, 249), ScrollBars = ScrollBars.Vertical };
    private readonly DataGridView _grid = new () { Dock = DockStyle.Fill, AutoGenerateColumns = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
        RowHeadersVisible = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, BackgroundColor = Color.White, BorderStyle = BorderStyle.None };
    private readonly BindingList<UpdateRow> _rows = [];
    private DriverUpdateReport? _report;
    private UpdateSession? _session;
    private bool _busy, _updating, _selecting;
    private string? _journal;
    internal DataGridView Checklist => _grid;
    internal BindingList<UpdateRow> Rows => _rows;
    public MainWindow ()
        {
        Text = "Crestron Home Driver Updates"; StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new (1320, 720); MinimumSize = new (1000, 550); AutoScaleMode = AutoScaleMode.Dpi;
        Font = new ("Segoe UI", 10); BackColor = Color.FromArgb (243, 246, 249);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new (22), ColumnCount = 1, RowCount = 7 };
        layout.ColumnStyles.Add (new (SizeType.Percent, 100));
        foreach (var height in new[] { 45, 45, 38, -1, 80, 44, 30 }) layout.RowStyles.Add (height < 0 ? new (SizeType.Percent, 100) : new (SizeType.Absolute, height));
        layout.Controls.Add (new Label { Text = "Driver updates", AutoSize = true, Font = new ("Segoe UI", 22, FontStyle.Bold), ForeColor = Color.FromArgb (24, 47, 70) }, 0, 0);
        var connection = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        connection.Controls.Add (new Label { Text = "Profile", AutoSize = true, Margin = new (0, 7, 8, 0) }); connection.Controls.Add (_profiles);
        connection.Controls.Add (new Label { Text = "Processor", AutoSize = true, Margin = new (18, 7, 8, 0) }); connection.Controls.Add (_processor);
        connection.Controls.Add (_scan); layout.Controls.Add (connection, 0, 1);
        layout.Controls.Add (_summary, 0, 2);
        _grid.EnableHeadersVisualStyles = false; _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing; _grid.ColumnHeadersHeight = 38;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb (224, 233, 240); _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb (24, 47, 70);
        _grid.RowTemplate.Height = 38; _grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb (247, 249, 251);
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb (218, 235, 249); _grid.DefaultCellStyle.SelectionForeColor = Color.Black;
        _grid.Columns.Add (new DataGridViewCheckBoxColumn { DataPropertyName = nameof (UpdateRow.Selected), HeaderText = "", Width = 38, Name = "Selected" });
        AddColumn (nameof (UpdateRow.Driver), "Driver", 195); AddColumn (nameof (UpdateRow.Devices), "Devices", 160); AddColumn (nameof (UpdateRow.Rooms), "Rooms", 115);
        AddColumn (nameof (UpdateRow.Installed), "Installed", 105); AddColumn (nameof (UpdateRow.Available), "Available", 105);
        AddColumn (nameof (UpdateRow.Status), "Status", 140); AddColumn (nameof (UpdateRow.Restart), "Restart", 90); AddColumn (nameof (UpdateRow.Result), "Result", 200);
        _grid.DataSource = _rows; layout.Controls.Add (_grid, 0, 3); _detail.Margin = new (0, 14, 0, 8); layout.Controls.Add (_detail, 0, 4);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        _selectAll.Margin = new (0, 8, 20, 0); actions.Controls.Add (_selectAll); actions.Controls.Add (_update); actions.Controls.Add (_results); layout.Controls.Add (actions, 0, 5);
        layout.Controls.Add (_status, 0, 6); Controls.Add (layout);
        _grid.CurrentCellDirtyStateChanged += (_, _) => { if (_grid.IsCurrentCellDirty) _grid.CommitEdit (DataGridViewDataErrorContexts.Commit); };
        _grid.CellBeginEdit += (_, e) => { if (_busy || !_rows[e.RowIndex].CanSelect) e.Cancel = true; };
        _grid.SelectionChanged += (_, _) => { if (_grid.CurrentRow?.DataBoundItem is UpdateRow row) _detail.Text = row.Detail; };
        _grid.CellFormatting += (_, e) =>
            {
            if (e.RowIndex < 0) return;
            var row = _rows[e.RowIndex];
            if (e.ColumnIndex == 0 && !row.CanSelect) e.CellStyle!.ForeColor = Color.Gray;
            if (_grid.Columns[e.ColumnIndex].DataPropertyName == nameof (UpdateRow.Result))
                e.CellStyle!.ForeColor = row.Result == "Updated" ? Color.DarkGreen : row.Result is "Failed" or "Needs inspection" ? Color.Firebrick : Color.FromArgb (24, 47, 70);
            };
        _rows.ListChanged += (_, _) => SelectionChanged ();
        _selectAll.CheckedChanged += (_, _) => { if (_selecting) return; SetAllSelected (_selectAll.Checked); };
        _scan.Click += async (_, _) => await ScanAsync ();
        _update.Click += async (_, _) => await UpdateAsync ();
        _results.Click += (_, _) => { if (_journal != null) { try { Process.Start (new ProcessStartInfo (_journal) { UseShellExecute = true }); } catch { _status.Text = "Unable to open the run folder: " + _journal; } } };
        _processor.TextChanged += (_, _) => InvalidateScan ();
        _profiles.SelectedIndexChanged += (_, _) => { _processor.Clear (); InvalidateScan (); };
        FormClosing += (_, e) => { if (_busy) { e.Cancel = true; _status.Text = _updating ? "Updates are being monitored. Wait for the result before closing." : "Please wait for the scan to finish."; } };
        try { _profiles.Items.AddRange (UpdateSession.Profiles ()); if (_profiles.Items.Count > 0) _profiles.SelectedIndex = Math.Max (0, _profiles.Items.IndexOf ("default")); }
        catch { _status.Text = "Saved profiles could not be read under this Windows account."; }
        if (_profiles.Items.Count == 0) { _scan.Enabled = false; _status.Text = "No saved profiles. Run DevTools Console configure first, then reopen this app."; }
        }
    private void AddColumn (string property, string title, int width) => _grid.Columns.Add (new DataGridViewTextBoxColumn { DataPropertyName = property, HeaderText = title, Width = width, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
    private void InvalidateScan () { _report = null; _session = null; _selectAll.Enabled = _update.Enabled = false; if (_rows.Count > 0) _status.Text = "Connection changed. Scan again before selecting updates."; }
    internal void ShowReport (DriverUpdateReport report, IReadOnlyDictionary<int, string> rooms)
        {
        _report = report; _rows.Clear ();
        foreach (var row in report.Drivers) _rows.Add (UpdateRow.From (row, rooms));
        foreach (var device in report.UnresolvedDevices) _rows.Add (UpdateRow.Unresolved (device, rooms));
        _summary.Text = $"{report.Target.Host}  •  {_rows.Count} entries  •  {_rows.Count (r => r.CanSelect)} eligible updates  •  {report.UnresolvedDevices.Length} unresolved instances";
        SelectionChanged ();
        }
    internal void SetAllSelected (bool value)
        {
        _selecting = true; foreach (var row in _rows) row.Selected = value; _selecting = false; SelectionChanged ();
        }
    private void SelectionChanged ()
        {
        if (_selecting) return;
        _selecting = true;
        var eligible = _rows.Where (r => r.CanSelect).ToArray (); int count = eligible.Count (r => r.Selected);
        _selectAll.CheckState = count == 0 ? CheckState.Unchecked : count == eligible.Length ? CheckState.Checked : CheckState.Indeterminate;
        _selectAll.Enabled = !_busy && _report != null && eligible.Length > 0;
        _update.Enabled = !_busy && _session?.CanApply == true && _report != null && count > 0;
        _update.Text = count > 0 ? $"Update selected ({count})" : "Update selected"; _selecting = false;
        }
    private void Busy (bool value)
        {
        _busy = value; _profiles.Enabled = _processor.Enabled = !value; _scan.Enabled = !value && _profiles.Items.Count > 0;
        SelectionChanged ();
        }
    private async Task ScanAsync ()
        {
        Busy (true); _status.Text = "Reading installed drivers and checking the catalogue…"; _report = null; _session = null;
        try
            {
            var session = await UpdateSession.LoadAsync (_profiles.Text, _processor.Text);
            var scan = await session.ScanAsync (); _session = session; ShowReport (scan.Report, scan.Rooms);
            _status.Text = session.CanApply ? "Scan complete. Select updates and review the confirmation." : "Scan complete. This profile needs verified SSH trust before updates can be applied.";
            }
        catch (Exception e) { _status.Text = Friendly (e); }
        finally { Busy (false); }
        }
    private async Task UpdateAsync ()
        {
        _grid.EndEdit ();
        if (_session == null || _report == null) return;
        var selected = _rows.Where (r => r.Selected && r.CanSelect).ToArray (); if (selected.Length == 0) return;
        using var confirmation = new ConfirmationWindow (_session.Target.Host, selected);
        if (confirmation.ShowDialog (this) != DialogResult.OK) return;
        _updating = true; Busy (true);
        _journal = Path.Combine (Environment.GetFolderPath (Environment.SpecialFolder.LocalApplicationData), "CrestronHomeDevTools", "DriverUpdates", DateTime.UtcNow.ToString ("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid ().ToString ("N"));
        _results.Enabled = false; _status.Text = "Reserving processor and rechecking selected updates…";
        foreach (var row in selected) row.SetProgress ("Queued", "Waiting for the preceding driver update to finish.");
        try
            {
            var progress = new Progress<DriverUpdateProgress> (p =>
                {
                if (!_updating) return;
                var row = selected.SingleOrDefault (r => r.DriverId == p.DriverId); row?.SetProgress (p.State, p.Detail);
                _status.Text = (row?.Driver ?? "Driver") + ": " + p.Detail;
                if (_grid.CurrentRow?.DataBoundItem == row) _detail.Text = p.Detail;
                });
            var batch = await _session.ApplyAsync (_report, selected.Select (r => r.DriverId).ToArray (), selected.Any (r => r.Restart == "Required"), _journal, progress);
            _updating = false;
            foreach (var row in selected) row.Finish (batch.Steps.SingleOrDefault (s => s.DriverId == row.DriverId), !batch.SafeToReleaseReservation);
            _status.Text = batch.State == "Completed" ? $"Completed: {batch.Steps.Count (s => s.State == "Updated")} drivers updated and verified."
                : batch.SafeToReleaseReservation ? "Batch stopped. Results are shown for each driver; later updates were not started."
                : "Outcome needs inspection. Processor reservation retained; open the run folder before any retry.";
            }
        catch (Exception e)
            {
            _updating = false;
            await UpdateResults.RestoreAsync (selected, _journal);
            _status.Text = Friendly (e) + " No automatic retry. Review the run folder.";
            }
        finally { _updating = false; _report = null; _results.Enabled = Directory.Exists (_journal); Busy (false);
            if (_grid.CurrentRow?.DataBoundItem is UpdateRow row) _detail.Text = row.Detail; }
        }
    private static string Friendly (Exception exception) => exception switch
        {
        ProcessorBusyException => "Processor busy with another reserved operation. No update started.",
        ArgumentException => exception.Message,
        FileNotFoundException => "Saved profile or required file is missing. Configure the profile in the DevTools console.",
        _ => $"Operation could not be confirmed ({exception.GetType ().Name}). Check the connection, saved profile and run evidence."
        };
    }
