// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License.
using System.Drawing.Imaging;
using System.Text.Json;
using NUnit.Framework;
namespace CrestronHomeDevTools.DriverUpdates.Tests;
[TestFixture, Apartment (ApartmentState.STA)]
public sealed class ChecklistTests
    {
    private static readonly IReadOnlyDictionary<int, string> Rooms = new Dictionary<int, string> { [1] = "Lounge", [2] = "Office" };
    private static AvailableDriverUpdate Driver (string id, string status = "UpdateAvailable") => new (id, id, "Example", "Example", "2.0.0.0", "Available", status, "Review all affected instances.",
        new () { InstalledDriverVersion = "1.0.0.0", AvailableDriverVersion = "2.0.0.0", IsSupportsSwapDriver = true, IsSwapDriverRequiresReboot = status == "RebootRequired", EligibleDeviceIds = [10, 11] },
        [new (10, "TV", id, 1, null, "1.0.0.0"), new (11, "Display", id, 2, null, "1.0.0.0")]);
    private static DriverUpdateReport Report () => new (1, new ("192.0.2.10", 443, 49000, new string ('A', 64)), DateTimeOffset.UtcNow,
        [Driver ("Streaming player", "RebootRequired"), Driver ("Lighting platform"), Driver ("Television", "Current"), Driver ("Legacy device", "Unknown")],
        [new (99, "Child shade", "Platform child", 1, 8, "1.0.0.0")]);

    [Test]
    public void SelectAllOnlyChecksConfirmedEligibleRows ()
        {
        using var window = new MainWindow ();
        window.ShowReport (Report (), Rooms);
        window.SetAllSelected (true);
        Assert.That (window.Rows.Where (r => r.Selected).Select (r => r.Driver), Is.EqualTo (new[] { "Streaming player", "Lighting platform" }));
        foreach (var row in window.Rows.Where (r => !r.CanSelect)) { row.Selected = true; Assert.That (row.Selected, Is.False); }
        window.SetAllSelected (false);
        Assert.That (window.Rows.Any (r => r.Selected), Is.False);
        Assert.That (window.Rows[0].Rooms, Is.EqualTo ("Lounge, Office"));
        Assert.That (window.Rows[0].Devices, Is.EqualTo ("TV, Display"));
        }

    [TestCase (true)]
    [TestCase (false)]
    public void ConfirmationRequiresRestartConsentOnlyWhenNeeded (bool restart)
        {
        using var window = new ConfirmationWindow ("192.0.2.10", [UpdateRow.From (Driver ("Streaming player", restart ? "RebootRequired" : "UpdateAvailable"), Rooms)]);
        var confirm = (Button)window.AcceptButton!;
        Assert.That (confirm.Enabled, Is.EqualTo (!restart));
        Assert.That (((Button)window.CancelButton!).DialogResult, Is.EqualTo (DialogResult.Cancel));
        var consent = Controls (window).OfType<CheckBox> ().Single ();
        consent.Checked = true; Assert.That (confirm.Enabled, Is.True);
        consent.Checked = false; Assert.That (confirm.Enabled, Is.EqualTo (!restart));
        Assert.That (Controls (window).OfType<ListBox> ().Single ().Items[0]!.ToString (), Does.Contain ("TV, Display"));
        }

    [Test]
    public void FailedUpdateDoesNotClaimNewInstalledVersion ()
        {
        var row = UpdateRow.From (Driver ("Failed device"), Rooms);
        row.Selected = true;
        row.Finish (new (row.DriverId, "Failed", "operation", "Processor reported failure."), false);
        Assert.That (row.Installed, Is.EqualTo ("1.0.0.0"));
        Assert.That (row.Result, Is.EqualTo ("Failed"));
        Assert.That (row.Selected || row.CanSelect, Is.False);
        }

    [TestCase (true)]
    [TestCase (false)]
    public async Task InterruptedCleanupPreservesCompletedAndUnconfirmedRows (bool resultAvailable)
        {
        var directory = Path.Combine (TestContext.CurrentContext.WorkDirectory, "desktop-results-" + Guid.NewGuid ().ToString ("N"));
        Directory.CreateDirectory (directory);
        try
            {
            var first = UpdateRow.From (Driver ("first"), Rooms);
            var second = UpdateRow.From (Driver ("second"), Rooms);
            var third = UpdateRow.From (Driver ("third"), Rooms);
            var complete = new DriverUpdateStep ("first", "Updated", "a", "Verified.");
            await File.WriteAllTextAsync (Path.Combine (directory, "000-completed.json"), JsonSerializer.Serialize (complete));
            await File.WriteAllTextAsync (Path.Combine (directory, "001-intent.json"), JsonSerializer.Serialize (second.Source));
            if (resultAvailable) await File.WriteAllTextAsync (Path.Combine (directory, "result.json"), JsonSerializer.Serialize (
                new DriverUpdateBatchResult ("Stopped", false, [complete, new ("second", "Unconfirmed", "b", "Timed out.")])));
            await UpdateResults.RestoreAsync ([first, second, third], directory);
            Assert.That (first.Result, Is.EqualTo ("Updated"));
            Assert.That (first.Installed, Is.EqualTo ("2.0.0.0"));
            Assert.That (second.Result, Is.EqualTo ("Needs inspection"));
            Assert.That (second.Installed, Is.EqualTo ("1.0.0.0"));
            Assert.That (third.Result, Is.EqualTo ("Not started"));
            }
        finally { Directory.Delete (directory, true); }
        }

    [Test]
    public async Task BusyProcessorBeforeJournalCreationLeavesAllUpdatesNotStarted ()
        {
        var row = UpdateRow.From (Driver ("first"), Rooms);
        row.SetProgress ("Queued", "Waiting");
        await UpdateResults.RestoreAsync ([row], Path.Combine (TestContext.CurrentContext.WorkDirectory, Guid.NewGuid ().ToString ("N")));
        Assert.That (row.Result, Is.EqualTo ("Not started"));
        }

    [Test]
    public void ChecklistRendersAllColumnsInInvisibleTestWindow ()
        {
        using var window = new MainWindow ();
        window.ShowReport (Report (), Rooms);
        window.SetAllSelected (true);
        window.BindingContext = new BindingContext ();
        window.Checklist.BindingContext = window.BindingContext;
        window.ShowInTaskbar = false;
        window.Opacity = 0;
        window.Show ();
        window.PerformLayout ();
        using var bitmap = new Bitmap (window.Width, window.Height);
        window.DrawToBitmap (bitmap, new Rectangle (Point.Empty, bitmap.Size));
        var path = Path.Combine (TestContext.CurrentContext.WorkDirectory, "driver-updates-checklist.png");
        bitmap.Save (path, ImageFormat.Png);
        window.Hide ();
        TestContext.AddTestAttachment (path);
        Assert.That (window.Checklist.Columns.Count, Is.EqualTo (9));
        Assert.That (window.Checklist.Rows.Count, Is.EqualTo (5));
        Assert.That (window.Visible, Is.False);
        }
    private static IEnumerable<Control> Controls (Control parent) => parent.Controls.Cast<Control> ().SelectMany (c => new[] { c }.Concat (Controls (c)));
    }
