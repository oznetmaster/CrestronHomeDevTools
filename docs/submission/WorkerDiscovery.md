# Discover and prepare the submission worker

Use this guide before freezing a new attempt's executable settings. The operator
owns routine discovery and provisioning. A missing path, tool or first-driver
profile is a setup task to investigate, not by itself a reason to stop and ask
the developer or a supervisor to supply it.

For an existing attempt, first read its checkpoint and verify its actual worker,
process and reservation identities. Do not apply this guide by changing pinned
tools, replacing profiles, restarting tests or resetting endurance in that run.
Keep documentation maintenance and new setup separate from active operations.

## 1. Establish where commands will run

Record the controller computer, execution computer and Windows account separately.
On each computer, read the actual identity with `hostname` and `whoami`. A worker
name in a profile does not make subsequent local commands execute remotely.
Check tool paths, scheduled tasks, processes and Android devices on the selected
worker under its intended execution account. A missing local ADB executable says
nothing about a different worker's Android installation.

Read the supplied checkpoint and saved setup first. The run-profile fields are:

| Field | Meaning |
|---|---|
| `WindowsResource` | Descriptive name of the test/monitoring computer. |
| `WindowsHost` | Remote address bound to the saved Windows credential; may be blank for local execution without a remote credential. |
| `WindowsCredential` | Named Windows credential for a remote worker. |
| `AndroidTarget` | App test target description; not a complete Android session profile. |

These fields do not provision a transport, install tools or discover an emulator.
Use the actual stage's settings contract for executable paths and session inputs.
See [saved setup](SetupApp.md) and [worker settings](AutomationWorker.md).

## 2. Discover the existing connection route

Inspect the supplied private equipment inventory, saved worker configuration,
connection documentation and previous infrastructure setup receipts. Reuse an
existing authorized transport, such as pinned SSH or configured WinRM. Verify
the remote computer and account before transferring files or starting commands.
An unsuccessful WinRM probe does not establish that an existing SSH route is
unavailable; inspect the configured route before reporting an access blocker.

Keep the credential reference and endpoint/trust identity together. Do not print
passwords, export decrypted profiles, guess host keys, disable trust checks or
change firewall/registry settings to make a guessed connection method work.
Windows user-scoped encrypted stores are not portable between computers or
accounts. Provision selected credentials through the documented worker setup;
do not broaden access to an entire setup store.

If no authorized route is documented or discoverable, report the specific missing
endpoint, trust information or credential reference, the locations inspected and
the original connection result. Ask only for what cannot be established through
authorized read-only discovery. Do not ask the developer to locate an executable
that can be installed through the public distribution procedure.

## 3. Verify and provision public tools

Inventory the selected worker's configured tool directories and task/service
definitions, then check the exact paths. `PATH` and conventional SDK directories
are only discovery aids. A missing source checkout is not a blocker when the
required tool has a published binary distribution.

For the NUnit coordinator, obtain **CrestronHomeNUnit.Cli-win-x64.zip** from the
selected [public release](https://github.com/oznetmaster/CrestronHomeNUnit/releases),
extract the complete archive into a fresh versioned tools directory, and run
`CrestronHomeNUnit.Cli.exe --help` there under the worker account. Follow the
[CLI guide](https://github.com/oznetmaster/CrestronHomeNUnit/blob/main/docs/CommandLineRunner.md)
and [processor workflow prerequisites](https://github.com/oznetmaster/CrestronHomeNUnit/blob/main/docs/ProcessorTestWorkflow.md)
for the selected version. CLI startup alone does not prove that fixture projects
can build or that their SDKs, targeting packs and dependencies are installed.

Record release/source identity, download origin and supplied digest verification;
retain the downloaded archive's hash and the installed file inventory. Do not
invent an independently published hash when none is supplied. Keep private
settings and logs outside pinned program directories. Transfer the exact candidate
and fixture inputs through the authorized route and verify their retained hashes
at the destination before use. Do not rebuild a candidate just to move it.

Likewise extract the complete DevTools bundle and, when required for building,
the complete ManifestUtil distribution described in [HelpBuild](HelpBuild.md).
Copying a single executable can omit required dependencies. Verify runtime and
command availability before acquiring equipment or requesting physical actions.
Provisioning a missing tool is operator work within the authorized submission
scope; preserve existing installations and active runs. Use noninteractive,
hidden background execution rather than flashing console windows on a desktop.

## 4. Verify Android infrastructure separately from driver tests

Read the existing private Android profile and emulator task configuration. Verify
on the worker the absolute ADB path, selected serial, booted device, app package,
expected Home and processor endpoint, connection port, execution account and
shared Android lock path. Observe the Home through the documented public UI
inspection APIs; do not infer it from an old screenshot or the processor hostname.
Follow [Android worker setup](AndroidWorkerSetup.md),
[Android UI testing](https://github.com/oznetmaster/CrestronHomeNUnit/blob/main/docs/AndroidUiTesting.md)
and the [installed-driver phase](https://github.com/oznetmaster/CrestronHomeNUnit/blob/main/docs/InstalledDriverTests.md).

Existing infrastructure may be shared across driver submissions after current
verification. Reuse its connection route, tool paths, emulator identity and lock
convention. Create the new driver's own fixture selection, instance/room bindings,
candidate identity and expectations. Do not copy another driver's test plan,
criteria, results or approval. Never run a duplicate emulator or bypass its shared
reservation because a new driver has no profile yet.

## 5. Continue or ask for the missing fact

| Finding | Operator's next action |
|---|---|
| No previous submission profile | Compose one from the public contracts and verified driver facts. |
| Missing path or coordinator binary | Discover it on the correct worker or provision the selected public distribution. |
| A connection method fails | Inspect the configured transport and original failure; do not assume all access is unavailable. |
| Routine test-instance deployment is next | Check existing authorization, target scope and restoration requirements, then use the public workflow. Do not request the same authorization again. |
| Human must move a cable | Prepare the test first, then issue one precise physical-action prompt and await the response. Software provisioning is not a cable-action prompt. |
| Missing required fixture or real endurance producer | Implement and validate reusable test support through the public contract within the assigned development scope, or report the concrete capability gap if that scope is absent. Configuration alone cannot supply coverage. |
| Unknown product fact, unavailable credential, changed scope or explicit approval gate | Ask for that fact or decision, explaining the actual rule or failure. Continue independent authorized work meanwhile. |

Existing authorization does not bypass an explicit tool approval or the final
artifact signing/delivery contracts. Conversely, do not invent extra approvals
for routine setup already covered by the assignment. Keep Phase 2 test evidence
separate from Phase 3 document/signing/delivery work; neither setup readiness nor
a help draft establishes a passing test.

Retain each operation's exact command (with secrets excluded), execution host and
account, UTC time, exit status and original output in distinct receipt files.
Link failures and subsequent successful attempts without overwriting either.
An unexpected operation result remains recorded even if independent readiness
later passes; investigate its meaning rather than replaying a mutation blindly.
Update the checkpoint with the actual next action and live worker handles. A
stopped operator is not a running submission. Do not finish with a request for
routine supervisor intervention when the next authorized setup step is executable.

Copyright (c) 2026 Neil Colvin. MIT licensed.
