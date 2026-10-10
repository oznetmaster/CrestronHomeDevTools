// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License.
namespace CrestronHomeDevTools.DriverUpdates;
internal sealed class ConfirmationWindow : Form
    {
    public ConfirmationWindow (string host, UpdateRow[] rows)
        {
        Text = "Confirm driver updates"; StartPosition = FormStartPosition.CenterParent;
        ClientSize = new (720, 450); MinimumSize = new (620, 380); Font = new ("Segoe UI", 10);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new (20), RowCount = 4, ColumnCount = 1 };
        layout.RowStyles.Add (new (SizeType.AutoSize)); layout.RowStyles.Add (new (SizeType.Percent, 100));
        layout.RowStyles.Add (new (SizeType.AutoSize)); layout.RowStyles.Add (new (SizeType.AutoSize));
        bool restart = rows.Any (r => r.Source!.Status == "RebootRequired");
        layout.Controls.Add (new Label { AutoSize = true, MaximumSize = new (670, 0), Text = $"Update {rows.Length} driver(s) on {host}? All affected instances listed below will be updated." }, 0, 0);
        var list = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true, IntegralHeight = false, Margin = new (0, 16, 0, 16) };
        foreach (var row in rows) list.Items.Add ($"{row.Driver}: {row.Installed} → {row.Available}  |  {row.Devices}" + (row.Restart == "Required" ? "  [RESTART]" : ""));
        layout.Controls.Add (list, 0, 1);
        var consent = new CheckBox { AutoSize = true, MaximumSize = new (670, 0), Visible = restart,
            Text = "Allow the required processor restarts. Home control will be unavailable during each restart." };
        layout.Controls.Add (consent, 0, 2);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Margin = new (0, 16, 0, 0) };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        var confirm = new Button { Text = "Update drivers", AutoSize = true, Enabled = !restart, DialogResult = DialogResult.OK };
        consent.CheckedChanged += (_, _) => confirm.Enabled = !restart || consent.Checked;
        buttons.Controls.Add (cancel); buttons.Controls.Add (confirm); layout.Controls.Add (buttons, 0, 3);
        Controls.Add (layout); CancelButton = cancel; AcceptButton = confirm;
        }
    }
