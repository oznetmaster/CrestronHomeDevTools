# Actual-driver removal validation

These APIs are available in source and are pending a packaged release. Removing the actual installed driver is a separate final check from cleaning up a temporary NUnit test instance. Run it only after the driver's functional and endurance checks and restoration of any controlled physical devices.

`DriverRemovalValidation.RunAsync` accepts an exact `DriverRemovalTarget`, a fresh private journal directory, a timeout, fresh configuration connections, reservation verification, a UI observer and a log reader. The outer coordinator must verify the candidate package and hold both processor and Android reservations. Both connection factories must address the target processor using its saved trust pins. This API does not acquire reservations, authorize physical controls, deploy a replacement, or select a driver by discovery order.

The operation verifies the installed root's ID, parent, name, model, room, numeric version and Loaded state. It records the entire descendant tree, including intermediate children without rooms, and the identities of unrelated devices. Before sending removal it requires a successful UI baseline, retained UI evidence, confirmed return to Home, a usable current-boot log and an unchanged inventory. The existing removal API's exact affected-device and no-reboot guards remain in force.

After sending removal once, it waits for the root and descendants to disappear, including newly observed children attached to that tree. It verifies that unrelated identities remain intact, invokes the absence observer, checks inventory again, and compares the processor logs. It retains each observation and the result; it does not automatically reinstall the driver.

## UI observer contract

The observer receives the original selected device tree, `false` for the baseline or `true` for post-removal observation, and a fresh evidence directory. Return `DriverRemovalUiOutcome(Passed, HomeRestored)` based on real app observations. Capture the relevant Home and Room views, including scrollable content, and retain the captures in that directory. Do not equate configuration API absence with absence in the app. A callback result or an arbitrary file by itself is not independent proof: the coordinator must validate the producer and audit its raw evidence before using it in a checklist.

The library requires evidence files in both phases but does not interpret screenshots or infer driver-specific tile presentation. The automation assembly supplies the observer described below. The standard installed-driver NUnit runner deliberately requires the candidate to remain installed afterward; do not remove a driver inside that runner and weaken its post-test identity check.

## Public app observer and coordinated execution

`CrestronHomeDevTools.Automation.DriverRemovalAppObserver` observes the selected Home and Room lists, including bounded vertical traversal. A `DriverRemovalAppPlan` contains the Android profile, explicit tile IDs/names/rooms/Home presentation, and explicitly reviewed `NonvisualDeviceIds`. Every selected root and descendant must appear in exactly one category. Set a tile's `NativeLight` property when the load appears in its room's native Lights list rather than as an extension tile. The observer opens that list using its navigation title; it never taps its power, dimmer, scene or colour controls. Unsupported presentation types require another observer; declaring a visible device nonvisual is not a workaround.

The observer captures masked hierarchies and PNGs, records its observed/expected names and returns Home. It detects a surviving selected tile anywhere in the traversed lists. Native-light scrolling requires an observed clear gutter; otherwise it stops. This covers those reviewed views, not every possible subsystem or firmware layout. App page structure changes fail explicitly.

`DriverRemovalWorkflow.ObserveBaselineAsync` validates the candidate package against the installed payload, acquires processor and Android reservations, runs the baseline only, checks the candidate again and releases after confirmed Home restoration. Its result has `RemovalRequested: false` and cannot become removal evidence. Use it to validate a new equipment/app profile without deleting the driver.

`DriverRemovalWorkflow.RemoveAsync` uses the same candidate checks and reservations, then invokes the actual removal API with the app observer and log reader. It is a final operation: finish physical-state restoration and any tests requiring the installation first. Exceptions with uncertain control or removal state retain reservations for inspection. Neither method redeploys a driver. The app must already be connected to the intended processor; the profile's Home name is a presentation assertion, not network-route authentication.

## Worker integration

The optional automation `Removal` setting contains `App` and `RequirementId`. It requires `PostEnduranceTests`, `PostEnduranceFromDeployment: true`, actual candidate deployment and a pinned review policy. That policy entry must describe this combined configuration/app/log check without unrelated response-time or physical-restoration claims.

The worker runs final removal after successful post-endurance checks and before PDF preparation. It obtains root/catalogue identity from retained deployment receipts, uses the same Android profile, saves a durable intent, and inventories the resulting raw evidence. Review consumes the generated observation automatically. Changed prior evidence, an uncertain attempt or a baseline-only result cannot become a pass; an existing attempt is never rerun. A failed result stays failed on recovery. Omit `Removal` when this operation has not been authorized or configured; omission does not satisfy a checklist requirement.

## Reading and comparing processor logs

`ProcessorErrorLog.ReadAsync(host, credential, sshFingerprint, timeout, token)` opens one pinned SSH session and sends only `err plogcurrent`, the read command documented in Crestron's [4-Series Message Logging reference](https://docs.crestron.com/en-us/8559/Content/Topics/Reference/Message-Logging.htm). It does not clear logs or change logging configuration. Reads require a complete terminal prompt and are bounded to two Mi-characters and two minutes.

The snapshot retains the raw response, persistent records with multiline continuations, asynchronous console lines, request/observation times and unrecognized output. The processor's end-of-log marker is excluded from the final record.

`ProcessorErrorLog.Compare(before, after)` requires successive snapshots from the same host. The original persistent entries must remain an exact prefix. Log reset, truncation, changed records, unknown framing, an empty baseline or suspended logging prevents a clean interval claim. Historical errors remain in the raw baseline but are not new errors. New Error/Fatal entries, console errors or exception mentions require review; the comparison does not attribute their cause to the driver. A firmware format change may require a reader update rather than relaxed validation.

## Results, retention and recovery

`Passed` requires confirmed removal, preservation of unrelated devices, app absence, return to Home and no new error/exception diagnostics in a comparable interval. `SafeToRelease` concerns confirmed device removal, preservation and Home restoration; an error-log or UI assertion failure still fails the test even when release is safe. The caller must also satisfy its own physical-restoration and ownership checks before releasing reservations.

A thrown exception or connection loss after the durable removal intent has an uncertain outcome. Inspect the journal and fresh processor state before reconciliation. An existing journal cannot be replayed. Do not release reservations unconditionally, delete failure evidence, or resend removal automatically. Journals and raw logs may contain private diagnostic information and belong in protected evidence storage, not public repositories.

## Verified scope

Offline tests exercise exact removal scope, descendants, unrelated-device preservation, failed UI preflight, retained evidence, uncertain removal, no replay, orphaned children, final inventory checks, full-list app observation, native-light navigation and new versus historical log errors. Controller tests verify deployment binding, preservation of failures, interruption without replay and evidence-change detection. Two successive read-only snapshots on a CP4-R verified the public log reader and prefix comparison with real current-boot output. A real baseline-only run verified five extension tiles and one native light through Home, Room and Lights views, candidate identity, Home restoration and reservation release. Actual removal with the Android observer and its full unattended execution have **not** yet been demonstrated. These APIs do not establish submission completeness or Crestron acceptance.
