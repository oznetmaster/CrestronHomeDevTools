# Planned physical actions

Available in 1.22.0. The shared request protocol, concurrent observation, desktop
inbox and CLI have offline coverage. A live desktop diagnostic verified two
successive requests through one persistent listener and cleanup of its validation
tasks. Live sensor/outage fixtures and the complete workflow still need validation.

A separate Windows-to-Windows diagnostic verified a synthetic request created by
the test worker, an action window on the controlling computer under its normal
signed-in account, a response written there and read back by the worker, and
automatic listener exit after closing the diagnostic inbox. It used the existing
authenticated private share. This proves that transport path, not actual physical
sensor/outage tests or recovery from a network interruption. Display formatting
changes described here are in the next release; the shared-inbox transport is
already available in 1.22.0.

A sensor press or unplugging a processor is a planned test action. It does not
require an AI session to advance the workflow. A fixture publishes an explicit
request, waits for an answer and then verifies the resulting device/app behavior.
An answer is neither a passing test nor authority to sign, upload or email.

## Fixture

Provision a private shared inbox writable by the test identity and the authorized
operator. Use a separate inbox per workflow attempt, retaining its contents with
the evidence. Keep credentials out of instructions. The run key is the workflow's
64-character lowercase hexadecimal identity.

Declare `OperatorInbox` in the worker settings as well as each participating fixture.
Release templates can use `${run}/operator-inbox` and `${runKey}`; normal intake expands
them once from the pinned release. Preflight verifies that main, additional-initial and
post-endurance fixture bindings all match the worker's directory and run key. It recognizes
the reserved `OperatorInbox` property and nested `Inbox` objects containing `Directory`
or `RunKey`. Mismatches fail before credentials or tests are opened; the tools do not silently
rewrite a fixture. This also ensures the worker closes the same inbox after review.
An operator desktop on another host may use a different mount path to that shared storage;
the worker and its fixture processes must use their same declared path.

```csharp
var handle = SubmissionOperatorStep.Create(inbox, runKey, "button-single-press",
    "Selected demonstration button", "Press the selected button once, then choose Done.",
    TimeSpan.FromMinutes(5));
var response = await SubmissionOperatorStep.WaitAsync(handle, cancellationToken);
if (response.Outcome != SubmissionOperatorOutcome.Done)
    throw new InvalidOperationException("Physical action was not completed.");
// Independently verify the new device event and visible app feedback.
```

Arm event recording before publishing the request so a quick action is not lost.
Retain request, response and actual observations. Do not interpret the response
timestamp as the exact physical event time. Do not reuse an earlier event or
accept an acknowledgement as functional evidence.

Requests are immutable and hash-bound. Responses are single-writer, retained and
validated against the specific request. Cancellation and expiry are terminal
outcomes. A restarted observer can read the same response; a restarted fixture
must recover its existing handle, not ask for a duplicate physical operation.
Permanent storage errors surface instead of retrying forever.

Any test that can leave equipment interrupted must use a separate restoration
budget and prompt to reconnect after failure/cancellation, then verify recovery.
A cancelled request is not proof of restoration. This protocol does not implement
the hardware-specific restoration or timing observations itself.

## Operator desktop

### Worker and controlling computer

Choose the computer on which the human will receive requests **before starting**
the rehearsal or submission. It can be a separate controlling PC; the test worker
does not need a person watching its desktop, and no AI session relays requests.
The listener opens the action window on the computer **running the listener**.
Running it only on a remote test worker therefore requires viewing that worker's
desktop through Remote Desktop or another remote-access tool.

For direct alerts on the controlling PC, provision authenticated private shared
storage and run the listener there. Both accounts need access to the same retained
inbox: the worker publishes requests and the operator writes responses. Keep
signatures and device/delivery credentials out of the shared inbox. Do not copy
requests to an independent folder: a copied response would not reach the waiting
fixture. Do not open a new public web endpoint or expose the inbox anonymously.

The persistent registry listener also needs read access to the registry and its
pinned settings. All absolute registry/settings/inbox paths must resolve to the
same files on both hosts. An administrator-provisioned private share/mapping can
supply that namespace. Where only the inbox is shared and paths differ, use the
per-run `--operator-inbox` mode with the controller's path and exact run key. That
mode needs neither processor credentials nor access to the worker's registry.

Install using the commands below **on the controlling PC** under the signed-in
operator account. An interactive desktop and its network access are required;
registration under a service account or an administrator's different session is
not proof of delivery. Prefer one listener per operator/profile set. If two
operators see the same request, only the first valid response is accepted.

Before release intake, send a clearly labelled synthetic request through the
same share and accounts, confirm that the controlling desktop displays it, answer
it there, and confirm the worker reads that response. Also verify that loss of
share access gives an alert rather than a successful response. This diagnostic
must not count as a sensor, outage or driver test. Register the listener to resume
at sign-in and verify its task, process and shared-file access after a restart.

Physical requests show the target, instructions, requested time and expiry with
the controller's UTC offset. Closing a window leaves the request pending; expiry
does not complete the test. The controller must be attended during planned manual
tests. A failure alert (such as **Submission worker needs attention**) is different
from a **physical action needed** window: identify its profile, release, stage and
observation time, then inspect the retained error. It is not an instruction to
operate hardware or to restart a workflow. A stale alert from a completed older
attempt must not be interpreted as a new run failure; retire its exact obsolete
watcher while keeping the evidence.

