// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License.
using System.ComponentModel;
using System.Runtime.CompilerServices;
namespace CrestronHomeDevTools.DriverUpdates;
internal sealed class UpdateRow : INotifyPropertyChanged
    {
    public AvailableDriverUpdate? Source { get; init; }
    public string DriverId => Source?.DriverId ?? "";
    public string Driver { get; init; } = "";
    public string Devices { get; init; } = "";
    public string Rooms { get; init; } = "";
    public string Installed { get; private set; } = "";
    public string Available { get; init; } = "";
    public string Status { get; private set; } = "";
    public string Restart { get; init; } = "";
    public string Detail { get; private set; } = "";
    public bool CanSelect { get; private set; }
    private bool _selected;
    public bool Selected { get => _selected; set { bool next = value && CanSelect; if (_selected != next) { _selected = next; Changed (); } } }
    public string Result { get; private set; } = "";
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed ([CallerMemberName] string? property = null) => PropertyChanged?.Invoke (this, new (property));
    public static IEnumerable<UpdateRow> CreateRows (DriverUpdateReport report, IReadOnlyDictionary<int, string> rooms)
        {
        // Catalogue search alternatives with no installed instance are not installed drivers.
        foreach (var group in report.Drivers.Where (r => r.Devices.Length > 0).GroupBy (r => string.Join (",", r.Devices.Select (d => d.Id).Order ())))
            {
            var entries = group.ToArray ();
            var eligible = entries.Where (r => r.Status is "UpdateAvailable" or "RebootRequired").ToArray ();
            if (entries.Length == 1) { yield return From (entries[0], rooms); continue; }
            if (eligible.Length == 1 && entries.All (r => r == eligible[0] || r.Status == "Superseded"))
                { yield return From (eligible[0], rooms); continue; }
            string models = string.Join (" / ", entries.Select (r => r.Model).Distinct ());
            bool current = entries.All (r => r.Status == "Current");
            yield return From (entries[0] with
                {
                Model = models, Status = current ? "Current" : "Unknown",
                Detail = current ? "Matching catalogue entries report the installed version."
                    : "Multiple catalogue entries match these installed instances; update identity is unresolved. "
                        + string.Join ("; ", entries.Select (r => $"{r.Manufacturer} {r.Model}: {r.CatalogueVersion} ({r.Status})"))
                }, rooms);
            }
        foreach (var device in report.UnresolvedDevices) yield return Unresolved (device, rooms);
        }
    public static UpdateRow From (AvailableDriverUpdate source, IReadOnlyDictionary<int, string> rooms) => new ()
        {
        Source = source, Driver = source.Model ?? source.DriverId,
        Devices = string.Join (", ", source.Devices.Select (d => d.Name ?? d.Id.ToString ())),
        Rooms = RoomNames (source.Devices, rooms),
        Installed = source.Eligibility?.InstalledDriverVersion ?? string.Join (", ", source.Devices.Select (d => d.Version ?? "Unknown").Distinct ()),
        Available = source.Eligibility?.AvailableDriverVersion ?? source.CatalogueVersion ?? "Unknown",
        Status = source.Status switch { "UpdateAvailable" => "Update available", "RebootRequired" => "Update available", "InstalledNewer" => "Installed is newer", _ => source.Status },
        Restart = source.Status == "RebootRequired" ? "Required" : source.Status == "UpdateAvailable" ? "No" : "—",
        CanSelect = source.Status is "UpdateAvailable" or "RebootRequired", Detail = source.Detail
        };
    public static UpdateRow Unresolved (DriverUpdateDevice device, IReadOnlyDictionary<int, string> rooms) => new ()
        {
        Driver = device.Model ?? "Unknown driver", Devices = device.Name ?? device.Id.ToString (), Rooms = RoomNames ([device], rooms),
        Installed = device.Version ?? "Unknown", Available = "Unknown", Status = "Unresolved", Restart = "Unknown",
        Detail = "No confirmed catalogue mapping. This can include legacy or platform-managed child drivers. No update will be attempted."
        };
    private static string RoomNames (IEnumerable<DriverUpdateDevice> devices, IReadOnlyDictionary<int, string> rooms) => string.Join (", ", devices
        .Select (d => d.RoomId is int id ? rooms.GetValueOrDefault (id, id.ToString ()) : "Unassigned").Distinct ());
    public void SetProgress (string state, string detail) { Result = state; Detail = detail; Changed (nameof (Result)); Changed (nameof (Detail)); }
    public void Finish (DriverUpdateStep? step, bool uncertain)
        {
        CanSelect = false; Selected = false;
        if (step?.State == "Updated") { Installed = Available; Status = "Current"; Changed (nameof (Installed)); Changed (nameof (Status)); }
        SetProgress (step?.State switch { "Updated" => "Updated", "Failed" => "Failed", "Unconfirmed" => "Needs inspection", _ => uncertain ? "Not started / inspect run" : "Not started" },
            step?.Detail ?? "The batch stopped before this driver's completion was recorded. Review the run before scanning again.");
        Changed (nameof (CanSelect));
        }
    }
