# Submit a driver: the normal path

Use the [execution protocol](ExecutionProtocol.md) for phase exit conditions,
bounded repair and explicit-stop handling. Use [Transfer.md](Transfer.md) for a
new task/model. This schedule applies only after intake establishes a feasible
route for every required measurement and the selected delivery payload. A
blocked prerequisite remains blocked while independent preparation continues.

The target is **delivery within 48 hours of the submission request, including a
24-hour endurance test, with at most one coordinated human interaction period**.
Preparation counts. This is our operational target, not an additional Crestron
requirement or a reason to weaken a result. A product defect or an unavailable
prerequisite is an explicit exception, not permission for days of silent drift.

Submission is optional for actual drivers. Library, client and processor-test
releases remain independent. Delivery means a verified package upload and SMTP
acceptance of the approved email; it does not mean Crestron acceptance or publication.

## Schedule and ownership

| Elapsed time | Work | Exit evidence |
| --- | --- | --- |
| Hours 0-8, target | Reuse saved setup; freeze scope and candidate; run local/processor checks; verify observation, cleanup and document/delivery preparation | Functional baseline, one coverage table, prepared human sequence, collector ready |
| Hours 8-32, target | One 24-hour interval; prepare/reconcile documents and independent checks concurrently | Actual full interval, periodic functional evidence and endpoint result |
| Hours 32-40, target | One scheduled human period for required actions, restoration, final checks and exact submission approval; then delivery | Final reviewed artifacts, authorized signature and provider receipts |
| Hours 40-48 | Contingency for an identified issue | Delivered, or an explicit exception with cause, owner and remaining task |

Arrange the human period to match actual availability. A period contains several
short readiness/action/observation/approval exchanges, not one ambiguous blanket
instruction. Do not hold the human on call throughout endurance. If an unavoidable
human baseline and endpoint must be a day apart, identify that conflict before
starting; do not claim the one-period target has been met.

Use one responsible operator and one concise state record: candidate, stage,
active operation or none, latest verified result, blocker, next action and evidence
links. Continue completed stages immediately. A periodic monitor observes state;
it is not the engine that advances the submission. An idle/failed operator is not
progress. Retain historical status separately instead of accumulating contradictory
current/next/blocker values. At hour 8, report any risk to starting the interval;
if it cannot start by hour 20, the normal four-hour closeout allowance no longer
fits inside 48 hours. Raise the specific exception then, not after the deadline.

## Set up once, reuse each time

Use the existing authorized computer/account, processor and app route. Verify the
few facts that can change: resource ownership, candidate identity, connectivity,
writable output and restoration access. Read saved developer facts and credentials
through their supported bindings. Do not ask again for established access or
restrictions. One console, one processor and an app observer where needed are
sufficient; optional CI, alternate hosts, OS-reboot rehearsals and new service
accounts are not prerequisites for a normal submission.

Pin a known working complete console bundle and matching documentation. Run its
runtime check when the installation changed or its health is uncertain, not as an
excuse to migrate a running attempt. Use the normal existing fixture frameworks.
Infrastructure development and full worker rehearsals belong to separate work.
Relevant references: [console](ConsoleTools.md), [saved setup](SetupApp.md),
[execution reliability](WorkflowReliability.md), [optional CI](WorkflowSetup.md).

## Prepare each candidate

Before hardware activity, make **one coverage table** for the exact official form:
official wording; driver capability/applicability; existing evidence and version;
planned method; pass/N/A/unverified/failure/qualified interpretation; remaining action.
Include README/help features, optional controls and group/list pages. Do not invent
exhaustive physical testing of every supported model. Source and automated tests
can support their actual scope; they do not prove unperformed physical actions.

Settle the distinction between not applicable and not verified. A supported control
without available hardware is unverified, not globally N/A. A documented platform
singleton does not automatically acquire a two-root-instance test because it has
several children. Preserve a reviewed applicability decision unless new evidence
or a changed requirement justifies reopening it. Keep official numeric criteria
separate from diagnostic thresholds and recorder deadlines. See [coverage](CoveragePlanning.md).

