# Planned physical actions

Status: source implementation for the next batched release; not included in 1.21.0.
The shared request protocol, concurrent observation, desktop inbox and CLI have
offline coverage. Live sensor/outage fixtures and service-to-desktop deployment
still need validation.

A sensor press or unplugging a processor is a planned test action. It does not
require an AI session to advance the workflow. A fixture publishes an explicit
request, waits for an answer and then verifies the resulting device/app behavior.
An answer is neither a passing test nor authority to sign, upload or email.

## Fixture

Provision a private shared inbox writable by the test identity and the authorized
operator. Use a separate inbox per workflow attempt, retaining its contents with
the evidence. Keep credentials out of instructions. The run key is the workflow's
64-character lowercase hexadecimal identity.

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

`tools/InstallSubmissionOperatorInbox.ps1` installs an explicitly named task for
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
device IDs. Each processor requires its own matching credential binding. The
separate target plan and evidence remain in `pre-endurance`; they do not imply
that the main/endurance processor was interrupted. Installation and fixture
preparation for both targets remain explicit workflow prerequisites.
