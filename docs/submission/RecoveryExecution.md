# Recovery observation and continuation

This describes unreleased recovery tooling. Use a build that contains these contracts; older published binaries do not necessarily support them. A passing tooling test is not live driver evidence.

## Before requesting physical action

Freeze the candidate, required assertions, timing policy, processor and device identities, topology and exact action. Network and power interruptions are separate scopes. A PoE-only connection may not permit independent network interruption: identify that limitation during preflight. Resolve credentials, worker permissions, discovery and app availability before Ready.

Ready may remain pending while the operator is away. The action request follows readiness and refreshed baseline capture. Each request names only the equipment in that scope and offers cancellation or inability with a reason. The operator acknowledgement is never used as an exact physical event time.

## Recovery observation

`SubmissionManualOutageHardware` calls the optional `ISubmissionManualRecoverySession` after connectivity is observed, before waiting for acknowledgement. The observer verifies the recovery epoch and starts one `SubmissionRecoverySession` owned by the recording, independently of the short-lived action acknowledgement task.

The session starts clock collection and functional observation concurrently. A delayed processor log must not delay the first functional checks. If the first observations precede the proven load boundary, the producer retains them and performs a fresh observation batch; it must not change their timestamps. The clock and probe tasks cancel together on failure and are joined before original-state restoration. Restoration has a separate budget.

Retain trigger/observation bounds and evidence receipt times separately. Local elapsed deadlines use monotonic timers. Hashing, exporting, screenshot packaging and final integrity checks do not move an already captured functional observation time. Failed later integrity checks invalidate that evidence.

New outage records use schema 3 with `functionObservationsAreWindows: true`. A successful check observed after the deadline is **inconclusive**, unless separate evidence establishes a defined failed assertion. Starting a check late does not prove that the driver recovered late. Earlier schemas remain readable with their declared semantics; do not rewrite historical records to change their verdicts. An earlier passing upper bound can remain applicable after a lower-bound interpretation fix, subject to explicit evidence review.

The recording reports a disposition separately from restoration: Passed, BehaviourFailed, MeasurementInconclusive, HarnessFailed or Cancelled. WaitingForOperator describes a pending workflow state, not a terminal measurement. Only complete passing evidence advances the acceptance gate.

A newly written Home load event may have an uncertainty interval that straddles the current observation time. Wait until its full interval is observable, retaining the original bounds; do not move the recovery anchor to the later read or extend the policy deadline. An interval entirely in the future remains invalid.

When investigating a recovery failure, retain diagnostics from each concurrent branch, including the operation, safe reason code and call sites. The recorder's outer stage alone may not identify the failing branch. Keep arbitrary exception messages, response bodies and credentials out of diagnostics. A connected recovery diagnostic can exercise clock collection, binding checks, authorized controls and restoration without an outage. Clearly identify its evidence as diagnostic only: it cannot satisfy an interruption requirement. Resolve reproducible tooling defects before requesting another physical action.

The owned session identifies failures using typed clock, binding, readiness and control phases. Before cleanup, the recorder writes a failure journal with both the stage it was awaiting and the actual failing phase, the original exception type, an allowlisted reason and call-site lines. Cancellation remains cancellation. Never include arbitrary exception messages, response bodies or exception data in this journal. A journal-write failure is recorded separately and must not replace the original failure.

During recovery, an API response may contain incomplete startup configuration. Wait within the existing observation budget for the exact approved identity and configuration hashes before issuing controls. Retain distinct observations. A persistent mismatch fails. A producer may retry explicitly classified startup HTTP errors while reading inventory or configuration, within the existing observation budget; retain each retry diagnostic. Authentication, authorization and unclassified errors still fail, cancellation propagates, and control commands must not be replayed by this read retry. This wait does not extend the measured recovery deadline. The preflight still requires a complete matching baseline before any interruption.

## Preserve source identity during diagnostic builds

