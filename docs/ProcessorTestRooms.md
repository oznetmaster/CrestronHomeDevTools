# Named rooms at the processor-test boundary

All driver and library test plans select rooms by name. The workflow must not resolve, store or report a native room ID. `ProcessorTestRooms.RequireAsync` validates names through the current authenticated ConfigurationClient; `VerifyDeviceAsync` checks an installed device against a named room. `NamesForDevicesAsync` returns names for the selected native tree, while `AssignmentsPreserved` compares native assignments internally for cleanup.

`DriverInstanceLifecycle.EnsureInRoomAsync` resolves the name inside the processor-facing lifecycle operation, with lookup included in its cancellation/timeout scope. `ManagedDeviceValidation.RunInRoomsAsync` accepts `NamedManagedDeviceRequest` targets, resolves every room before commissioning any child, then delegates to the established guarded commissioning/testing/restoration/cleanup lifecycle. Public named bindings contain room names. Original native journals remain private lower-layer restoration evidence, not workflow input or a resolved room-ID report.

Name matching is case-insensitive for ordinary room names. Missing names, duplicate/case-variant matches and non-Room locations fail before mutation. There is no numeric fallback and no CI resolver. Existing device IDs and native configuration APIs retain their meanings.

DevTools 1.27.0 introduces these additive core APIs while its existing Automation consumer remains compatible with adapter 2.3.0. Publish the core through the existing normal release workflow, then publish NUnit adapter 3.0.0 against the verified public core, then migrate Automation through its normal follow-up release. Do not change all dependency pins simultaneously into a release cycle or create a special publisher. Normal public package and clean-consumer verification remain required.

Local software regressions exercise different native identities for one named plan, missing/ambiguous rooms before mutation, exact installed assignment and managed-child preflight. They do not constitute hardware execution or public publication.
