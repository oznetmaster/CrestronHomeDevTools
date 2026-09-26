// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using CrestronHomeNUnit.Android;

namespace CrestronHomeDevTools.Automation;

public sealed record DriverRemovalAppTile(int DeviceId, string Name, int LocationId, string RoomName, bool OnHome)
{
    public bool NativeLight { get; init; }
    /// <summary>Automation-only alias resolved from retained managed-child creation receipts before observation.</summary>
    public string? ManagedAlias { get; init; }
}
/// <summary>Nonvisual IDs must be explicitly reviewed; no child is silently excluded.</summary>
public sealed record DriverRemovalAppPlan(AndroidSessionProfile Profile, DriverRemovalAppTile[] Tiles, int[] NonvisualDeviceIds)
{
    public string[] NonvisualManagedAliases { get; init; } = [];
    public bool IncludeDeployedPlatformAsNonvisual { get; init; }
}

/// <summary>Observes Home and Room tile lists without opening device controls. Caller owns both reservations.</summary>
public sealed class DriverRemovalAppObserver
{
    private readonly DriverRemovalAppPlan plan;
    private readonly IAndroidCommandTransport transport;
    private readonly AndroidDevice device;
    private readonly Func<CancellationToken, Task> verifyOwnership;
    private const string App = "com.crestron.phoenix.app";
    private const string Prefix = CrestronHomePages.ResourcePrefix;
    private static AndroidSelector Id(string id) => CrestronHomePages.Resource(id);

    public DriverRemovalAppObserver(DriverRemovalAppPlan plan, Func<CancellationToken, Task> verifyOwnership)
        : this(plan, verifyOwnership, new AdbCommandTransport(plan.Profile.AdbExecutable, plan.Profile.DeviceSerial, TimeSpan.FromSeconds(25))) { }
    internal DriverRemovalAppObserver(DriverRemovalAppPlan plan, Func<CancellationToken, Task> verifyOwnership, IAndroidCommandTransport transport)
    {
        this.plan = plan; this.verifyOwnership = verifyOwnership; this.transport = transport;
        if (plan.Profile.Application != App) throw new ArgumentException("This observer requires the Crestron Home app.");
        device = new(transport, App);
    }

    internal static void ValidateScope(DriverRemovalAppPlan plan, IReadOnlyList<DriverRemovalDevice> selected)
    {
        if (plan.NonvisualManagedAliases.Length != 0 || plan.IncludeDeployedPlatformAsNonvisual || plan.Tiles.Any(t => t.ManagedAlias != null) ||
            plan.Tiles.Length == 0 || plan.Tiles.Length > 100 || plan.NonvisualDeviceIds.Length > 4096 ||
            plan.Tiles.Any(t => t.DeviceId <= 0 || t.LocationId <= 0 || string.IsNullOrWhiteSpace(t.Name) || string.IsNullOrWhiteSpace(t.RoomName)) ||
            plan.Tiles.Select(t => (t.RoomName, t.Name)).Distinct().Count() != plan.Tiles.Length ||
            plan.Tiles.Where(t => t.OnHome).Select(t => t.Name).Distinct().Count() != plan.Tiles.Count(t => t.OnHome))
            throw new InvalidDataException("Provide unambiguous explicit tile expectations.");
        var ids = plan.Tiles.Select(t => t.DeviceId).Concat(plan.NonvisualDeviceIds).ToArray();
        if (ids.Distinct().Count() != ids.Length || !ids.Order().SequenceEqual(selected.Select(d => d.Id).Order()))
            throw new InvalidDataException("Every selected root/descendant must have a tile expectation or explicit nonvisual declaration.");
        foreach (var tile in plan.Tiles)
        {
            var actual = selected.Single(d => d.Id == tile.DeviceId);
            if (actual.Name != tile.Name || actual.LocationId != tile.LocationId)
                throw new InvalidDataException("App tile expectations differ from the selected device identity.");
        }
    }

