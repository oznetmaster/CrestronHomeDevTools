# Submission execution protocol

Use this with the [runbook](Runbook.md). These are operator procedures and exit
conditions, not a new execution engine, SDK schema or vendor certification policy.
Apply them to new submissions; do not rewrite an old run's frozen contract.

## 1. Establish scope and a finishable route

Create a private [current-state record](templates/RunState.md) and
[coverage register](templates/CoverageRegister.md). Read existing authorization
first. Do not request permissions already granted for unchanged work. Separate
ordinary preparation, physical actions, signing and external delivery. Record
bounded physical-action limits and consumed counts; restoration authority does
not replenish an exhausted test allowance.

During preparation establish the following facts, using saved configuration and
dated successful receipts where appropriate. Any live read/capture requiring a
reservation must acquire it through the selected route first. Fresh readiness and
ownership are checked under that reservation immediately before the action.
Refresh only what can change or what failed. Record a specific unavailable prerequisite, not a generic
request for a new infrastructure rehearsal.

| Area | Required preparation evidence |
| --- | --- |
| Official scope | Selected original form/template and hash; exact row wording and alternatives; every supported/conditional capability accounted for; reviewed applicability and genuine gaps. Labels in a field inventory are not the full requirement text. |
| Execution | Selected route, host and account, tool/source identities, host-qualified paths, writable private output and collection, command/receipt contract, compatible installed dependencies. A successful controller-local command proves nothing about a remote target. |
| Hardware | Actual identity, intended installation, configuration baseline, effects of custom programming, readiness conditions, action scope and restoration route. Refresh dynamic facts immediately before action under ownership. |
| Native observations | Working installer and end-user UI routes where required, visible controls, capture and hierarchy retention, timing trigger/response method and evidence storage. No silent fallback to another computer or to an API for a native UI assertion. |
| Physical/outage checks | Exact targets and power/network topology, action and restoration method, surviving observation path, shared-equipment impact, time origins and bounded cleanup. A proposed router block is not measured isolation. |
| Endurance | Functional baseline, suitable periodic probe/independent observation, continuity and gap policy, endpoint functional/performance method, and automatic collection of the required endpoint. |
| Delivery | Matching official form/help tools, complete or declared-gap route, exact message/attachment capability, protected account and existing upload/journal route. Verify mechanics offline; do not make a live send or upload merely to rehearse. |

**Exit:** each required gap has an executable method using the selected installed
tools, or an explicit unresolved prerequisite and owner. Independent work may
continue, but an unresolved required method makes the full submission blocked,
not ready. Do not start endurance merely to create activity while the rest of the
submission has no feasible finish. An intentionally limited evidence-collection
run is possible when the user requests it; label it separately from a ready plan.

CI discovery starts in the configured central orchestration repository as well
as the driver repository. Read workflow labels, environments, schedules and gates
from the selected revision. An online/idle runner is neither an available processor
nor a configured private submission worker. A paused schedule or historical 403
comment is not a current failed manual run. Do not enable CI as a side effect of
discovering it. See [worker discovery](WorkerDiscovery.md).

## 2. Settle coverage before performing actions

For each official row, distinguish display inventory, navigation, command input,
backend state, physical effect, timing and restoration. A shared sequence can
support several rows when it retains each required observation. Do not multiply
physical actions merely because several rows reference the same behavior.

Enumerate conditional modes and dynamic actions (for example Start and Stop on
one control), and distinguish a text display from an actionable control. Define
the finite test sequence and repetition interpretation before execution. If the
form says 'repeatedly' without a count, record a justified method; do not attribute
an invented number to the vendor or discover the interpretation after the action
allowance has been used. Readiness checks must not silently send commands.

Use source/test/API/human evidence at its actual scope. An API-driven physical
effect does not prove the native button, and a native click does not prove a
physical effect. Missing hardware or a failed capture is not N/A. Preserve accepted
applicability unless a changed feature, requirement or contrary evidence justifies
a dated correction. Do not reopen it on every handoff.

**Exit:** all official rows and subconditions are mapped, selected cases are
finite, effects and restoration are known, and each remaining action advances a
named unresolved assertion. Inventory totals and passing test counts are not
substitutes for this review.

## 3. Freeze and prove the actual candidate

Review help early: use real captures, accurate behavior, configuration instructions,
limitations and support information. Render every page. Inspect the help actually
embedded in the package, including links and dependency notices. Build once and
record source commit, package hash, version/GUID, build inputs and installed payload.
Test-package success and a version string do not prove the installed production bytes.

Prefer the final help-bearing package before hardware testing. If documentation
must change later, retain both containers. Verify all added, removed and changed
entries against an explicit documentation-only allowlist; verify every runtime,
configuration, UI and other non-allowed entry is byte-identical. Revalidate package
structure and render the final embedded help. Bind the new outward package hash and
the scoped equivalence review without claiming that container ran on hardware.
If runtime-relevant bytes changed, assess affected checks; do not assert equivalence
or rebuild tested bytes and call them tested. Use [prior evidence](PriorEvidence.md).

For evidence obtained on an older installed version, keep that version visible.
Any equivalence claim names the particular unchanged behavior and comparison
evidence. Unchanged UI does not establish unchanged connection/polling behavior.

## 4. Execute one owned operation and close it

Acquire required resources in the route's established order. Keep exact owner IDs,
remote operation ID or PID plus start identity, original state and deadlines.
A foreign lease or ActiveTest marker is an admission blocker. Do not steal it,
expire it by age or infer process liveness from a marker. If acquisition failed,
record that nothing was acquired rather than claiming cleanup/release failure.

