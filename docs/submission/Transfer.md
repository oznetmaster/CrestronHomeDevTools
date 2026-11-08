# Transfer a submission to another task or model

This is a handoff procedure, not authorization to resume, create a task, delegate
or operate equipment. The human's latest instruction controls. Transfer only the
specific submission requested; do not carry another driver's approvals or fixtures.

## Prepare the handoff

Use one entry point: [START-DRIVER-SUBMISSION.md](START-DRIVER-SUBMISSION.md).
Keep the matching documentation and executable tool identities together. Include
the following private files or references; do not require the recipient to read
the whole conversation or hundreds of historical checkpoints to find current facts.

1. **Current state:** complete the [RunState template](templates/RunState.md), with
   one active stage, an explicit operation or none, dated observations, actual
   ownership, blocker, next action and latest user instruction. Replace its current
   fields; archive superseded snapshots separately. Link history instead of appending
   contradictory current/next values. Stop/resume authority is separate from stage.
2. **Coverage register:** use [CoverageRegister](templates/CoverageRegister.md).
   Include the exact official text, applicability decisions, demonstrated scope,
   current qualification, referenced originals and remaining action for every row.
   Preserve earlier failures; a later success adds evidence rather than deleting them.
3. **Immutable candidate and evidence index:** package/source/tool/fixture identities,
   original hashes, installed version used for each observation, reviewed equivalence
   limits and receipt paths. Copy exact files if moving computers; verify hashes.
   A hash shipped with a file checks transport integrity, not independent provenance.
4. **Environment bindings:** actual host/account, credential *references*, trust pins,
   installed tool paths, processor/app identity, resource protocol and demonstrated
   transport/capture route. Every path identifies its host. Dates make stale state
   visible. Do not include plaintext credentials or signature images in a general
   handoff; use the supported protected stores and access controls.
5. **Operations and authority:** process/task/remote operation IDs and start identities,
   scheduled endpoints, restoration obligations, action allowance consumed/remaining,
   exact approval evidence and expiry. Include the same delivery journal if external
   work has begun. An unknown operation remains unknown until reconciled.
6. **One next action:** name the command/API or read operation from the chosen tool
   version, inputs, expected result, deadline, failure branch and source of authority.
   If none is authorized, say 'Wait for the user'; do not supply an executable resume.

These Markdown records are operator notes, **not SDK settings, approvals or evidence
receipts**. Do not feed them to commands or generate passing observations from them.
They complement the selected tools' durable checkpoints; they never replace them.

## Recipient's first check

Read the current-state record and latest human scope before any side effect.
Verify supplied candidate/input hashes locally. Check only the live facts needed
for the next authorized action, through the established route. Do not treat this
documentation's machine examples, historical runner availability or previous
cleanup receipts as a current reservation.

- If stopped, confirm the handoff is stopped and wait. A request to audit the
  workflow is not a request to resume an old submission.
- If a known operation is active, inspect its actual process/start identity and
  original result. Do not launch a second collector or test.
- If a process has terminated, distinguish succeeded, failed and unknown effects;
  reconcile its receipt and ownership before advancing. Do not rebuild/replay.
- If there is no operation and prerequisites are met, continue the specific next
  authorized action. Preserve reviewed applicability and scope decisions.
- If a required artifact or binding is missing, name it precisely and continue
  independent authorized preparation. Never silently substitute a host, candidate,
  tool version, test route or signature authority.

## Copyable assignment for a new submission

Replace every bracketed item with facts before giving this to another task/model.
Do not insert credentials. This text by itself is not human authorization.

> Prepare the new submission for [driver and source/package identity] within
> [user-authorized scope]. Start with START-DRIVER-SUBMISSION.md, ExecutionProtocol.md
> and the private current-state record at [host:path]. The working tools and their
> documentation are pinned at [identities and paths]. Use [established execution
> route]; [delegation restriction]. The coverage register is [path]; evidence index
> is [path]. The current operation is [none, or exact identity and deadline].
> Current authority is [preparation/physical scope and bounds; signing/delivery
> authority or pending]. The next action is [specific action, result and failure
> branch]. Preserve tested bytes, original failures and the same delivery journal.
> Do not claim a whole-row pass from inventory/counts or replace native/physical
> observations with API results. Report a specific blocked prerequisite within the
> protocol's repair budget. Stop immediately on a human stop and perform only
> necessary safe cleanup. Finish authorized preparation before exact artifact
> approval; report provider acceptance separately from the vendor's decision.

For workflow-only maintenance, replace that assignment with: 'Audit/repair the
reusable procedure and documentation only. No submission resume, live device
contact, test dispatch, signing, upload, email or automation activation.'

## Transfer acceptance

The recipient should be able to answer from the handoff: What is authorized? What
is actually running? Who owns each resource? Which bytes were tested? Which
requirements remain? What is the next action and its stopping condition? Which
approval or provider uncertainty prevents delivery? If any answer requires
guessing, the handoff is incomplete. No live run is needed to check those questions.

Generic documentation can be copied with its relative directory structure and
version manifest. Distribute private run data separately through authorized access.
Mark whether a transfer was reviewed offline, exercised with synthetic providers,
or validated by an actual complete submission. Do not conflate those levels.