The physical-action listener handles physical requests and inbox-access errors.
It is not a general worker-failure monitor or a signing/delivery approval service.
Provision the appropriate worker-status observer as well; do not depend on an AI
noticing a failure. Dismissing an alert, or choosing Done, never signs or sends
anything.

For release-triggered workflows, install the persistent listener **once**, using
the evidence worker's private registry and an explicit list of trusted profile names:

```powershell
./scripts/automation/InstallSubmissionOperatorListener.ps1 `
  -Executable 'C:/Tools/DevTools/setup/CrestronHomeDevTools.Setup.exe' `
  -Registry 'C:/PrivateSubmission/registry.json' -Profiles 'driver-rehearsal','driver-submit' `
  -Name 'DriverActions'
```

Install it as the authorized operator account, in a signed-in Windows desktop.
The named interactive task resumes at sign-in without storing a password. It runs
the windowless setup executable directly, so no empty console appears. An active
desktop session is a prerequisite; task registration alone does not prove the
operator can see a prompt.

Every ten seconds the listener reads registrations for the selected profiles,
checks the frozen settings digest, release/mode and common inbox bindings, and
discovers the actual expanded run key. New releases require no per-run listener
installation or AI handoff. It stays quiet between releases, ignores inboxes not
yet created and skips completed ones. Changed or unreadable records produce an
attention notification and close stale action windows until verification succeeds.
The registry, frozen settings and inbox paths must resolve on the operator host;
this mode does not translate another host's drive paths. Do not expose that private
registry publicly or give arbitrary users write access. Discovery neither reads
saved credentials nor starts tests or advances a workflow.

The task `CrestronSubmission-DriverActions-operator-listener` is intentionally
persistent across releases. When retiring it, stop and unregister that exact task;
retain the evidence. It does not accumulate a new task for each run. Only one
listener should cover each set of profiles. Live task/desktop validation remains
required before relying on this mode for unattended release intake.

For a single already-known run, or a shared inbox mounted at a different local path,
use the per-run mode instead:

Run the setup application in the signed-in operator desktop session, using the
same shared inbox (its local mount path may differ from the worker's):

```text
CrestronHomeDevTools.Setup.exe --operator-inbox DIRECTORY --run-key RUNKEY
```

It stays in the notification area while idle and opens a window for a new pending
action. The window identifies the exact target and instructions and offers Done
and Unable to do this. Closing it leaves the request pending; use the notification
area menu to reopen pending actions. Answered/expired requests cannot be answered
again. An inaccessible inbox produces an attention notification rather than a
fabricated response. This application requires an interactive Windows session;
do not launch it as LocalService or in a noninteractive service desktop.

`scripts/automation/InstallSubmissionOperatorInbox.ps1` (under `tools/` in source) installs an explicitly named task for
the current signed-in account and exact run key. It starts the windowless inbox
monitor and resumes it at sign-in; no password is stored. Install it on the
operator's computer, not necessarily the evidence worker. Its 48-hour execution
limit bounds one invocation. After the run, stop and unregister that exact
`CrestronSubmission-NAME-operator` task if abandoning the run, preserving the
inbox records. For automatic completion, configure the worker's `OperatorInbox`
with the same directory and exact run key. Successful unsigned review preparation
closes that inbox. The desktop monitor validates this completion record and exits;
the accompanying watcher unregisters its own task after checking the task still
names that watcher and run. Closing the monitor manually is a different exit and
does not unregister the task. Task cleanup has not yet been validated in a live
interactive deployment.

`SubmissionPhysicalAction.ObserveAsync` arms an independent observer before it
publishes the action, then requires both a Done response and a successful
observation. It cancels the pending prompt if observation fails. Supply an
observer that honors cancellation and a baseline that rejects stale state. Use
an independent cancellation budget and a new restoration request in fixture
cleanup; never reuse the cancelled trigger request for restoration.

A specific request can also be opened with `--operator-request DIRECTORY
--request-sha256 SHA256`, or inspected/answered through the CLI:

```text
submission-operator show --request-directory DIRECTORY --request-sha256 SHA256
submission-operator respond --request-directory DIRECTORY --request-sha256 SHA256 --outcome done
```

Use `unable` when the action cannot be performed. There is no signing, upload or
email permission implied by either answer.

## Separate interruption processor

The additional initial fixture can explicitly select another processor with
`PreEnduranceSeparateProcessor: true`. Provide `PreEnduranceTests` with its own
host, certificate/SSH pins and concrete installed-driver identity, plus explicit
`PreEnduranceFixtureSettings`. The candidate package and source commit must match
the main run. The installed-driver runner reserves that processor and verifies
its installed payload before and after tests. It does not deploy a candidate as a
side effect; the matching candidate must already be installed there.

Do not use `PreEnduranceFromDeployment` or main-target `${managed:...}` references
with this option. They refer to the main processor and would select the wrong
device IDs. Set `PreEnduranceCredentialBindings` to the absolute private bindings
file or encrypted setup snapshot for the separate processor. It must resolve that
host and its reviewed trust pins; there is no fallback to the main processor's
`CredentialBindings`. Give the separate fixture the matching binding reference
as well if it authenticates independently. Only references are configured here;
passwords remain in the encrypted store. The
separate target plan and evidence remain in `pre-endurance`; they do not imply
that the main/endurance processor was interrupted. Installation and fixture
preparation for both targets remain explicit workflow prerequisites.
