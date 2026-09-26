# Actual-driver removal validation

These APIs are available in source and are pending a packaged release. Removing the actual installed driver is a separate final check from cleaning up a temporary NUnit test instance. Run it only after the driver's functional and endurance checks and restoration of any controlled physical devices.

`DriverRemovalValidation.RunAsync` accepts an exact `DriverRemovalTarget`, a fresh private journal directory, a timeout, fresh configuration connections, reservation verification, a UI observer and a log reader. The outer coordinator must verify the candidate package and hold both processor and Android reservations. Both connection factories must address the target processor using its saved trust pins. This API does not acquire reservations, authorize physical controls, deploy a replacement, or select a driver by discovery order.

The operation verifies the installed root's ID, parent, name, model, room, numeric version and Loaded state. It records the entire descendant tree, including intermediate children without rooms, and the identities of unrelated devices. Before sending removal it requires a successful UI baseline, retained UI evidence, confirmed return to Home, a usable current-boot log and an unchanged inventory. The existing removal API's exact affected-device and no-reboot guards remain in force.

After sending removal once, it waits for the root and descendants to disappear, including newly observed children attached to that tree. It verifies that unrelated identities remain intact, invokes the absence observer, checks inventory again, and compares the processor logs. It retains each observation and the result; it does not automatically reinstall the driver.

## UI observer contract

The observer receives the original selected device tree, `false` for the baseline or `true` for post-removal observation, and a fresh evidence directory. Return `DriverRemovalUiOutcome(Passed, HomeRestored)` based on real app observations. Capture the relevant Home and Room views, including scrollable content, and retain the captures in that directory. Do not equate configuration API absence with absence in the app. A callback result or an arbitrary file by itself is not independent proof: the coordinator must validate the producer and audit its raw evidence before using it in a checklist.

The library requires evidence files in both phases but does not interpret screenshots, know driver-specific tile presentation, or provide a built-in Android observer. The standard installed-driver NUnit runner deliberately requires the candidate to remain installed afterward; do not remove a driver inside that runner and weaken its post-test identity check.

## Reading and comparing processor logs

`ProcessorErrorLog.ReadAsync(host, credential, sshFingerprint, timeout, token)` opens one pinned SSH session and sends only `err plogcurrent`, the read command documented in Crestron's [4-Series Message Logging reference](https://docs.crestron.com/en-us/8559/Content/Topics/Reference/Message-Logging.htm). It does not clear logs or change logging configuration. Reads require a complete terminal prompt and are bounded to two Mi-characters and two minutes.

The snapshot retains the raw response, persistent records with multiline continuations, asynchronous console lines, request/observation times and unrecognized output. The processor's end-of-log marker is excluded from the final record.

`ProcessorErrorLog.Compare(before, after)` requires successive snapshots from the same host. The original persistent entries must remain an exact prefix. Log reset, truncation, changed records, unknown framing, an empty baseline or suspended logging prevents a clean interval claim. Historical errors remain in the raw baseline but are not new errors. New Error/Fatal entries, console errors or exception mentions require review; the comparison does not attribute their cause to the driver. A firmware format change may require a reader update rather than relaxed validation.

## Results, retention and recovery

`Passed` requires confirmed removal, preservation of unrelated devices, app absence, return to Home and no new error/exception diagnostics in a comparable interval. `SafeToRelease` concerns confirmed device removal, preservation and Home restoration; an error-log or UI assertion failure still fails the test even when release is safe. The caller must also satisfy its own physical-restoration and ownership checks before releasing reservations.

A thrown exception or connection loss after the durable removal intent has an uncertain outcome. Inspect the journal and fresh processor state before reconciliation. An existing journal cannot be replayed. Do not release reservations unconditionally, delete failure evidence, or resend removal automatically. Journals and raw logs may contain private diagnostic information and belong in protected evidence storage, not public repositories.

## Verified scope

Offline tests exercise exact removal scope, descendants, unrelated-device preservation, failed UI preflight, retained evidence, uncertain removal, no replay, orphaned children, final inventory checks and new versus historical log errors. Two successive read-only snapshots on a CP4-R verified the public log reader and prefix comparison with real current-boot output. Actual removal with the Android observer and its integration into unattended review preparation have **not** yet been validated. These APIs do not establish submission completeness or Crestron acceptance.
