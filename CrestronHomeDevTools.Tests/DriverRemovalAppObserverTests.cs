// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text;
using System.Xml.Linq;
using CrestronHomeDevTools.Automation;
using CrestronHomeNUnit.Android;
using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class DriverRemovalAppObserverTests
{
    private string folder = null!;
    private static readonly DriverRemovalDevice[] Tree = [new(1, -6, "Gateway", "Platform", 10, "1.0", "Loaded"), new(2, 1, "Demo", "Child", 10, null, null)];
    private DriverRemovalAppPlan Plan => new(new("C:/adb.exe", "emulator-5554", "com.crestron.phoenix.app", "Test Home", "C:/private/lock"), [new(2, "Demo", 10, "Office", false)], [1]);
    [SetUp] public void SetUp() { folder = Path.Combine(TestContext.CurrentContext.WorkDirectory, "removal-ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder); }
    [TearDown] public void TearDown() => Directory.Delete(folder, true);

    [TestCase(false, true, true)]
    [TestCase(false, false, false)]
    [TestCase(true, false, true)]
    [TestCase(true, true, false)]
    public async Task ScansBeyondFirstScreenAndRestoresHome(bool removed, bool tileExists, bool pass)
    {
        var adb = new FakeAdb { TileExists = tileExists };
        var observer = new DriverRemovalAppObserver(Plan, _ => Task.CompletedTask, adb);
        var result = await observer.ObserveAsync(Tree, removed, folder);
        Assert.That(result.Passed, Is.EqualTo(pass)); Assert.That(result.HomeRestored, Is.True);
        Assert.That(adb.Page, Is.EqualTo("home")); Assert.That(adb.SawSecondRoomPage, Is.True);
        Assert.That(File.Exists(Path.Combine(folder, "home-restored", "screen.png")), Is.True);
        Assert.That(File.ReadAllText(Path.Combine(folder, "room-0-summary.json")), Does.Contain("FullVerticalTraversal"));
    }

    [Test]
    [TestCase(false, true, true)]
    [TestCase(true, true, false)]
    [TestCase(true, false, true)]
    public async Task NativeLightsAreObservedInTheirListWithoutTouchingControls(bool removed, bool exists, bool pass)
    {
        var adb = new FakeAdb { TileExists = exists, NativeMode = true };
        var nativePlan = Plan with { Tiles = [Plan.Tiles[0] with { NativeLight = true }] };
        var result = await new DriverRemovalAppObserver(nativePlan, _ => Task.CompletedTask, adb).ObserveAsync(Tree, removed, folder);
        Assert.That(result.Passed, Is.EqualTo(pass)); Assert.That(result.HomeRestored, Is.True);
        Assert.That(adb.SawLightList, Is.True);
        Assert.That(File.Exists(Path.Combine(folder, "lights-0-summary.json")), Is.True);
    }

    [Test]
    public void EveryDescendantMustBeAccountedFor()
    {
        Assert.Throws<InvalidDataException>(() => DriverRemovalAppObserver.ValidateScope(Plan, [..Tree, new(3, 1, "Unexpected", "Child", 10, null, null)]));
        Assert.Throws<InvalidDataException>(() => DriverRemovalAppObserver.ValidateScope(Plan with { NonvisualDeviceIds = [1, 2] }, Tree));
        Assert.Throws<InvalidDataException>(() => DriverRemovalAppObserver.ValidateScope(Plan with { Tiles = [new(2, "Wrong", 10, "Office", false)] }, Tree));
    }

    [Test]
    public void LostOwnershipSendsNoAppInput()
    {
        var adb = new FakeAdb();
        var observer = new DriverRemovalAppObserver(Plan, _ => throw new IOException("Owner lost"), adb);
        Assert.ThrowsAsync<IOException>(async () => await observer.ObserveAsync(Tree, false, folder));
        Assert.That(adb.Inputs, Is.Zero);
    }

    [Test]
    public void UncertainGestureIsNotRepeated()
    {
        var adb = new FakeAdb { FailInput = true };
        var observer = new DriverRemovalAppObserver(Plan, _ => Task.CompletedTask, adb);
        Assert.ThrowsAsync<IOException>(async () => await observer.ObserveAsync(Tree, false, folder));
        Assert.That(adb.Inputs, Is.EqualTo(1));
        Assert.That(File.Exists(Path.Combine(folder, "outcome.json")), Is.False);
    }

    private sealed class FakeAdb : IAndroidCommandTransport
    {
        public string Page = "home";
        public bool TileExists, SawSecondRoomPage, FailInput, NativeMode, SawLightList;
        public int Inputs;
        private int roomPosition;
        public Task<byte[]> ExecuteAsync(IReadOnlyList<string> args, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (args[0] == "exec-out") return Task.FromResult(new byte[] {137,80,78,71,13,10,26,10});
            if (args[1] == "uiautomator") return Task.FromResult(Encoding.UTF8.GetBytes("UI hierarchy dumped to: test.xml"));
            if (args[1] == "cat") return Task.FromResult(Encoding.UTF8.GetBytes(Xml()));
            if (args[1] == "rm") return Task.FromResult(Array.Empty<byte>());
            if (args[1] != "input") throw new AssertionException("Unexpected transport command");
            Inputs++; if (FailInput) throw new IOException("Input outcome unknown");
            if (args[2] == "swipe")
            {
                if (Page == "room") roomPosition = int.Parse(args[4]) > int.Parse(args[6]) ? 1 : 0;
            }
            else if (args[2] == "tap")
            {
                int x = int.Parse(args[3]), y = int.Parse(args[4]);
                if (y >= 900 && Page is "home" or "rooms") Page = x > 250 ? "rooms" : "home";
                else if (Page == "rooms" && y < 900) { Page = "room"; roomPosition = 0; }
                else if (Page == "room" && NativeMode && y >= 200 && y <= 280) { Page = "lights"; SawLightList = true; }
                else if (Page == "room" && y < 50) Page = "rooms";
                else if (Page == "lights" && y < 100) Page = "room";
                else throw new AssertionException("Observer must not tap physical device controls");
            }
            else throw new AssertionException("Unexpected input");
            return Task.FromResult(Array.Empty<byte>());
        }
        private string Xml()
        {
            const string prefix = "com.crestron.phoenix.app:id/";
            XElement N(string id, string text = "", string bounds = "[0,50][500,850]", params XElement[] children) => new("node",
                new XAttribute("package", "com.crestron.phoenix.app"), new XAttribute("resource-id", id == "" ? "" : prefix + id),
                new XAttribute("text", text), new XAttribute("bounds", bounds), new XAttribute("enabled", "true"), new XAttribute("clickable", "true"), children);
            var tabs = N("bottomNavigationView", "", "[0,900][500,1000]",
                N("", "", "[0,900][250,1000]", N("itemBottomNavigationIcon")),
                N("", "", "[250,900][500,1000]", N("itemBottomNavigationIcon")));
            var nodes = new List<XElement> { tabs };
            if (Page == "home") nodes.AddRange([N("home_wholeHouse_name", "Test Home", "[0,0][500,50]"), N("home_scrollView")]);
            if (Page == "rooms") nodes.AddRange([N("fragmentRoomsTitle", "Rooms", "[0,0][500,50]"), N("rooms_roomsList", "", "[0,50][500,900]", N("itemRoomTitle", "Office", "[0,60][500,150]"))]);
            if (Page == "room")
            {
                if (roomPosition == 1) SawSecondRoomPage = true;
                nodes.AddRange([N("room_name", "Office", "[0,0][500,50]"), N("room_back", "", "[0,0][50,50]"),
                    N("room_scrollView", "", "[0,50][500,900]", N("row" + roomPosition, roomPosition == 1 && TileExists && !NativeMode ? "Demo" : "Other"))]);
                if (NativeMode) nodes.Last().Add(N("twoButtonsTile_title", "Lights", "[0,200][500,280]"));
            }
            if (Page == "lights")
            {
                var content = N("lights_lightsDetails_content", "", "[0,100][500,850]", N("lights_lightsDetails_lightLoadTitle", TileExists ? "Demo" : "Other"));
                content.Add(new XAttribute("scrollable", "false"));
                nodes.AddRange([N("lights_lightsDetails_title", "Lights", "[0,0][200,50]"), N("lights_lightsDetails_subtitle", "OFFICE", "[0,50][200,90]"), N("lights_lightsDetails_closeButton", "", "[400,0][500,90]"), content]);
            }
            return new XElement("hierarchy", nodes).ToString(SaveOptions.DisableFormatting);
        }
    }
}
