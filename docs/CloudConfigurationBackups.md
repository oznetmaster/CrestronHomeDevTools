# Cloud configuration backups

`ProcessorCloudBackups` lists cloud configuration metadata and requests a current normal or golden configuration backup through an authenticated `ProcessorConnection`. These operations leave Crestron Home running: they issue no program-stop, restart, reboot or restore command.

The interface is unofficial and firmware-dependent. Listing has been exercised on an MC4-R running Home 4.012.0194, returning 200 metadata entries using the processor's own authenticated backup context. This does not establish compatibility with other firmware or access to another processor's backups. Normal-save hardware validation is recorded separately below; golden-save behavior has offline coverage only.

## Library use

Use a connected, certificate-pinned `ProcessorConnection` and the exact processor device ID from authenticated inventory. Both methods check the selected device's advertised command capability before submission.

```csharp
var backups = await ProcessorCloudBackups.ListAsync(
    connection, processorDeviceId, TimeSpan.FromMinutes(1), cancellationToken);

// This writes a new normal cloud configuration backup.
var saved = await ProcessorCloudBackups.SaveAsync(
    connection, processorDeviceId, CloudConfigurationBackupKind.Normal,
    TimeSpan.FromMinutes(5), cancellationToken);

if (saved.BackendSucceeded && saved.NewEntryVerified)
{
    var observedEntry = saved.NewOrUpdatedEntries.Single();
    // Store metadata privately; this is not a downloaded backup archive.
}
```

Saving requires an explicitly supplied `Normal` or `Golden` kind. `Golden` requests saving/replacing the golden configuration and must be chosen deliberately. There is no default golden operation, automatic schedule or automatic save retry. These are library APIs; this change adds no console command.

`BackendSucceeded` means the dedicated backup operation reported success. `NewEntryVerified` additionally means exactly one new or updated entry of the requested kind was observed between the before/after listings. This verifies metadata observation, not archive integrity, completeness or successful restoration. Zero or multiple matching changes leave that property false, including concurrent backups that make attribution ambiguous. A golden replacement may retain its opaque ID while changing date or other metadata. Hardware readback also showed 199 existing backups receiving different `ConfigurationId` values after one new backup appeared. IDs therefore must not be treated as stable across listings; the comparison uses filename, date, version and type. Treat an ID as belonging to its particular listing.

Dates and optional version, type and verification metadata are retained as returned; no timezone or additional meaning is inferred. Opaque IDs and filenames should be retained exactly and kept private.

If submission, event observation or readback fails or times out, a backup may already exist or still be running. Inspect the cloud list before deciding whether to submit a separate request. The API never retries a submitted save. No public restore or historical file-download helper is added by this change.

A processor-reported failure throws `CloudBackupOperationException`, an `IOException` that preserves `OperationId` and the exact `DiagnosticEvent`, including backend `Message` and `SubsystemCode` when supplied. Retain that event privately; the exception's ordinary message does not print its potentially sensitive contents. Failure evidence is preserved even if later success or end events arrive.

## Protocol and completion

| Purpose | Command | Parameters |
|---|---|---|
| List this processor's configurations | `cp.processorBackupRestore:beginConfigurationListRetrieval` | `{ "deployCode": "" }` |
| Save normal configuration | `cp.processorBackupRestore:beginBackupOperation` | `{ "operation": "backupConfiguration" }` |
| Save golden configuration | `cp.processorBackupRestore:beginBackupOperation` | `{ "operation": "backupGoldenConfiguration" }` |

The list request requires the `deployCode` key. The verified empty value selects the authenticated processor's own context; this API neither generates a code nor accepts a code for a different processor. The returned operation ID correlates `configurationListChanged` and `backupStatusChanged` events. List metadata comprises `ConfigurationId`, `Filename`, `Date`, `Version`, `VerifiedStatus` and `Type`.

The dedicated `backupStatusChanged` status mapping differs from generic `cp.types:operationStatusChanged`:

| Numeric value | Backup status | Generic operation status |
|---|---|---|
| 0 | Failed | Started |
| 1 | UnzipFailed | Progress |
| 2 | Succeeded | Failed |
| 3 | Started | Succeeded |
| 4 | Ended | Ended |
| 5 | Unpacked | Not mapped here |

Save completion requires both dedicated `Succeeded` and `Ended` events. The observed processor refreshes its backup list after upload success under the same operation ID; the client waits for that refresh to end before requesting its independent verification list. An accepted command or `Ended` event alone is insufficient. Generic and dedicated failures are retained. Event tracking tolerates early/list-event reordering, rejects malformed or duplicate list metadata, bounds retained operation state and unblocks pending waits when the connection fails.

