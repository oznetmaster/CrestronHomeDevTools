# Bounded outage evidence

Status: importer and recorder available starting with 1.21.0. Concrete live hardware bindings still require validation. Planned [operator steps and separate-processor initial tests](OperatorSteps.md) are source additions for the next batched release.

`submission-import-outage-evidence` turns a retained, measured interruption into a normal `SubmissionEvidenceDocument`. It validates evidence and timing; it does **not** disconnect equipment, gather a processor log, operate the app, or establish that a particular hardware scope satisfies the official checklist. The source library also provides `SubmissionOutageRecorder` for capture sequencing and restoration. Its concrete hardware bindings must be implemented and validated before starting the workflow. Neither the importer nor the recorder's offline tests prove a live equipment test.

## Recording from an initial NUnit fixture

### Planned manual interruption

The next batched source release adds `SubmissionManualOutageHardware`. An initial fixture
supplies an `ISubmissionManualOutageObserver` and `SubmissionManualOutageSettings` containing
the exact shared operator inbox, target, disconnect/reconnect instructions and response timeout.
Wrap that binding with the existing recorder. It publishes **one coordinated disconnect request
and one reconnect request for all selected components**, including when the first operation was
cancelled or failed after the operator may have acted. It does not require a remotely controlled
power supply. The operator desktop and evidence collector must remain connected.

Before hardware access, verify the retained policy bytes against the plan's policy digest and
call `SubmissionOutageEvidence.ValidatePlanPolicy`. The importer repeats this check, but an
invalid duration, recovery limit or execution contract must be caught before asking for an outage.

The observer independently checks every bound endpoint, watches for an early return during
the hold, checks real recovery functions and restores original device/app state. Acknowledgement
alone cannot complete a transition or pass a test. An early return or stopped hold observer prevents
a pass, but still requests reconnection and performs final restoration. The physical action is an
operator attestation; connectivity samples alone do not prove electrical isolation. Instructions
must name all equipment, require confirmation only after completing the action, and require
leaving it disconnected until the reconnect request.

The retained event interval runs from publication of that request through the later of confirmation
and independent observation. It deliberately includes operator delay. The transition record embeds
the request, response and bounded raw connectivity observations (maximum 64 KiB per component),
so those inputs remain interpretable after the inbox task is cleaned up. This is conservative manual
timing, not a controller timestamp. Functional and program-load evidence remain separate obligations;
a missing program-load marker still produces Partial. Configure a separately authorized processor
and explicit protected-host exclusions in the concrete fixture before publishing any request.

Offline tests cover grouped prompts, acknowledgement without observation, cancellation, Unable,
early return, incorrect scope and restoration. Actual operator-desktop and equipment validation are
still required. This source addition is not yet proof that a live rehearsal has succeeded.

### Recorder lifecycle