Freeze the release package, source and dependency identity, embedded help and
reviewed policy. Reconcile prior evidence before requesting new observations.
If the candidate changes, review the affected behavior once: repeat only justified
affected checks, preserve historical versions and disclose limits. A form, help or
tooling correction alone does not demand another 24 hours. A runtime change is not
automatically exempt either. See [prior evidence](PriorEvidence.md).

**Endurance entry is technical readiness plus a complete plan, not completion of
every independent physical checklist action.** Establish installed identity,
stable configuration, an actual functional baseline, suitable periodic checks,
continuity evidence and a planned endpoint/performance assessment. Unattended tests
must have passed to the extent needed for that baseline. Book outstanding human
checks for the final period and keep them visibly pending; this is scheduling, not
a passing result. Perform the endpoint and any before/after comparison before an
outage, removal or other action that would disturb the endurance evidence.

**Current automation limitation:** `AutomationPreEndurance.Check` requires all
initial policy observations, allowing only endurance, configured comparison and
removal to be deferred. It cannot currently defer an arbitrary planned human
outage/control check or accept a declared gap at that gate. Do not weaken its
policy, fake observations or patch a frozen checkpoint to get through. For a new
run needing the final-period schedule, use the established direct console/API
route with the explicit readiness/coverage review above and the existing collector's
own checks. A frozen worker run retains its contract; changing routes requires
settling that run and preserving its evidence. The worker's gate is an implementation
restriction, not a universal Crestron ordering rule. See [worker contract](AutomationWorker.md#required-checks-before-endurance).

## Endurance: one interval, useful work alongside it

Start once; schedule collection using the existing validated route. Retain the
first actual sample, 24-hour endpoint, approved observation cadence, candidate and
continuity checks. A ping or Ready flag alone is not a functional test. Use suitable
driver-specific reads/independent comparisons; physical motion or human attendance
is not inherently required for every periodic sample. A final performance claim
still needs evidence, not merely elapsed time. See [collection](EnduranceCollection.md).

Keep the producer and candidate stable. Prepare the form, annex, email, package
inspection, applicability and delivery payload during the interval. Do not perform
interruptions/removal within it. Do not wait for a human or an AI wake-up to collect
the endpoint. Preserve every sample and failure; a monitor fault is not necessarily
a driver fault. If the strict collector cannot export, retain that outcome and
assess originals honestly through the supported reviewed-evidence route. Never
fabricate a passing export, concatenate interrupted intervals or restart by default.

## One prepared human period

Before inviting the human, finish builds, discovery, source review, navigation and
observer setup. Have all targets, original/restored states, outage method, timing
method and action prompts ready. Batch compatible checks into one app session.
Use the existing supported observer; do not build new timing machinery after Ready.

First capture endpoint functionality/performance while the endurance configuration
is intact. Then perform only the remaining applicable physical/control/outage and
removal checks, restoring each target. Readiness is not the action: give separate
precise prompts for each interruption and restoration. Keep the human's job to
observation or the physical action; the agent records app/API evidence. No extra
visit merely to satisfy an invented timing rule, unavailable model or optional
group test. Ask only if an actual uncovered requirement remains. See [operator steps](OperatorSteps.md).

If the prepared observer fails, restore first and diagnose the failure. Do not turn
the session into a cycle of new launchers and repeated physical actions. Preserve
what succeeded; finish with a truthful qualification if the developer chooses it,
or report the specific exception. A separate test must not erase an earlier pass.

## Reconcile, approve and deliver

Generate the form from the coverage table and actual evidence. Check every official
row against the final PDF's canonical fields and rendered marks. Keep checked scope
notes, N/A reasons, unverified features and genuine failures consistent across form,
annex and email. Complete technical review before presenting it. Use concise outward
explanations; retain internal receipt inventories and tool failures privately unless
needed to explain the claim. Do not edit already sent artifacts to apply this guidance.

Arrange one review period, with the exact authorization exchanges required by the
selected route. The standard complete-mode flow requires signing authorization for
the exact unsigned form, then review of the produced signed copy and a separate
delivery authorization bound to its actual hashes. These can occur in one period;
they are not one interchangeable approval. Do not manufacture future signed hashes
or assume an unsigned-packet approval satisfies that tool contract. A separately
validated route may support explicitly approved signature/date and verified-URL
substitutions; establish that capability during preparation. Signing alone is not
sending authority. Material content/recipient changes require new approval.

Use the supported [declared-gaps route](DeclaredGaps.md) only when the developer
chooses it, never as a shortcut for unfinished authorized work. Review the concrete
artifacts and limitations, not an empty form or a technical approval packet.

Before upload, prepare and parse the complete outgoing MIME message locally with a
non-sendable URL placeholder. Verify CRLF/LF handling, exact sender/recipient/subject,
body and **every intended attachment**. The stock SMTP provider accepts one PDF;
when more are approved, select the existing validated transport that supports them
before the human period. Never silently drop annex/help or construct an untested
mailer at the send step. See [SMTP](SmtpDelivery.md).

After explicit approval, sign/date and check the signed appearance and unchanged
content. Upload the exact package, download and hash-check it, insert its verified
URL and send the approved message. Use one [delivery journal](DeliveryJournal.md).
An uncertain provider result requires evidence-based reconciliation, not a fresh
journal or blind retry. A local parsing error before SMTP does not require reuploading
an already verified package. Retain the failed attempt separately.

## Close once

Verify the retained sent message, recipient, body, attachment bytes and SMTP
acceptance; record the upload URL/package hash. Verify task-owned resources and
processes are settled. Stop only this attempt's monitors/listeners, retain originals
and report delivery once. Do not schedule further hardware tests or recreate a
completion monitor. Record any later Crestron response separately.
An earlier failed worker remains failed when a separate reviewed route completes
delivery. Link its recovery receipt and retire only its dedicated observer; do not
rewrite its outcome to silence alerts. Preserve registrations and diagnostics and
check that retired task-owned observers cannot restart at sign-in or boot. Keep
shared persistent listeners needed by other active work.

This revised schedule is operational guidance, not a claim that the preview worker
already implements it or that a 48-hour/single-period full run has been demonstrated.
[Validation status](ValidationStatus.md) and each selected tool's real contract remain
explicit. Missing capabilities become bounded engineering follow-up, not new hidden
submission requirements.

## Compare values according to their meaning

Choose and document comparison semantics when authoring a fixture or workflow guard; do not rely on the language's default string comparison or make every string case-insensitive.

- SHA-256 pins: validate the required hexadecimal length and syntax, then compare decoded digest bytes. Uppercase and lowercase hex represent the same digest; changed or malformed digests must still fail. This does not permit changing the pinned file bytes.
- Presentation text: when casing is only a rendering choice and is not part of the requirement, use an explicit ordinal case-insensitive comparison, or pin the exact observed rendered value. Check an authentic hierarchy before live execution; a model/property value may render differently. Preserve exact resource, application, front-layer, uniqueness and expected-value checks. Do not use fuzzy matching or broadly strip whitespace/punctuation to make a failed guard pass.
- Credentials, tokens, opaque identifiers, resource IDs, paths and protocol values: follow the field's documented contract. Preserve case-sensitive equality wherever required or unspecified; do not lowercase them globally. UI presentation rules do not change the semantics of embedded values such as endpoint paths or identifiers.

Validate the chosen comparator offline with permitted casing variants and a genuinely different value that must fail. A corrected comparison must preserve the original failed evidence and candidate identity; it is not permission to replay uncertain input. See [Android fixture guidance](AndroidEvidence.md#fixture-string-comparisons).