## Historical download and local copies

The cloud list contains metadata, not archive contents or a download URL. The examined Configure Pro backup interface exposes list, save and restore, but no separate historical-file download operation was found. The generic authenticated file downloader requires an already-known URL; it does not resolve backup IDs. Download-only cloud access remains unverified. Restore must not be used merely to extract an older configuration.

Crestron's [Configure Pro backup documentation](https://docs.crestron.com/en-us/9599/Content/Topics/Operation/Settings.htm) describes cloud backup and restore. The separate [myCrestron documentation](https://docs.crestron.com/en-us/9599/Content/myCrestron.htm) describes portal backup download and its account requirements. Processor-authenticated listing does not establish dealer-portal access.

The manual **Copy Data between Control Processors** procedure (Crestron Home manual Doc. 9599B, pages 500-503) is a separate current-system SFTP migration workflow. It explicitly stops Home for the copy, uses lowercase `user`, excludes `AutoUpdate`, and warns against copying uppercase `User` certificate/XioCloud contents. Its destination layout and deletion/reboot steps are migration-specific. That stop requirement does not apply to the cloud save APIs above. A complete typed local-system snapshot/migration feature is not implemented here; file listing or copying while Home is active does not by itself establish a consistent recoverable backup.

## Validation

Offline coverage exercises exact request arguments and list/save/list command ordering, explicit normal/golden selection, status mappings, failure and uncertain-outcome handling, metadata attribution, event correlation and bounded state. No stop, restore or reboot command is part of the save sequence.

Live validation on 2026-10-26 confirms the typed listing API and 200 entries on MC4-R / Home 4.012.0194. Exactly one explicitly authorized normal-save request was submitted. The client observed a generic operation failure and threw; it did not retry. Subsequent independent read-only listings showed exactly one new normal backup marked `Passed`, and the Home log recorded packaging and upload initiation. Home's program uptime established that it remained running from before the request through reconciliation. This is conflicting completion evidence, not a clean end-to-end save API pass: archive upload completion/integrity and the reason for the failure notification remain unverified. The API deliberately retains failure semantics until that discrepancy is understood.

The full offline suite passed 2,483 tests before the ID-renumbering correction. All 28 focused cloud tests passed after that correction, including two new renumbering cases. Golden save, cloud restore, historical archive download and full local-system recovery have not been exercised by this change. No stop, reboot, golden save or restore was performed during this cloud investigation.

### Diagnostic follow-up

One additional normal backup was separately authorized and submitted on 2026-10-26, with both status streams durably captured. Both reported `Succeeded` about 47 seconds after request submission, or 31 seconds after the first dedicated `Started` event. The same operation ID then reported another `Started` pair, delivered a refreshed 200-entry configuration list, and reported `Ended` in both streams. Independent typed listing identified exactly one new normal entry marked `Passed` relative to the immediately preceding list. Program uptime still placed Home's start at approximately 08:43:23 UTC, before both requests. This protocol-level capture succeeded; it was not a second end-to-end run of the typed save wrapper.

The later-flushed Home log explicitly confirmed successful upload of the first backup at 17:56:18, approximately 64 seconds after its packaging-start log line. Its exact request-to-completion duration was not retained. The original generic failure therefore did not mean that no backup was uploaded. A timeout/completion-reporting issue is a possible explanation; the original full failure event was not retained and the cause is not established. Generic failures remain terminal in the client; no status check was weakened based on this inference. Future processor-reported failures now retain the exact diagnostic event. All 31 focused cloud tests pass, including diagnostic retention, replay of the captured successful event order and waiting through its final refresh. Two normal backups were submitted in total, each separately approved; no automatic write retry occurred.

Read-only capability inspection found no advertised backup archive download command on this processor. `cp.processorDeploy:getBackupCodeInformation` requires both `deployCode` and `filename`; with a listed filename and empty code it rejects the request as `DeployCodeInvalid`. The empty-code own-processor behavior verified for listing must not be generalized to other commands. Neither a deploy code nor a restore/import operation was generated or invoked during these checks.

After the completion-boundary fix and diagnostic-retention changes, the complete offline suite passed 2,488 tests with no failures or skips. No further cloud backup was submitted for this change. The revised typed save wrapper has offline replay coverage; it has not received another end-to-end live save invocation. These findings are based on observed protocol behavior and lab evidence, not a published or supported Configure Pro API contract.
