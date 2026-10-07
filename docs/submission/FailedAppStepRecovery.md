# Explicit replacement of a failed app-test step

This source feature is not available in DevTools 1.24.0. It repairs an already
failed run; it does not establish that the original workflow completed without
intervention. Keep that distinction in the validation report.

Ordinary worker recovery reconciles retained results and never repeats a started
physical test. When a fixture defect requires corrected code, use a separately
identified replacement attempt. Preserve the original source checkout, failed
outputs and completed earlier steps. Do not edit the frozen workflow settings,
delete an intent, or turn a failed result into a pass.

## Inspect before replacing

Stop the worker for the selected run and establish that no test process remains.
Use the same private settings and its independently retained SHA-256:

```text
CrestronHomeDevTools.Automation --inspect-app-step --settings PRIVATE_JSON --settings-sha256 SETTINGS_PIN --phase pre-endurance --step 0
```

`--phase` accepts `main`, `pre-endurance` or `post-endurance`. `--step` is a
zero-based index into that phase's configured app steps. The command requires a
stopped attention checkpoint, checks the retained target plan and earlier step
receipts, and holds the workflow lock. It prints the state hash, original evidence
hash, failed outcome path, restoration requirement and original plan. Inspection
does not execute tests, clear reservations or request an operator action.

Review the original failure, including app/device restoration and reservation
disposition. If restoration, cleanup or release is unconfirmed, independently
verify the original physical and app state and reconcile only the reservations
owned by that stopped attempt. Retain the verification and release receipts.
Neither elapsed time nor a stopped process establishes restoration. A JSON flag
alone is not verification evidence.

## Prepare a pinned replacement request

Save a private JSON request using these fields (PascalCase):

| Field | Required value |
| --- | --- |
| `SchemaVersion` | `1` |
| `Phase`, `Step` | The inspected phase and index |
| `AttemptId` | A new lowercase GUID without hyphens |
| `StateSha256`, `OriginalEvidenceSha256` | Exact inspected hashes |
| `FailedOutcome` | The latest retained failed `InstalledDriverTests.json`, relative to the step directory |
| `Replacement` | The inspected `InstalledDriverTestPlan`, with only permitted changes below |
| `SourceSha256` | `WorkflowEvidence.SourceDigestAsync` digest of the replacement source roots |
| `ProfileSha256` | SHA-256 of the unchanged Android profile bytes |
| `Restoration` | Omit only when the failed result already confirms restoration, cleanup and release |

Permitted plan changes are the corrected fixture's `SourceRoots` and
`AndroidTests.Project`, plus a verified catalogue discriminator for the same
driver key and version. Keep the candidate package, processor, device identity,
profile, selected tests, time budgets and readiness requirements unchanged.
Build and validate the corrected fixture in a separate checkout before proceeding.

When required, `Restoration` contains `OriginalEvidenceSha256`, `VerifiedBy`,
`VerifiedUtc`, the verified booleans `OriginalStateRestored`, `CleanupConfirmed`
and `ReservationsReleased`, and an `Evidence` array. Each evidence entry has
`RelativePath` (relative to the step directory) and `Sha256`. The command verifies
and copies these receipts into the new attempt. The original failed flags remain
unchanged. This is an explicit reviewed reconciliation, not automatic verification
of the truth of arbitrary supplied statements.

Hash the completed request independently and run:

```text
CrestronHomeDevTools.Automation --recover-app-step --settings PRIVATE_JSON --settings-sha256 SETTINGS_PIN --phase pre-endurance --step 0 --recovery-plan REQUEST_JSON --recovery-plan-sha256 REQUEST_PIN
```

Run on the configured testing computer under its ordinary worker identity. If a
scheduled task hosts it, allow indefinite operator readiness waits; do not impose
a shorter external task execution limit. The selected fixture still applies its
active recording deadlines and Cancel/Cannot perform handling. Keep the configured
operator listener running on the controlling computer.

## What happens next

The command binds the new source, tool assemblies and request before invocation.
It retains a new producer under
`installed-app/recovery-attempts/ATTEMPT_ID/`, checks the live driver and Android
readiness, and runs only the selected step. A configured readiness prompt gets a
new identity; an old Ready or Done acknowledgement cannot authorize it.

A passed producer must also confirm restoration, candidate verification, cleanup
and reservation release. Only then does the command create the step completion
receipt and request ordinary workflow recovery. Restarting the normal worker is
a separate operational step. Earlier completed tests remain retained and are not
replayed. Endurance still waits for every prerequisite to pass.

Reinvoking a started attempt inspects its retained result and never repeats it.
An interrupted attempt requires reconciliation. A subsequent replacement can
follow only the latest completed failure, never branch around an unresolved
attempt. Failed replacement attempts produce distinct controller alerts even if
the original failure was dismissed.

Review uses the accepted replacement's observations and Android producer pins.
All original and replacement files remain hash-verified in the coordinator
inventory. The review also records superseded producer locations in
`review-inputs/replaced-android-producers.json`; excluding a failed producer from
the passing audit does not erase it or make it pass. Signing and delivery are
outside this recovery operation.

## Correcting a combined test arrangement

Power interruption and network interruption are separate workflow steps, including
when they use the same processor. Each has its own readiness and action prompts,
timing record, result and restoration. A PoE power cycle must not stand in for a
network-only interruption whose recovery interval includes processor boot time.

The source implementation supports an explicit schema 2 scope revision for an
already failed combined step. It retains the frozen original plan and failure;
it does not edit them. This is assisted recovery, not a clean rehearsal.

Use the same independently pinned recovery request and command, with
`SchemaVersion: 2` and a `ScopeRevision` object:

- `Reason`: why the original physical arrangement was invalid.
- `Invocations`: two to eight separately bound test plans. Each entry supplies
  `Tests` (`InstalledDriverTestPlan`), `Fixture` (private fixture JSON),
  `CredentialBindings`, `SourceSha256` and `ProfileSha256`.
- `Observations`: a complete mapping of the original step's configured review
  outputs. Each entry supplies `OriginalPath` (relative to the original AndroidUI
  directory), zero-based `Invocation`, and `RevisedPath` (relative to that
  invocation's AndroidUI directory).

The request's existing `Replacement`, `SourceSha256` and `ProfileSha256` fields
must match the first invocation. Every invocation retains the original candidate,
test selection, time budgets, operator inbox and evidence identity/policy. Processor
bindings must resolve to targets already declared in the workflow. Changed fixture
scope is explicit in the pinned request; review still requires every policy item.
Neither a missing mapping nor an unstarted second test can complete the step.

Each invocation records an intent before execution and a verified inventory after
success. Reopening the same revision verifies and skips completed invocations;
it never repeats a started invocation whose outcome is missing or failed. It
publishes a fresh readiness identity for each unstarted invocation. All credentials
are resolved before the first test starts. The revision completes only after every
invocation passes and confirms candidate verification, restoration and release.

Review follows each mapped observation to its actual producer and includes every
accepted producer's independent pins. Original failures and superseded producers
remain in the retained inventory. Do not use this operation to hide a failure or
relax a timing requirement. Do not start a physical test until its measurement
method and revised scope have been validated.

New schema-2 runs retain post-endurance failures in `FinalizeTests`, before document
preparation. Use that retained stage for post-endurance inspection/replacement.
Older schema-1 runs keep their original tools and evidence; they are not silently
migrated or replayed by the new worker.