    public async Task<DriverRemovalUiOutcome> ObserveAsync(IReadOnlyList<DriverRemovalDevice> selected, bool removed,
        string evidenceDirectory, CancellationToken token = default)
    {
        ValidateScope(plan, selected);
        if (!Path.IsPathFullyQualified(evidenceDirectory) || !Directory.Exists(evidenceDirectory) || Directory.EnumerateFileSystemEntries(evidenceDirectory).Any())
            throw new InvalidDataException("Use an empty private evidence directory.");
        await verifyOwnership(token);
        await Write("plan.json", plan);
        bool passed = true;
        string? room = null;
        try
        {
            CrestronHomePages.RequireHome(await Read(token), plan.Profile.ExpectedHomeText);
            var allNames = plan.Tiles.Select(t => t.Name).Distinct(StringComparer.Ordinal).ToArray();
            var homeNames = removed ? [] : plan.Tiles.Where(t => t.OnHome).Select(t => t.Name).Distinct(StringComparer.Ordinal).ToArray();
            passed &= await Scan("home", null, allNames, homeNames, evidenceDirectory, token);
            foreach (var group in plan.Tiles.GroupBy(t => t.RoomName))
            {
                await Tab(true, token);
                room = group.Key;
                await FindRoom(room, token);
                await device.TapAsync(new(AndroidSelectorKind.Text, room), h => { CrestronHomePages.RequireRooms(h); RequireVisibleRoomChoice(h, room); }, token);
                await Wait(h => CrestronHomePages.RequireRoom(h, room), token);
                passed &= await Scan("room-" + Array.IndexOf(plan.Tiles, group.First()).ToString(CultureInfo.InvariantCulture), room,
                    allNames, removed ? [] : group.Where(t => !t.NativeLight).Select(t => t.Name).ToArray(), evidenceDirectory, token);
                if (group.Any(t => t.NativeLight))
                {
                    string label = "lights-" + Array.IndexOf(plan.Tiles, group.First()).ToString(CultureInfo.InvariantCulture);
                    if (await FindLights(room, token))
                    {
                        await device.TapAsync(new(AndroidSelectorKind.Text, "Lights") { AncestorResourceId = Prefix + "room_scrollView" },
                            h => { CrestronHomePages.RequireRoom(h, room); RequireLightsChoice(h, room); }, token);
                        await Wait(h => Lights(h, room), token);
                        passed &= await Scan(label, room, allNames, removed ? [] : group.Where(t => t.NativeLight).Select(t => t.Name).ToArray(), evidenceDirectory, token, true);
                        await device.TapAsync(Id("lights_lightsDetails_closeButton"), h => Lights(h, room), token);
                        await Wait(h => CrestronHomePages.RequireRoom(h, room), token);
                    }
                    else
                    {
                        passed &= removed;
                        await Capture(Path.Combine(evidenceDirectory, label + "-unavailable"), h => CrestronHomePages.RequireRoom(h, room), token);
                    }
                }
                await Restore(room, token); room = null;
            }
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException)
        {
            passed = false;
            await Write("observation-failure.json", new { ErrorType = error.GetType().Name });
        }
        // Only known navigation pages can be restored. Uncertain ADB/lease failures propagate.
        await Restore(room, token);
        await Capture(Path.Combine(evidenceDirectory, "home-restored"), h => CrestronHomePages.RequireHome(h, plan.Profile.ExpectedHomeText), token);
        var result = new DriverRemovalUiOutcome(passed, true);
        await Write("outcome.json", result);
        return result;

        Task Write(string name, object value) => File.WriteAllTextAsync(Path.Combine(evidenceDirectory, name), JsonSerializer.Serialize(value), token);
    }