`SubmissionOutageRecorder.RecordAsync` accepts the reviewed measurement plan, a trusted
`ISubmissionOutageHardware` implementation, a new evidence directory, an observation
timeout and a separate restoration timeout. Call it from the workflow's reserved
[additional initial fixture](AutomationWorker.md#additional-initial-fixture), before endurance.
The bindings must identify exactly the plan's component and function names; unknown,
missing or duplicate bindings are rejected before hardware access.

The recorder performs these steps:

1. Run the binding's read-only preflight and capture original state.
2. Retain an intent before each interruption command, then retain its evidenced event bounds.
3. Wait at least the plan's minimum interruption after all components acknowledge interruption.
4. Restore every attempted component, including a command that threw after possibly changing equipment. A failed restoration does not suppress the other restoration attempts.
5. Capture the program-load marker when required, then run each named functional assertion.
6. Restore and independently compare the original device/app state, including collateral changes.
7. Assess the retained measurements through the same conservative timing validator used by the importer.

Cancellation and observation timeout still enter restoration. Each component's connectivity
restoration and the final state restoration have separate bounded budgets, independent of
caller cancellation. One component timing out cannot cancel restoration of the next component.
Bindings must honor cancellation; the recorder cannot safely force-stop a transport that ignores it.
Process termination or host power loss cannot execute `finally`: retain the incomplete directory
and require inspection/restoration, never automatically replay the interrupted test. A hardware
controller that can restore independently of the test host is an external prerequisite where
loss of control would otherwise leave equipment disconnected.

Progress captures are immutable numbered JSON files, flushed to disk. An existing output
directory is rejected. The recorder verifies capture file hashes and timestamp ordering before
continuing. It stores exception types and the failing phase, not arbitrary transport exception
messages that could contain credentials. Bindings must themselves keep their raw captures free
of credentials. They retain raw controller/processor/app evidence under the supplied directory;
the recorder does not authenticate a provider's factual assertions merely by hashing them.

On a completed capture, `measurements.json` and `assessment.json` are retained even when the
measurement is Partial or Failed. `recording-result.json` names the importable record. If an
operation or recording step failed, that path is null and the partial progress and error types
remain available. `Passed` is true only with no recording errors and a passing measurement.
If disk writes fail entirely, the method can throw after restoration; absence of a final result
must never be interpreted as success.

Import any available record using its independently retained plan and record digests, preserve
nonpassing observations, and make the NUnit fixture fail when `Passed` is false. Include the
resulting observations and raw captures in its normal producer inventory. No standalone CLI
hardware provider is installed by this API.

### Hardware binding prerequisites

- Control must remain available when the selected processor and device are disconnected. Do not use the processor being interrupted as the sole controller for its own restoration.
- Capture actual power/network transition bounds from the controller or instrumentation. Ping failure and a later operator acknowledgement do not establish electrical state or an exact restoration instant.
- Establish the continuous interruption interval, including any unexpected early restoration. The recorder's hold delay alone does not prove equipment stayed disconnected throughout it.
- For power recovery, retain a new program-load marker and account for processor clock uncertainty. A successful API call is not that marker.
- Check real functions and visible app feedback after recovery, using the selected candidate and device identities. A listening port alone is insufficient.
- Capture and restore all affected device states, app navigation and collateral equipment. Validate the binding with the actual worker account and its permitted equipment before freezing the run.

The recorder currently has synthetic regression coverage for sequencing, cancellation,
interruption/recording/restoration failures, timing limits and normal evidence import. Concrete
hardware providers and their live verification remain required; do not remove planned gaps
solely because this shared recorder is available.

## Plan before interrupting equipment

Pin a `SubmissionOutageMeasurementPlan` independently of the producer's result. It contains:

| Field | Contract |
| --- | --- |
| `identity` | Exact package, source, policy and official template digests. |
| `requirementId` | One outage requirement in that policy. |
| `requiredComponents` | Every component whose interruption must be demonstrated for the selected method. |
| `requiredFunctions` | Concrete functional assertions needed after recovery. Reachability alone is not functional recovery. |
| `minimumInterruption` | Required simultaneous interruption interval, serialized as a `TimeSpan`, for example `00:01:00`. |
| `recoveryLimit` | Recovery deadline, for example `00:01:00`. May not weaken the policy. |
| `recoveryClock` | `NetworkRestored` or `ProgramLoaded`; selected from the actual checklist wording. |
| `programComponent` | The interrupted processor component for `ProgramLoaded`; null for `NetworkRestored`. |

The policy must bind this requirement to `method: outage`, a `Passed` outcome, a positive response deadline and restoration verification. The plan cannot shorten its minimum duration or lengthen its deadline. This does not replace review of the [physical scope and permitted alternatives](CoveragePlanning.md#outage-scope-and-recovery-timing).

For example, the endpoint-disconnection network alternative can name processor and selected device. A power test that also requires network equipment must include that equipment. Do not omit a component because it is difficult to disconnect or add every access point when the selected network alternative does not require that.

## Capture actual bounds and functions

The producer writes `SubmissionOutageMeasurementRecord` schema 1 with the same identity:

- `interruptions`: one `{ component, interrupted, restored }` entry per required component.
- `programLoaded`: a separately evidenced program-load event for that clock, otherwise null.
- `functions`: one `{ id, outcome, observation }` per required functional assertion.
- `originalState`, `verifiedState`, `matchesOriginal`: captured baseline and independent final restoration check.

Every capture contains `earliestUtc`, `latestUtc`, and `evidence: { relativePath, sha256 }`. Bounds describe when the event could actually have occurred, not when an operator later answered a question. Retain the raw measurements supporting them. Keep all clocks synchronized or include their measured uncertainty in those bounds. If instrumentation cannot establish finite trustworthy bounds, record the missing component/function or missing program-load event; do not invent an exact timestamp.

The guaranteed shared downtime is the earliest possible restoration of any component minus the latest possible interruption of any component, clamped at zero. Sequential interruptions do not demonstrate a common outage. Insufficient evidence produces `Partial`, not a claim that the driver failed.

For network recovery, the clock is bounded by restoration of the last required component. For power recovery, use the program-load marker, with evidence that the processor's power restoration preceded it. Functional observations must follow the clock and restoration of all required components. The importer uses the earliest possible clock start and latest functional observation for the conservative response duration in the ordinary evidence document.

All functions must pass. Missing, inconclusive or partial results remain `Partial`. An explicit functional failure, unconfirmed original-state match (`matchesOriginal: false`), or an observation certainly beyond the deadline produces `Failed`. An interval straddling the deadline remains `Partial`. A late observed response does not by itself diagnose the cause as a driver defect: retain the polling cadence, host delays and original errors.

Do not substitute a successful ping, open port, readiness query, screenshot of a loading page, or old program-load marker for the required functional assertions. The importer cannot determine whether a producer truthfully captured a physical event. Producer authentication and semantic review remain responsibilities of the trusted workflow.

## Import and retain

```powershell
CrestronHomeDevTools.Console submission-import-outage-evidence `
  --evidence C:\PrivateRun\outage `
  --plan plan.json --plan-sha256 <reviewed-plan-sha256> `
  --record record.json --record-sha256 <authenticated-producer-record-sha256> `
  --policy policy.json --output C:\PrivateRun\outage-import
```

The existing private evidence root must contain the plan, record, policy and all referenced raw captures. The policy digest comes from the pinned plan identity. Hashes are checked against the bytes parsed; raw capture hashes and safe contained paths are checked. JSON inputs are limited to 16 MiB and individual raw captures to 64 MiB. Unknown/duplicate components and functions, mismatched identities, invalid event ordering and changed files are rejected.

The new output directory contains `report.json` and `observations.json`. Exit codes are 0 for a passing measured scope, 1 for retained `Partial`/`Failed` measurements, and 2 for invalid input or an output error. Existing output directories are never reused. The command leaves all inputs untouched and does not copy them: preserve the original evidence root alongside the outputs. Observation paths remain relative to that root, not the output directory.

Both passing and nonpassing observation documents retain references to the exact plan, record, policy and raw captures. A passing result must also satisfy the normal execution/evidence validator. A scope passing this command is not a complete checklist or authorization to sign, upload or email.

For automation, have the authenticated initial-test producer retain the inputs, captures and imported observations in its completed producer inventory, with correctly rebased paths under the run directory. Include the observation document in the review's `preEnduranceObservationSources` (and final sources). The normal [pre-endurance gate](ReleaseAutomation.md) checks that provenance and the full policy; an ad hoc file outside a completed producer inventory is insufficient. Never clear a declared gap solely because an importer command exists. Freeze the actual recorder/function bindings and validate their normal workflow execution first.

Synthetic tests exercise timing uncertainty, common interruption scope, restoration, provenance, policy binding, CLI outcomes and integration with the normal evidence validator. They are not hardware test evidence.
