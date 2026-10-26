# Managed light replacement and configuration repair

The replacement workflow has two separate responsibilities:

1. Preserve user configuration: room, name, supported scene targets and programming references.
2. Regenerate Home's child configuration and bindings through fresh commissioning and initial configuration.

An incomplete old record is evidence, not a restoration template for Home's internals. The replacement workflow never copies old `ActivationMarker`, `DriverDataStore` or internal connection records into the new child. A live `Ready` result alone does not establish this repair succeeded.

**Recommissioning finding:** the October 26 lab test created a ready native L530 wrapper/load and restored all four scene targets, but the newly saved child still had no configuration markers. Its UniversalLightingDevice wrapper rejects the generic configuration-wizard command. Recommissioning alone therefore does not populate the required saved records.

**Missing-record lab validation:** `MissingManagedLightConfiguration` prepared a candidate using the exact empty-store marker schema of a healthy sibling. With explicit approval for the processor-wide interruption, a separate lab helper stopped Home, retained the original locally and remotely, added only `ActivationMarker=true` and the empty `DriverDataStore`, and changed the child record's `ConfigurationNeeded` from 0 to the healthy-record value 1. After a full hardware reboot, the added attributes persisted, the native L530 wrapper/load was loaded and online with its control available, all device identities and four scene targets were preserved, and both Demo bulbs were directly confirmed off. The processor lease was released after verification.

This was Home 4.012.0194 with diagnostic Kasa/Tapo 2.1.002.0004. The lab child was already online before the patch; this test proves preservation and post-reboot readiness, not a controlled reproduction of the reported failing upgrade or physical HS220/KS225 operation. The offline planner never applies candidates itself. A customer repair tool still needs strict version/scope checks, a validated template when all siblings have missing records, safe stopped-Home journaling/recovery, and customer acceptance testing.

## Current scope

`LiveConfigurationCapture` saves raw device/location/group/quick-action/media resources, scene definitions and programming definitions in a new private directory. Programming enumeration includes location 0, which includes system-wide and unlocated entities on the validated Home release. Checksums pin the manifest and every entry. Captures contain private configuration and must not be published.

`ManagedChildReplacement.Plan` supports one managed native-light wrapper with exactly one load, a reviewed physical managed-device ID, and default voice-control settings. It rejects unsupported references before removal. Reference restoration currently supports light scene targets and flat command sequences with one trigger using Home's copy/paste DTO. Other programming modes, multiple triggers, nested sequences, references in unsupported resources, and customized voice settings require further implementations. A rejected preflight changes no processor configuration.

`ManagedChildReplacement.ReplaceAsync` requires the caller's processor operation lease and the reviewed, validated capture. It captures again and refuses configuration changes after review, removes the reviewed load/wrapper once, recommissions the same managed physical device, applies caller-supplied initial configuration when requested, and restores references using the new wrapper/load IDs. Every write has a durable local intent and readback. An uncertain request is not retried. An interrupted journal requires inspection and explicit recovery.

The result state is `RecommissionedAwaitingPersistenceVerification`. It intentionally is not a completed repair. The caller must verify the expected newly saved configuration markers and managed binding, reload the reviewed driver tree, and recheck child readiness and the restored definitions. Do not report customer success until those checks pass. The capture is not a full processor backup, and the workflow does not edit Home's manifest files directly.

## Usage sequence

- Capture with an explicitly selected processor identity and certificate pin.
- Validate the capture against its manifest SHA-256.
- Prepare and review an exact `ManagedDeviceRequest` and existing wrapper ID.
- Acquire the shared processor lease and call `ReplaceAsync` with a new private journal.
- Inspect fresh saved configuration evidence, including binding to the expected platform and physical managed-device ID.
- Reload the exact reviewed platform tree; inspect the operation result and observe every affected child.
- Read scene/programming definitions again and compare to the mapped capture. Programming comparison ignores only regenerated local block/step IDs.
- Verify fresh saved configuration evidence again and preserve all receipts privately.

Process one child at a time. Capture and review a fresh graph before the next child; do not reuse a plan whose referenced IDs have already changed.