    private async Task<AndroidHierarchy> Read(CancellationToken token) { await verifyOwnership(token); return await device.CaptureAsync(token); }
    private async Task Wait(Action<AndroidHierarchy> guard, CancellationToken token)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token); limit.CancelAfter(TimeSpan.FromSeconds(15));
        while (true)
        {
            var h = await Read(limit.Token);
            try { guard(h); return; }
            catch (InvalidOperationException) { await Task.Delay(200, limit.Token); }
        }
    }
    private static IEnumerable<XElement> Nodes(AndroidHierarchy h) => XDocument.Parse(h.MaskedXml).Descendants("node").Where(n => (string?)n.Attribute("package") == App);
    private static string Signature(AndroidHierarchy h, string containerId) => string.Join("\n", Nodes(h)
        .Where(n => (string?)n.Attribute("resource-id") == Prefix + containerId).Single().Descendants("node")
        .Select(n => string.Join("|", (string?)n.Attribute("resource-id"), (string?)n.Attribute("content-desc"), (string?)n.Attribute("bounds"))));
    private static AndroidElement Viewport(AndroidHierarchy h, string? room, bool rooms = false)
    {
        string id = rooms ? "rooms_roomsList" : room == null ? "home_scrollView" : "room_scrollView";
        var box = h.RequireUnique(Id(id)); var bar = h.RequireUnique(Id("bottomNavigationView"));
        int top = room != null ? Math.Max(box.Top, h.RequireUnique(Id("room_back")).Bottom) : box.Top;
        if (rooms) top = Math.Max(top, h.RequireUnique(Id("fragmentRoomsTitle")).Bottom);
        int bottom = Math.Min(box.Bottom, bar.Top);
        if (!box.Enabled || bottom - top < 100 || box.Right - box.Left < 80) throw new InvalidDataException("No usable list viewport.");
        return box with { Top = top, Bottom = bottom };
    }
    private async Task Swipe(AndroidElement box, bool down, CancellationToken token)
    {
        await verifyOwnership(token);
        int x = (box.Left + box.Right) / 2, high = box.Top + (box.Bottom - box.Top) / 5, low = box.Bottom - (box.Bottom - box.Top) / 5;
        string N(int n) => n.ToString(CultureInfo.InvariantCulture);
        await transport.ExecuteAsync(["shell", "input", "swipe", N(x), N(down ? low : high), N(x), N(down ? high : low), "450"], token);
    }
    private async Task<AndroidHierarchy> Capture(string directory, Action<AndroidHierarchy> guard, CancellationToken token)
    {
        var h = await Read(token); guard(h);
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "hierarchy.xml"), h.MaskedXml, token);
        await File.WriteAllBytesAsync(Path.Combine(directory, "screen.png"), await device.CaptureScreenshotAsync(token), token);
        return h;
    }
    private async Task<bool> Scan(string name, string? room, string[] all, string[] expected, string evidence, CancellationToken token, bool lights = false)
    {
        void Guard(AndroidHierarchy h) { if (lights) Lights(h, room!); else if (room == null) CrestronHomePages.RequireHome(h, plan.Profile.ExpectedHomeText); else CrestronHomePages.RequireRoom(h, room); }
        string container = lights ? "lights_lightsDetails_content" : room == null ? "home_scrollView" : "room_scrollView";
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (bool down in new[] { false, true })
        {
            string? previous = null; bool edge = false;
            for (int step = 0; step < 16; step++)
            {
                var h = await Capture(Path.Combine(evidence, name + (down ? "-down-" : "-up-") + step), Guard, token);
                var viewport = lights ? h.RequireUnique(Id(container)) : Viewport(h, room);
                string signature = Signature(h, container);
                foreach (var node in Nodes(h).Where(n => n.Ancestors("node").Any(a => (string?)a.Attribute("resource-id") == Prefix + container)))
                {
                    string? text = (string?)node.Attribute("text"), description = (string?)node.Attribute("content-desc");
                    foreach (string title in all)
                        if (text == title || description == "room_service_" + title || description == "home_service_" + title) seenNames.Add(title);
                }
                if (lights)
                {
                    var list = Nodes(h).Single(n => (string?)n.Attribute("resource-id") == Prefix + container);
                    if ((string?)list.Attribute("scrollable") == "false") { edge = true; break; }
                }
                if (signature == previous) { edge = true; break; }
                previous = signature;
                if (lights) await SwipeLightList(h, viewport, down, token); else await Swipe(viewport, down, token);
            }
            if (!edge) throw new InvalidDataException("The bounded scan did not reach a stable list edge.");
        }
        await File.WriteAllTextAsync(Path.Combine(evidence, name + "-summary.json"), JsonSerializer.Serialize(new { Expected = expected, Observed = seenNames.Order().ToArray(), FullVerticalTraversal = true }), token);
        return seenNames.SetEquals(expected);
    }
    private static void Lights(AndroidHierarchy h, string room)
    {
        h.RequireAbsent(Id("lightLoadTuning_title"));
        if (h.RequireUnique(Id("lights_lightsDetails_title")).Text != "Lights" ||
            !h.RequireUnique(Id("lights_lightsDetails_subtitle")).Text.Equals(room, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The expected room's light list is not open.");
        _ = h.RequireUnique(Id("lights_lightsDetails_closeButton"));
    }
    private static void RequireLightsChoice(AndroidHierarchy h, string room)
    {
        var item = h.RequireUnique(new(AndroidSelectorKind.Text, "Lights") { AncestorResourceId = Prefix + "room_scrollView" });
        var box = Viewport(h, room);
        if (item.ResourceId != Prefix + "twoButtonsTile_title" || item.Top < box.Top || item.Bottom > box.Bottom)
            throw new InvalidOperationException("Lights navigation is not visible.");
    }
    private async Task<bool> FindLights(string room, CancellationToken token)
    {
        foreach (bool down in new[] { false, true })
        {
            string? previous = null; bool edge = false;
            for (int step = 0; step < 16; step++)
            {
                var h = await Read(token); CrestronHomePages.RequireRoom(h, room);
                try { RequireLightsChoice(h, room); return true; } catch (InvalidOperationException) { }
                string signature = Signature(h, "room_scrollView");
                if (signature == previous) { edge = true; break; }
                previous = signature; await Swipe(Viewport(h, room), down, token);
            }
            if (!edge) throw new InvalidDataException("Lights navigation absence was not established across the full room list.");
        }
        return false;
    }
    private async Task SwipeLightList(AndroidHierarchy h, AndroidElement box, bool down, CancellationToken token)
    {
        // Use an observed clear gutter, never a dimmer, toggle, colour swatch or scene control.
        int x = box.Left + 4;
        if (!box.Enabled || box.Right - box.Left < 100 || box.Bottom - box.Top < 100) throw new InvalidDataException("Unusable light-list viewport.");
        var list = Nodes(h).Single(n => (string?)n.Attribute("resource-id") == Prefix + "lights_lightsDetails_content");
        foreach (var n in list.Descendants("node"))
        {
            if ((string?)n.Attribute("clickable") != "true" && !((string?)n.Attribute("resource-id") ?? "").Contains("Seekbar", StringComparison.OrdinalIgnoreCase)) continue;
            var m = System.Text.RegularExpressions.Regex.Match((string?)n.Attribute("bounds") ?? "", @"^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$");
            if (!m.Success || x >= int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) && x <= int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture))
                throw new InvalidDataException("No clear gutter for read-only light-list scrolling.");
        }
        int high = box.Top + (box.Bottom - box.Top) / 5, low = box.Bottom - (box.Bottom - box.Top) / 5;
        await verifyOwnership(token);
        string N(int n) => n.ToString(CultureInfo.InvariantCulture);
        await transport.ExecuteAsync(["shell", "input", "swipe", N(x), N(down ? low : high), N(x), N(down ? high : low), "450"], token);
    }
    private static void RequireVisibleRoomChoice(AndroidHierarchy h, string room)
    {
        var item = h.RequireUnique(new(AndroidSelectorKind.Text, room)); var box = Viewport(h, null, true);
        if (item.ResourceId != Prefix + "itemRoomTitle" || item.Top < box.Top || item.Bottom > box.Bottom || item.Left < box.Left || item.Right > box.Right)
            throw new InvalidOperationException("Room choice is outside the viewport.");
    }
    private async Task FindRoom(string room, CancellationToken token)
    {
        foreach (bool down in new[] { false, true })
        {
            string? previous = null;
            for (int step = 0; step < 16; step++)
            {
                var h = await Read(token); CrestronHomePages.RequireRooms(h);
                try { RequireVisibleRoomChoice(h, room); return; } catch (InvalidOperationException) { }
                string signature = Signature(h, "rooms_roomsList"); if (signature == previous) break;
                previous = signature; await Swipe(Viewport(h, null, true), down, token);
            }
        }
        throw new InvalidDataException("Expected room was not found within bounded scrolling.");
    }
    private async Task Tab(bool rooms, CancellationToken token)
    {
        var h = await Read(token);
        if (rooms) CrestronHomePages.RequireHome(h, plan.Profile.ExpectedHomeText); else CrestronHomePages.RequireRooms(h);
        var bar = Nodes(h).Single(n => (string?)n.Attribute("resource-id") == Prefix + "bottomNavigationView");
        var choices = bar.Elements("node").ToArray();
        if (choices.Length != 2 || choices.Any(n => (string?)n.Attribute("clickable") != "true" || (string?)n.Attribute("enabled") != "true" || n.Descendants("node").Count(c => (string?)c.Attribute("resource-id") == Prefix + "itemBottomNavigationIcon") != 1))
            throw new InvalidDataException("Unexpected bottom tabs.");
        var match = System.Text.RegularExpressions.Regex.Match((string?)choices[rooms ? 1 : 0].Attribute("bounds") ?? "", @"^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$");
        if (!match.Success) throw new InvalidDataException("Invalid observed tab bounds.");
        var b = Enumerable.Range(1, 4).Select(i => int.Parse(match.Groups[i].Value, CultureInfo.InvariantCulture)).ToArray(); var outer = h.RequireUnique(Id("bottomNavigationView"));
        if (b[0] < outer.Left || b[1] < outer.Top || b[2] > outer.Right || b[3] > outer.Bottom || b[2] <= b[0] || b[3] <= b[1]) throw new InvalidDataException("Tab outside navigation bar.");
        await verifyOwnership(token);
        await transport.ExecuteAsync(["shell", "input", "tap", ((b[0] + b[2]) / 2).ToString(CultureInfo.InvariantCulture), ((b[1] + b[3]) / 2).ToString(CultureInfo.InvariantCulture)], token);
        await Wait(rooms ? CrestronHomePages.RequireRooms : h => CrestronHomePages.RequireHome(h, plan.Profile.ExpectedHomeText), token);
    }
    private async Task Restore(string? room, CancellationToken token)
    {
        var h = await Read(token);
        if (Nodes(h).Any(n => (string?)n.Attribute("resource-id") == Prefix + "lights_lightsDetails_title"))
        {
            if (room == null) throw new InvalidDataException("Cannot restore an unbound light list.");
            await device.TapAsync(Id("lights_lightsDetails_closeButton"), page => Lights(page, room), token);
            await Wait(page => CrestronHomePages.RequireRoom(page, room), token); h = await Read(token);
        }
        if (Nodes(h).Any(n => (string?)n.Attribute("resource-id") == Prefix + "room_back"))
        {
            if (room == null) throw new InvalidDataException("Cannot restore an unbound room.");
            await device.TapAsync(Id("room_back"), page => CrestronHomePages.RequireRoom(page, room), token);
            await Wait(CrestronHomePages.RequireRooms, token); h = await Read(token);
        }
        if (Nodes(h).Any(n => (string?)n.Attribute("resource-id") == Prefix + "fragmentRoomsTitle")) await Tab(false, token);
        CrestronHomePages.RequireHome(await Read(token), plan.Profile.ExpectedHomeText);
    }
}
