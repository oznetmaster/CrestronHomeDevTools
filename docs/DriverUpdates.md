# Driver update manager

The DevTools console can report available updates across installed Crestron Home drivers and apply a reviewed selection. This is general system maintenance, independent of submission testing. These commands are currently available from source; they are not yet in a published console release.

Build the console with the .NET 10 SDK from the repository root, then use the executable in `artifacts/console` for the commands below:

```powershell
dotnet publish CrestronHomeDevTools.Console/CrestronHomeDevTools.Console.csproj -c Release -o artifacts/console
```

## List available updates

Display the report in the console using an existing processor profile:

```powershell
CrestronHomeDevTools.Console.exe driver-updates --profile development
```

To also save the report for a subsequent update operation, add `--output`:

```powershell
CrestronHomeDevTools.Console.exe driver-updates --profile development --output updates.json
```

The command reads installed devices, queries the processor's driver catalogue and asks Home which installed instances are eligible for each relevant catalogue entry. It does not download packages, refresh the catalogue, update a device or restart the processor. The JSON report is always printed to standard output. With `--output`, the same report is also saved; choose a new output filename for each scan. Without `--output`, no report file is created.

Each entry includes the driver and developer, installed and available versions, affected device names and room IDs, and one of these statuses:

| Status | Meaning |
| --- | --- |
| `UpdateAvailable` | Home confirms a newer version and its affected instances; no processor restart is required. |
| `RebootRequired` | A newer version is eligible but needs explicit restart authorization. |
| `Current` | Home reports equal installed and available versions. |
| `InstalledNewer` | The installed version is newer; the tool will not downgrade it. |
| `Unsupported` | A newer version exists, but Home does not support an in-place update. |
| `Unknown` | Versions, eligibility, instance mapping or restart support could not be confirmed. |
| `Superseded` | A newer eligible catalogue version is reported for the same driver and affected instances. |
| `Conflict` | Catalogue entries overlap or their identity is ambiguous; neither is automatically selected. |

`UnresolvedDevices` lists installed driver instances for which the scan could not establish an eligible catalogue mapping. An empty update selection is not proof that every driver is current if unknown or unresolved entries remain. Child drivers that Home updates through their platform may not have an independent catalogue entry.

This is the catalogue exposed by the selected processor at the recorded time, not a direct query of every historical cloud release. The tool does not infer cloud versus side-loaded origin from a version number or an undocumented availability label. It preserves the processor's `AvailabilityState` verbatim. Review development or side-loaded drivers before including them; do not replace a deliberately installed development version merely because the catalogue offers another version.

## Apply the reviewed selection

Apply all confirmed updates that do not require a restart:

```powershell
CrestronHomeDevTools.Console.exe update-drivers --profile development --plan updates.json --all true --journal update-run-01
```

Alternatively, select exact catalogue IDs from the report:

```powershell
CrestronHomeDevTools.Console.exe update-drivers --profile development --plan updates.json --drivers "CATALOGUE_ID_1,CATALOGUE_ID_2" --journal update-run-02
```

The selection is at driver level: Home's update operation applies to **all eligible instances of that driver**, which are enumerated in the report. It cannot update an arbitrary subset of those instances. The tool takes the existing shared processor reservation before applying any change, so an active test or submission reservation prevents an update batch. It never removes someone else's reservation.

To include restart-required updates, explicitly name the selected processor in both arguments:

```powershell
CrestronHomeDevTools.Console.exe update-drivers --profile development --processor DEVELOPMENT --plan updates.json --all true --confirm-reboot DEVELOPMENT --journal update-run-03 --timeout 600
```

The name or address in `--confirm-reboot` must exactly match the selected processor. This authorizes the processor restarts needed by the selected updates, not a restart of the Windows computer. Without it, `--all true` excludes `RebootRequired`; explicitly selecting such an entry is rejected. Each restart-required update is completed and verified before the next, so more than one restart may occur. Updates that require reconfiguration rather than a confirmed reboot-only completion stop for inspection.

The tool binds the report to the processor address, ports and verified HTTPS certificate. It rechecks the entire selection before the first update and rechecks each driver immediately before its operation. It verifies the resulting version, loaded state, instance identity and room before moving on. A changed version, scope, catalogue identity or device identity stops the batch. It does not silently add newly eligible devices, select a newer release or downgrade a driver.

Saved profiles and `--settings` plus `--credentials` work as with other DevTools commands. Passwords are never command-line arguments. With named encrypted credentials, settings contain only the target and ports; login and trust stay in the existing protected store. Reports contain only selected metadata, not raw configuration properties.

## Results and recovery

Every batch requires a new journal directory. The journal records the review and selection, an intent before each operation, its returned operation ID and verified completion. `result.json` distinguishes completed, failed and unconfirmed work; untouched later entries are still visible in the original intent. Standard output contains the batch result. Exit 0 means completion (or no eligible selection); 1 means a known stop/failure; 2 is invalid usage; 3 indicates an unconfirmed operation or reservation cleanup.

On a failure or uncertain result, no later update is submitted. A lost reply, timeout or interrupted restart is never automatically retried. The shared reservation remains held if a submitted operation cannot be confirmed finished; inspect the private lease receipt and processor first. A completed update is not rolled back automatically. Retain the journal, reconcile any uncertain operation, then generate a fresh report before applying remaining updates. Reusing a journal is refused.

## Library use

`DriverUpdateManager.InspectAsync` produces the same report. `SelectUpdates` validates an explicit selection and restart policy. `ApplyAsync` rechecks and applies that selection with a durable journal. The calling application must hold a `ProcessorOperationLease` for the entire apply/reconnect sequence and release it only when `SafeToReleaseReservation` is true. For restart-required updates, supply an explicitly authorized `DriverRebootHandler` that reconnects and verifies the same reservation. The console implements this orchestration.

Offline regression tests cover eligibility, missing coverage, version alternatives, no downgrades, changed plans, overlapping scopes, terminal failure, uncertain responses, restart callbacks, preserved identity and journal reuse. Live cloud catalogue freshness and update behavior across different driver families still require validation on an available development processor; offline results do not establish that coverage.