Before a physical action, use the existing observer to retain an actual harmless
capture/navigation result from the required page and verify the recording/output
path. Reuse a recent unchanged success; do not add a full rehearsal to every run.
Validate changed fixture mechanics offline. A failed scroll, stale selector,
capture exception, malformed receipt or immediate-snapshot race is a procedure
defect or unknown observation, not an automatic product failure.

For each consequential input retain intent, dispatch result, fresh observed effect
and restoration independently. Once dispatched, a lost screenshot does not make
the action unused. Unknown dispatch outcome is reconciled before any retry.
After an asynchronous setter, observe convergence with a bounded read-only wait;
do not resend it merely because the first read is old. Preserve official timing
criteria separately from transport timeouts and conservative command pacing.

Consume terminal receipts promptly. Verify primary result, final physical/app
state, original configuration and affected identities, cleanup, release and
archive retention separately. A released resource does not prove restoration;
an archive hash does not prove a test passed. If a process finishes while an
operator is idle, inspect its result instead of dispatching a replacement.

## 5. Bound failure and stop correctly

Default repair budget for a procedure fault: one diagnosed correction and one
bounded reattempt of the affected step, within existing authorization. This is an
internal operating limit, not an official test rule or permission to repeat a
physical action. First preserve the original, establish any uncertain action/state,
restore safely, and show how the correction addresses the observed fault. If the
same fault recurs, or the cause/correction is unproven, stop that live sequence and
report the concrete blocker. Move shared-tool development into separately scoped
work; do not grow a chain of near-identical launchers during submission.

For route discovery or evidence reconciliation that stalls, use a 30-minute
operator-work budget unless the user sets another. At the limit report what was
checked, the missing fact, its owner and the smallest next action. Independent
authorized work can continue. Time elapsed, an unanswered question or a heartbeat
is never approval. Known long tests use their declared duration, not this repair
budget. Do not keep rereading unchanged state and call it progress.

Distinguish these outcomes:

| Outcome | Required next step |
| --- | --- |
| Admission failure; nothing acquired/dispatched | Record blocker. Recheck only under a bounded, requested monitor or when availability changes; do not clean another owner's resources. |
| Procedure failure before dispatch | Preserve diagnosis; validate the correction; reattempt only the affected step within the repair budget. |
| Dispatched action; observation failed or uncertain | Reconcile original action and current state, then restore. Never automatically replay. |
| Observed product criterion failure | Preserve the failure. Scope product repair/change-impact work or report the qualified result; do not alter the criterion. |
| Cleanup or ownership uncertain | Prioritize reconciliation/restoration of this exact operation; no next test until resolved. |
| Required evidence missing | Mark unverified/qualified, with exact scope. The developer chooses any declared-gap submission. |
| Explicit user stop | Cancel pending work through the supported route, finish only necessary safe cleanup, pause this attempt's wakeups, record released/uncertain resources, then wait. |
| Uncertain upload/send | Use the same durable journal and original provider receipts. No new journal or automatic retry. |

A technical pause can continue within unchanged prior authorization after a
verified correction. An **explicit user stop requires an explicit user resume**;
technical review, delegated messages and stale schedules do not supply it. Do not
resume other operators, inspect devices or complete artifacts while stopped merely
because those actions are read-only. A separately requested documentation audit
can proceed without resuming the submission.

## 6. Endurance and monitoring

The direct collector and automation worker have different entry contracts.
`AutomationPreEndurance.Check` requires its initial observations and defers only
specified endurance, comparison and removal obligations. Select a compatible
route before launch. The direct route still needs a real baseline, full coverage
plan and viable endpoint; it is not a workaround for missing evidence.

Preserve the interval and original failures. At the scheduled endpoint collect
and review continuity, functional results and the planned performance comparison
before outages, removal or restoration disturb the setup. '48 samples passed'
alone does not prove every part of an endurance checklist row. Never restart or
concatenate intervals by default after a documentation or monitor fault.

Separate sampling, progress checks and user notifications. A monitor reads the
actual operation and exits; the operator advances work. The next check is the
earlier of its configured interval and the scheduled endpoint. Do not schedule
recurring wakes without authorization. End unchanged wakes quietly. Notify once
on meaningful progress, failure, completion or needed input; do not repeatedly
request an already pending approval. A failed/idle process is not 'working'.

## 7. Review, approval and closeout

Reconcile the register, official canonical fields, rendered marks and notes, help,
annex and email. Identify complete versus explicitly chosen declared-gap mode.
Keep whole-row qualifications until every applicable subcondition is supported.
Finish all authorized preparation before asking for exact artifact approval.

Select the installed signing/delivery contract during preparation. The standard
complete-mode flow authorizes the exact unsigned form for signing, then requires
review of the signed output and a separate delivery authorization bound to its
actual hashes. Both exchanges can occur in the same human period. Do not invent
future signed hashes, silently derive sending authority from signing, or promise
one-click delivery when the tool needs signed-copy confirmation. A different
already-validated route must explicitly support any approved transformations and
attachment set. Preserve valid existing approvals; material changes need review.

Parse the exact MIME draft offline before upload. After approved signing, inspect
unchanged content/marks and signature/date appearance. After authorized upload,
verify downloaded package bytes; substitute only the verified URL in the approved
message. Send through the guarded journal. Retain distinct prepared, signed,
uploaded, SMTP-accepted and vendor-decision states.

Close only after required resources are restored/released, original evidence and
provider receipts retained, and this attempt's monitors stopped. 'Delivered with
declared gaps' must not become 'all tests passed'. Document any residual cleanup
or provider uncertainty as open. Transfer through [Transfer.md](Transfer.md).
