# Correcting a failed phase-two run

A failed test result is immutable. A corrected test implementation does not make an earlier result pass. Document preparation must continue rejecting that result, including when an older coordinator already wrote a completion receipt.

## Select the affected evidence, not an entire rehearsal replay

1. Identify the original producer, candidate, policy, test implementation, measured outcome and retained failure. Diagnose the failure before selecting a replacement execution.
2. Record the changed implementation and its software validation. Keep acceptance limits unchanged unless a separate, explicit requirements decision creates a new test plan; never apply new limits retrospectively.
3. Work out the dependencies of the affected test. A before/after endurance comparison requires matching candidate, device, control/request and measurement method, with both observations bracketing the same completed interval. A new measurement method therefore requires a new matching baseline, interval and post-interval measurement. A new post-interval measurement cannot use an incompatible old baseline.
4. Preserve every original passing and failed result. Reuse unaffected passes only through an explicit scoped prior-evidence/change-impact decision in the new pinned test policy. The existing prior-evidence importer retains original files and labels accepted reuse as ReviewedPriorPass, not a new execution. Failed originals cannot be imported as passes.
5. Recheck which setup, restoration, placement and removal evidence depends on the proposed new execution. Earlier cleanup does not prove cleanup of a later installation. Do not repeat unrelated physical recovery tests simply because they share a processor.
6. Execute only the explicitly selected continuation with its own original NUnit/TRX results and evidence. Do not edit the old checkpoint, overwrite its assessment, synthesize a passing TRX, or use a document-preparation command to repair tests.
7. Phase two must assess the complete resulting requirement set and produce an accepted assessment and final-test receipt. Only then can phase three prepare documents from those accepted observations.

## Current support and limits

The generic workflow already supports scoped prior-evidence imports and verified reuse of completed stages, plus explicit recovery of selected ordinary postcheck cases. These are not a general command to replace a failed finalized assessment. A complete continuation across a changed measurement method still needs an independently pinned new plan and producer setup; it must not be inferred by the phase-three worker.

The current Kasa/Tapo retained policy contains a timed continuity requirement and a separate untimed response-comparison requirement. These two requirements are not evidence that two independent endurance executions are implemented. The native fixture currently exposes one explicit Endurance test. Complete coverage and any second explicit test must be established from the full submission contract before claiming the requested test-suite migration is complete.

Software-only package, NUnit selection and controlled-mail tests validate tooling behavior. They do not replace the retained live test result, certify hardware behavior, authorize signatures, or prove delivery of a real rehearsal submission.

## Read-only continuation inspection

The source-preview automation worker exposes:

```text
--inspect-test-continuation --settings FILE --settings-sha256 PIN
  --state-sha256 PIN --composition RELATIVE_FILE --composition-sha256 PIN
  --changed-requirements JSON_ARRAY --changed-requirements-sha256 PIN
```

The composition and its exact original policy.json must already be retained together inside the run. The changed-requirements file is an explicitly selected JSON array of requirement IDs; use an empty array only when no implementation change is being proposed. The command holds the existing run lock, checks the inspected checkpoint, settings, composition and policy identities, recomposes original evidence without writing it, and emits a planning report. Store each report separately.

NewExecutionRequired includes nonpassing, missing, inadequate or explicitly changed tests. A response comparison and its configured interval are treated as a single before/interval/after dependency for replacement execution. PriorPassReviewRequired means an unchanged native pass is a candidate for an evidenced change-impact review, not that reuse is already approved. Previously imported passes and applicability decisions require their original source review. Placement and cleanup are marked InstallationReviewRequired when new execution is proposed: prior removal does not prove removal of a later installation.

The report always declares PlanningOnly and denies execution and phase-three authorization. It does not produce a new acceptance receipt, bypass producer authentication, import observations, modify a failed checkpoint, or start tests. Preparing and executing the new pinned continuation remains separate work.

QualifiedEvidenceReviewRequired uses the same explicit rehearsal-qualification rules as the phase-two gate. A completed one-hour rehearsal is not automatically repeated solely because the production minimum is 24 hours; its limitation remains visible and cannot count as a production pass. A changed response method still makes the matching baseline, interval and final measurement dependent on new execution.

## Select complete producer cases

A changed measurement can share a test case with otherwise unchanged control assertions. Include every configured assertion emitted by the selected replacement case in the fresh selection. Do not import its older assertion and also compose its new output. For example, the Kasa/Tapo basic and energy outlet cases produce response samples and twelve configured control assertions; selecting those two cases makes those twelve assertions fresh alongside the coupled response comparison and interval. Unrelated outage tests remain separate. Trace an already imported pass back to its native original policy and observation document before a new scoped review; never chain review outcomes.

## Add missing performance recordings before assessment

At the waiting phase-two performance assessment boundary, an explicit `--capture-performance --settings FILE --settings-sha256 PIN --capture-plan FILE --capture-plan-sha256 PIN` command can collect additional recordings from selected ordinary postcheck cases. It binds the inspected state, accepted endurance and post-test receipts, unchanged candidate/profile/device configuration, selected original response pairs and reviewed fixture source. Explicit cases and physical operator steps are rejected.

Each attempt retains its own binding, original test results, restoration checks, complete producer inventory and receipt in `performance-captures/<attempt-id>/`. An interrupted invocation is never replayed automatically; an unresolved attempt blocks another attempt and final assessment. The accepted initial, endurance and post-test results stay intact. A later assessment can cite this additional evidence only for its matching after-endurance response pair. Original before/after measurements remain in the comparison, and additional measurements must identify the same candidate, device, controls and method. Longer video capture is an evidence change, not a looser response limit.

This is not a way to replace a failed finalized assessment or to approve a driver. Phase two still needs a supported qualitative finding and complete cleanup; phase three remains separately gated.

A capture command owns the checkpoint for its invocation. Stop an idle IDE/CI assessment waiter before collecting; do not run two owners simultaneously. If inspection proves the producer never created its mandatory pre-input output directory, `--close-unstarted-performance-capture` can retain an explicit closure of the original binding, fixture settings and intent. It requires the same pinned settings/request and an explanatory `--reason`, refuses any producer or readiness directory, and never marks a test passed. A later capture uses a new attempt ID. Once any producer execution exists, ordinary execution/restoration reconciliation is required instead.

A terminal failed supplemental capture can be closed with `--close-failed-performance-capture` using the original pinned settings and capture plan plus an explanatory `--reason`. Inspect the actual worker termination and original failure first. Closure requires the retained failed result to confirm restoration, cleanup, candidate identity and reservation release. It hashes the original producer, readiness and identity files and preserves the failed verdict. Those files remain part of the final comparison evidence. A closed attempt cannot be replayed; another observation requires a separate explicit attempt. Successful captures cannot use this command.
