// Copyright (c) 2026 Neil Colvin. MIT licensed.
namespace CrestronHomeDevTools;

/// <summary>Processor-facing room lookup. Workflow inputs and results use names, never resolved room IDs.</summary>
public static class ProcessorTestRooms
{
    internal static int Resolve(IReadOnlyList<ProcessorLocation> locations, string roomName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roomName);
        var matches = locations.Where(room => room.Category == "Room" &&
            string.Equals(room.Name, roomName, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1 || matches[0].Id <= 0)
            throw new InvalidOperationException("The named room is missing or ambiguous on this processor.");
        return matches[0].Id;
    }

    public static async Task RequireAsync(ConfigurationClient client, IEnumerable<string> roomNames,
        CancellationToken token = default)
    {
        var locations = await client.GetLocationsAsync(token).ConfigureAwait(false);
        foreach (var name in roomNames) Resolve(locations, name);
    }

    public static async Task VerifyDeviceAsync(ConfigurationClient client, int deviceId, string roomName,
        CancellationToken token = default)
    {
        var room = Resolve(await client.GetLocationsAsync(token).ConfigureAwait(false), roomName);
        var device = await client.GetDeviceAsync(deviceId, token).ConfigureAwait(false);
        if (device == null || device.LocationId != room)
            throw new InvalidOperationException("The selected device is not in the named room on this processor.");
    }

    /// <summary>Describe device assignments by room name for name-only preservation reports.</summary>
    public static async Task<IReadOnlyDictionary<int,string>> NamesForDevicesAsync(ConfigurationClient client,
        IReadOnlyList<DeviceInfo> devices, int rootDeviceId, CancellationToken token = default)
    {
        var rooms = await client.GetLocationsAsync(token).ConfigureAwait(false);
        var selected = new HashSet<int> { rootDeviceId };
        while (true) {
            var count = selected.Count;
            selected.UnionWith(devices.Where(device => device.ParentDeviceId is int parent && selected.Contains(parent)).Select(device => device.Id));
            if (count == selected.Count) break;
        }
        var names = new Dictionary<int,string>();
        foreach (var device in devices.Where(device => selected.Contains(device.Id)))
        {
            var matched = rooms.Where(room => room.Category == "Room" && room.Id == device.LocationId).ToArray();
            if (matched.Length != 1 || string.IsNullOrWhiteSpace(matched[0].Name) ||
                Resolve(rooms, matched[0].Name!) != device.LocationId)
                throw new InvalidOperationException("A device room cannot be represented by one unambiguous name.");
            names.Add(device.Id, matched[0].Name!);
        }
        return names;
    }

    /// <summary>Compare processor-native assignments internally without exporting room IDs.</summary>
    public static bool AssignmentsPreserved(IReadOnlyList<DeviceInfo> before, IReadOnlyList<DeviceInfo> after) =>
        before.All(device => after.Count(current => current.Id == device.Id && current.Model == device.Model &&
            current.LocationId == device.LocationId) == 1);
}