When preparing an isolated fixture checkout, copy its source-control metadata rules as well as its code. Generated `bin` and `obj` files must remain excluded from the source inventory; the separately pinned producer manifest covers the actual binaries used by the test. A missing ignore file can make an ordinary discovery build change the source digest after a passing test.

Before reserving equipment, compare the source digest before and after discovery using the same dependency bindings and build options as the worker. A changing digest is a preparation failure. Inspect the changed paths; do not weaken the source check or automatically exclude unexplained changes. If an integrity failure occurs after execution, retain the original failure and reconcile the exact owned reservations only after confirming restoration and cleanup. A passing NUnit case alone is insufficient to declare the enclosing workflow successful.

## Retain a passed scope after another scope fails

Use the existing explicit `--inspect-app-step` and `--recover-app-step` workflow. Inspect the latest terminal outcome and pin the reviewed state and evidence. A scope-revision invocation can contain a `Retained` reference:

```json
{
  "AttemptId": "<original lowercase 32-character attempt ID>",
  "Invocation": 0,
  "VerifiedSha256": "<SHA-256 of the original verified.json>",
  "Reason": "Explain why this passed scope remains applicable after the repair."
}
```

The surrounding invocation must retain the original complete test plan, fixture, source/profile pins and credential-binding path. The worker verifies the original receipt, successful result, cleanup, equipment/candidate/policy bindings and evidence inventory before requesting any new action. It does not execute the retained invocation or copy its files to pose as a fresh test. Completion references the original producer path and receipt. An incompatible or altered reference fails before a physical request.

Unresolved scopes use fresh attempt evidence and fresh readiness. A failed or interrupted physical invocation is never implicitly replayed. Unknown outcomes require reconciliation; successful observations do not substitute for unconfirmed restoration. The completed aggregate retains links to both original failures and accepted successes. Evidence inventory applies the 4,096-entry bound separately to each original operation and each retained recovery attempt, with at most 128 attempts per step and 128 steps. Parent phase aggregation preserves these same step and attempt boundaries; it does not apply a single-operation limit to the combined history. Only the defined step and recovery directory layout creates a separate bound. It preserves the same complete, sorted file receipts and link rejection; accumulated retry history must not prevent inspection merely because several individually valid attempts exceed one operation's limit.

## Operator status and unattended progression

The worker advances ordinary stages without AI prompts. Recovery notices distinguish an unproven measurement from a test-tool or behavioural failure and report whether restoration and cleanup were confirmed. A notice stating that no physical action is currently requested must not be interpreted as a retry request. Terminal results remain available even if the worker has exited.

Rehearsal endurance starts only after the full initial gate passes. A configured rehearsal proceeds through its declared endurance, postchecks and review, then hands off to protected signing and test-mail delivery. Approval waits are unfinished states; completion requires delivery confirmation and retention. Do not use a progress monitor as a substitute for repairing a stopped worker.



The endurance prerequisite gate and final review use the same accepted replacement mapping and producer-relative evidence paths. A successful recovery remains tied to its retained lineage and file hashes at both boundaries; old failed observations remain available for inspection and are never silently relabelled. Validate the aggregate and the full prerequisite report before handing an already completed physical test back to the ordinary worker.

An app phase without an explicit step list is treated as step zero by the same inspection and recovery commands, including `--phase post-endurance --step 0`. Replacement changes only the reviewed fixture source locations; it keeps the processor, selected cases, candidate, profile and timing. On continuation, the worker verifies and reuses the completed replacement receipt instead of rerunning the original failed suite. Response comparisons resolve accepted replacement measurements through their retained lineage and record the actual source paths. They never fall back to an original failed measurement when an accepted replacement exists.

A repair to one fixture does not update independently frozen fixtures in other phases. Before continuation, inspect the postcheck producer's project and dependency bindings as well as the worker tool. Keep original sources immutable and use a separately validated replacement when a later phase still refers to the older code.
