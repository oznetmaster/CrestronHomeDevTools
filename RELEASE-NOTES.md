# CrestronHomeDevTools 1.29.0

Adds authenticated processor capability discovery, correlated import receipts,
managed-child snapshots and bounded event monitoring, and read-only dynamic
device-settings inspection. The APIs preserve existing custom configuration
connections and Home 4.12 behaviour when newer fields or events are absent.
Property values are excluded from inspection output unless explicitly requested.
See [inspection commands and compatibility](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/ImportReceiptsAndInspection.md).

Import receipts keep terminal-operation and dedicated-completion observations
separate, including unavailable versus empty imported-ID lists. Ended is not
treated as proof of import success; existing deployment catalogue verification
is unchanged. Managed-child monitors subscribe before reading baselines, mark
overlap without inventing server ordering, and fail explicitly on overflow or
lost continuity. Dynamic-settings inspection uses typed metadata and never
executes settings commands. System IDs, including zero, are supported by these
read-only operations.

Adds read-only, version-aware I/O connection preparation for legacy Home 4.7–4.12
and typed Home 4.13 formats, including Serial/Com normalization and CEC direction
checks. Unknown versions and unconfirmed selections stop before commands.
Existing raw configuration strings remain unchanged. Discovery and inspection
were checked on Official Home 4.012.0194 and EAP Home 4.013.0134. A no-transmit
SDK 29 serial probe on a physically disconnected EAP processor port restored
its channel and ready state after an isolated V2 reload; external-device
communication and other port types remain unqualified.

Includes the reconciled driver-update manager, console assessment/reviewed
sequential updates and source-built Windows checklist, plus independent public
driver-catalogue search. Update processing retains processor reservations,
explicit restart consent, version/readiness checks and durable recovery journals.
Actual multi-driver update/restart behaviour still requires controlled live
qualification. Portal search does not establish Crestron Home compatibility.
See [driver updates](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/DriverUpdates.md)
and [published catalogue search](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/PublishedDrivers.md).

Also incorporates retained submission evidence/review repairs, artifact
publication checks, pinned signing-plan preparation, complete bundled Python
import closure, bounded native output paths, and explicit worker-local versus
controller-share input/output contracts with private terminal diagnostics.
Existing frozen submissions and their evidence are unchanged. These tooling
changes do not establish new compliance passes or authorize signing, uploading,
emailing, hardware actions or processor reboots.

Validation includes all 2,708 discovered offline .NET tests and API compatibility
against public 1.28.0, with package-consumer, documentation and packaged-console
checks in release validation. Live empty-queue import receipts preserved device
configuration, programming and port settings on both firmware versions, with
reservations released. Neither lab processor advertised dynamic settings or
supplied non-null inline managed-child state, and no new child-state events
arrived during bounded observation. Positive settings/event paths have offline
protocol and connection-dispatch coverage rather than positive live server
qualification. See [validation scope](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/DevelopmentStatus.md).

The SDK 30 video probe was rejected at import because its manifest declared
minimum SDK 30 while Home reported a maximum of 29. This establishes a
manifest-version gate, not general SDK 30 binary incompatibility. A shade probe
built with SDK 30 and declaring minimum SDK 29 imported but did not complete
configuration. Native SDK 30 video/shade support remains unqualified.

Requires .NET 10 and Crestron Home NUnit 2.3.0 for the existing
Automation/SubmissionTests consumer. The configuration interface is unofficial,
undocumented and firmware-dependent. No production driver or device-client
payload changes are included.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are
trademarks of Crestron Electronics, Inc. This independent project is not
affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.
