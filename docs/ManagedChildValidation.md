# Managed children in a validation workflow

This API requires DevTools 1.6.0 or later. It combines configuration management with a caller-supplied test producer; it has no NUnit, Android or Crestron SDK dependency. It is useful for ordinary development CI as well as optional submission validation.

## Lifecycle and ownership

After installing or updating the intended platform, hold the existing processor reservation across the entire operation. If testing uses Android, acquire its reservation after the processor reservation and retain both through UI restoration. Do not launch the separately locking console commands inside this held reservation.

`ManagedDeviceValidation.RunAsync` performs these steps:

1. Validate and freeze the target list. Each target has a distinct alias, advertised managed-device ID and room/name pair. It never adopts a manually installed child.
2. Commission each new child, enter its initial configuration and observe readiness. Persist each returned ID with its alias and expected identity. Stop before testing if configuration is required or setup is uncertain.
3. Write `bindings.json`, then invoke the supplied test producer with the same read-only bindings. Child IDs come from this run; do not reuse an ID from an earlier run.
4. Record test success and independently confirmed restoration as separate values. If restoration is unconfirmed, retain the children and reservation for reconciliation.
5. After confirmed restoration, remove children in reverse creation order through their commissioning receipts. Verify disappearance and preservation of other devices. A cleanup exception stops further removal and retains evidence.

The ownership callback is checked before and after every configuration, test and cleanup stage. The connection factory opens a fresh management session for each setup or removal, avoiding reuse of a websocket that may have expired during a long test.

## Library integration

Supply the same processor identity, credentials and certificate policy to the connection factory and reservation. Persist the reservation owner before mutation. The validation journal belongs in a fresh private run directory.

```csharp
// These callbacks and targets belong to the encompassing workflow.
var outcome = await ManagedDeviceValidation.RunAsync(
    OpenProcessorConnectionAsync,
    targets,
    privateValidationJournal,
    TimeSpan.FromMinutes(2),
    VerifyHeldReservationsAsync,
    RunBoundTestsAndVerifyRestorationAsync,
    cancellationToken);

if (!outcome.RestorationConfirmed || !outcome.CleanupConfirmed)
    throw new InvalidOperationException("Retain reservations and reconcile this run.");

await ReleaseHeldReservationsAsync();
return outcome.Passed ? 0 : 1;
```

Each `ManagedDeviceTestTarget` contains an alias and a `ManagedDeviceRequest` identifying the parent ID/model/version, advertised managed ID, new child name/model and Home room ID. Read those values from the intended processor. The callback receives `ManagedDeviceTestBinding` values containing the actual new child ID alongside that request. Map aliases into the test producer's private input format; never select a physical device merely because it is first in a discovery list.

The test callback returns `ManagedDeviceTestOutcome(Passed, RestorationConfirmed)`. Its restoration flag must be supported by the producer's checks of the relevant physical devices, persistent settings and UI state. A successful process exit alone is insufficient. For a process-based producer, wait for actual termination and validate its result identity, discovered/executed test coverage and restoration evidence before returning. The outer workflow remains responsible for pinning the tested driver package and producer, enforcing the overall test deadline and supplying authorized physical-control scope.

A failed test with verified restoration is cleaned up and still returns `Passed: false`. A thrown exception, lost ownership, partial setup or unconfirmed restoration does not trigger guessed cleanup. Disposing a reservation is not release. Never release it in an unconditional `finally` block around this operation.

## Evidence and recovery

The journal contains the frozen request, per-alias commissioning journals, created bindings, test intent/outcome, per-child cleanup receipts and the final result. A successful result requires both restoration and cleanup; neither erases a test failure. A `stopped.json` record describes an interrupted or uncertain run when it can be written. Original exceptions are preserved if evidence writing also fails.

Existing run directories are never replayed. An operator or recovery workflow must inspect the recorded owner, process status and processor state before continuing. Do not delete a journal or ownership marker to make the same operation run again. Journals may include private configuration and must stay outside public source, GitHub logs and release artifacts.

This orchestration supports multiple distinct targets, with offline coverage for partial setup, duplicate IDs, failed tests, lost ownership, cleanup ordering and uncertain results. Hardware evidence must identify the actual targets exercised; a single-child run does not establish multiple-platform isolation. The final combined CLI/Test Explorer workflow integration and submission-specific acceptance remain separate from this library API.

See [driver configuration and cleanup](DriverConfiguration.md#initialize-a-newly-commissioned-managed-child), [processor coordination](ProcessorCoordination.md) and [protocol observations](ProtocolReference.md#managed-child-configuration-entry).


## Validation status

The library API passed a real CP4-R workflow with one newly created managed room child and a caller-supplied Android editor test. The test received the actual returned child ID, independently verified editor and hub-state restoration, and returned Home. Shared cleanup removed the child, preserved the original inventory and released reservations. This was a selected development test, not a multi-platform isolation check or completed driver submission. Offline lifecycle tests cover the additional failure and multi-target ordering cases described above.
