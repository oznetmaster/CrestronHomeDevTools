# Reconciled development work

The development branch and its uncommitted files were reconciled with current
`main` on 8 November 2026. The published 1.28.0 release, its cloud-backup and
managed-child repair APIs, and all previously frozen submission artifacts remain
unchanged. This source reconciliation is not a new public release or a completed
live submission qualification.

| Area | Reconciled source | Validation and remaining limits |
| --- | --- | --- |
| Driver update management | Read-only installed-driver assessment, reviewed sequential updates, readiness observation, console commands and Windows checklist | Offline processor/consent/cleanup tests; actual multi-driver update and restart behaviour still requires controlled live qualification |
| Published catalogue search | `PublishedDriverCatalogue` and `published-drivers`, independent of local/sideloaded packages | Synthetic response/validation tests and anonymous Overkiz lookup; the portal catalogue does not establish Crestron Home compatibility |
| Submission evidence and review | Retained interrupted/history observations, scoped documentary reviews, pinned snapshots and signing-plan preparation | Offline .NET/Python tests; no new compliance passes, signature access, upload or email follows from reconciliation |
| Artifact delivery and packaging | Verified-byte publication, complete Python import closure, native output-path admission and private diagnostics | Source regressions and complete packaged-console checks are recorded in development history; old failed outputs remain private |
| Worker execution | Explicit local/shared input/output roles, host checks, private terminal diagnostics and collection verification | Offline real-file permission/path tests; this does not qualify every remote worker or grant new access |
| Processor compatibility | Fresh authenticated capability reports and explicit version-aware I/O preparation | Discovery verified on Official 4.12 and EAP 4.13; EAP accepted and saved a typed serial selection in an isolated SDK 29 mock, including `Serial`/`Com` normalization. The helper passed against its completed setting. Physical I/O, connected runtime restoration and 4.12 server assignment remain unqualified |
| Submission procedure | Execution/transfer protocol, coverage/run-state templates, bounded recovery and approval/continuation guidance | Documentation/process clarification; no full initial or update submission is newly certified |

The old checkout also contained stale copies of changes already integrated in
1.27/1.28: named-room helpers, managed-child room validation and the PKCS dependency
test. Those were not reintroduced as new features. The stale 1.27 release-notes
file was superseded by the existing 1.28 notes.

Existing runs continue with their pinned tooling, original evidence and approval
boundaries. Do not substitute this development build into an active submission or
interpret an offline passing test as a hardware result.

The original branch, tracked patch and file-by-file working snapshot were retained
before reconciliation. Unique changes are accounted for before the old checkout
is cleaned; archived originals are not automatically deleted.
