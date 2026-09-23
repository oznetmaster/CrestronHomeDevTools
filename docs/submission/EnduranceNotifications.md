# Endurance notifications

These optional APIs and CLI commands require DevTools 1.12.0 or later. They send operational email alerts; they do not submit a driver or contact the processor. The health assessment, notification sender and independent observer are separate components. Configure and test the entire observation-to-inbox path before relying on unattended monitoring.

## Private configuration

Authorize the sender and recipient before enabling sending. Keep settings, credentials, reports and the notification journal in protected local storage, outside the repository and release assets. Provide one authoritative observer and one journal for each run/destination; do not give two observer PCs separate journals for the same subscription.

```json
{
  "runId": "COPY_THE_PLAN_SHA256_FROM_THE_TRUSTED_HEALTH_REPORT",
  "label": "Candidate A on the submission processor",
  "host": "smtp.example.com",
  "port": 587,
  "sender": "monitor@example.com",
  "recipient": "developer@example.com",
  "notifyCompletion": true
}
```

`runId` must match the `PlanSha256` returned by `endurance-health`. It identifies the immutable worker plan, including its reservation. Do not reuse a subscription for another candidate or monitoring period. The placeholder above is not a valid identity. Port 587 requires STARTTLS; port 465 requires implicit TLS. Normal certificate validation remains enabled.

## CLI

### Windows unattended setup

The Windows scripts and named credential bindings below require DevTools 1.17.0 or later. They provide the protected credential handoff and scheduled invocation, so a developer does not have to write a launcher or secret-store integration. They use the existing `endurance-watch` command and its notification journal. They never start, tick, restart or change the collector. See [reusable private inputs](../PrivateInputs.md) for supported consumers and provisioning limits.

Use a **separate** complete console bundle for the observer; do not replace a bundle pinned by a running collector. Copy its `scripts/endurance` directory to a sibling `scripts` directory as described in [Windows worker setup](WindowsEnduranceWorker.md). The observer can run on the same PC as the collector, or on another Windows PC. For remote monitoring, retain a local copy of the exact existing `worker.json`; do not regenerate its plan. Prepare `observer.json` and `notifications.json` using the formats on this page and the approved SMTP provider details.

In an interactive console, collect named entries in your private user store once:

```powershell
$base = 'C:\Private\EnduranceObserver\candidate-a'
& "$base\cli\CrestronHomeDevTools.Console.exe" credentials create
& "$base\cli\CrestronHomeDevTools.Console.exe" credentials configure --name monitoring-mail --kind Smtp
& "$base\cli\CrestronHomeDevTools.Console.exe" credentials configure --name monitoring-pc --kind Windows
```

Skip `create` if the user store already exists. Omit `monitoring-pc` for local observation. Setup prompts privately for the endpoint, username, password and relevant sender/port/trust settings. Stored entries are encrypted with Windows DPAPI; commands use purpose and endpoint checks before resolving them. Setup performs no email or processor operation. Existing entries require explicit replacement.

For an unattended LocalService task, explicitly provision only its required entries into a separate local service store:

```powershell
& "$base\cli\CrestronHomeDevTools.Console.exe" credentials create --store "$base\service-store" --service-reader S-1-5-19
& "$base\cli\CrestronHomeDevTools.Console.exe" credentials provision --name monitoring-mail --target-store "$base\service-store"
& "$base\cli\CrestronHomeDevTools.Console.exe" credentials provision --name monitoring-pc --target-store "$base\service-store"
```

Again omit the Windows entry for local observation. Granting a service access must be authorized. The new service store uses machine DPAPI with NTFS access limited to its creating user, Administrators, SYSTEM and read/execute for the selected account. Machine DPAPI is not an account boundary by itself; preserve those file permissions. The task cannot rewrite entries. Nothing copies other passwords or signature assets, and copying a user's encrypted store is not a method of provisioning another account or computer.

Create `credential-bindings.json` with names only:

```json
{
  "storeDirectory": "C:\\Private\\EnduranceObserver\\candidate-a\\service-store",
  "smtp": "monitoring-mail",
  "windows": "monitoring-pc"
}
```

Omit `windows` for local observation. Keep the binding file private along with the other configuration. Passwords do not appear in it. The SMTP endpoint, port and sender, and the remote Windows endpoint, port and SSH fingerprint, must match the saved entries. Rotate a password by explicitly replacing and reprovisioning that entry; changing a binding or destination requires review and a new watch configuration.

Create and protect `journal` and `state` as separate persistent directories, giving LocalService modify access there. LocalService needs read/execute access to the console, copied scripts, worker, observer and notification files; only administrators and the owner should be able to modify those inputs. For local observation it also needs read access to the collector's Task Scheduler entry and completed scheduler-state files. For remote observation, the supplied Windows account needs that read access on the monitoring computer. Verify it with `endurance-observe`; do not interpret `Access denied` as a missing or failed collector.

Prepare the pinned watch configuration and register its independent startup/minute task:

```powershell
& "$base\scripts\New-EnduranceWatchConfiguration.ps1" `
    -CliDirectory "$base\cli" `
    -WorkerFile "$base\worker.json" `
    -ObserverFile "$base\observer.json" `
    -NotificationsFile "$base\notifications.json" `
    -CredentialBindingsFile "$base\credential-bindings.json" `
    -JournalDirectory "$base\journal" `
    -StateDirectory "$base\state" `
    -Output "$base\watch.json"

& "$base\scripts\Register-EnduranceScheduledTask.ps1" `
    -TaskName 'Crestron-Endurance-Watch-CandidateA' `
    -Configuration "$base\watch.json" `
    -TickScript "$base\scripts\Invoke-EnduranceScheduledWatch.ps1"
```

Register only after authorizing the sender/recipient: the task's first invocation can send an attention or completion email. It runs as LocalService without a Windows login and starts after a PC reboot. No task is overwritten. Use one task and one protected journal per run/destination; different runs use different names and directories. The wrapper hashes the entire console and reviewed inputs, passes only the binding-file path, retains each invocation's stdout/stderr and result privately, and updates `state/status.json`. The CLI resolves the encrypted entries; the script never reads a password. Nonzero child exit codes remain nonzero scheduled-task results. It rejects overlapping invocations and bounds the child process; it does not hide a failure behind a previous healthy result. SMTP uncertainty remains governed by the existing journal, including after Windows restarts.

Validate one invocation under the actual service account and an explicitly approved test email to your own address before relying on alerts. Inspect the task result and retained output, not just whether registration succeeded. The offline script tests use a process double and send no mail; actual provider acceptance, inbox delivery and machine startup remain deployment checks. Disable the observer task after reviewing and retaining a completed run and its final notification; keep its journal for audit and duplicate prevention.

A single PC supports collection and local alerts without a second computer. While that PC is off it cannot send an alert about itself. If notification during a PC outage is required, use an independent receiver or another observer; otherwise the retained observation gap is assessed after restart. This limitation does not require a second PC merely to use the workflow.

### Combined observation and notification

For unattended callers, `endurance-watch` performs one fresh observation and passes its result directly to the notifier. It avoids a success-only command chain accidentally dropping an attention report:

```text
CrestronHomeDevTools.Console.exe endurance-watch --worker PRIVATE_WORKER.json --observer PRIVATE_OBSERVER.json --notifications PRIVATE_SETTINGS.json --journal PRIVATE_EXISTING_DIRECTORY --send true
```

Use the same worker, observer and notification settings described below. On Windows, add `--credentials PRIVATE_BINDINGS_JSON` to resolve the saved entries described above. For integration with another secret store, the protected calling process can instead supply credentials on standard input and close it:

```json
{
  "windows": { "userName": "WINDOWS_LOGIN", "password": "WINDOWS_PASSWORD" },
  "smtp": { "userName": "SMTP_LOGIN", "password": "SMTP_PASSWORD" }
}
```

Omit `windows` for local observation. Windows credentials are used only for the pinned remote reader; SMTP credentials are used only by the notifier. Do not put actual values in arguments, examples, logs or source control. The destination still needs explicit authorization before enabling `--send true`.

The output contains separate `Health` and `Notification` results. Exit 0 means healthy/completed observation with a quiet or SMTP-accepted notification. Exit 2 means invalid configuration or credentials. Exit 3 means monitoring attention or notification requiring inspection; an accepted attention email does **not** turn unhealthy monitoring into exit 0. If notification fails after observation, the output retains the fresh health report with a failure code. An observation failure never falls back to an old healthy report.

Schedule repeated invocations through the operator's supervised job, retaining output privately and using the same protected journal each time, including after a PC restart. Each invocation observes afresh; the notifier suppresses duplicate incidents and refuses to resend an uncertain attempt. Do not delete its journal or create another to force delivery. Surface nonzero exits independently of email. The command has a 90-second outer bound, performs no collector or processor commands, and does not install a task, provision credentials or alter a running endurance period. Use one authoritative observer per subscription. Local Task Scheduler startup and a remote observer's own availability still need the deployment checks below.

The combined command is included in 1.12.0. Its handoff tests use synthetic observations and SMTP sessions, including query failures, duplicate invocations, uncertain delivery and inaccessible journals. They do not establish scheduled operation or real inbox delivery.

### Obtain a fresh observation

The `endurance-observe` command includes its Windows snapshot reader, so developers do not write or transfer a PowerShell script. Give it the existing trusted worker plan and a private observer configuration:

```json
{
  "task": {
    "taskName": "Crestron-Endurance-CandidateA",
    "stateDirectory": "C:\\ProgramData\\PrivateMonitoring\\CandidateA\\scheduler-state",
    "taskPath": "\\"
  },
  "remote": {
    "host": "monitor-pc.example.test",
    "port": 22,
    "hostKeyAlgorithm": "ssh-ed25519",
    "hostKeyFingerprint": "INDEPENDENTLY_VERIFIED_WINDOWS_SSH_FINGERPRINT"
  }
}
```

Omit `remote` to read the local Windows PC. For another Windows PC, enable its SSH server deliberately, authorize an account to read the intended task and scheduler files, and verify its host key before configuring the observer. Credentials are that Windows account's credentials, not processor credentials. The command never trusts an unknown key automatically and never installs SSH, changes the firewall or provisions account access.

```text
CrestronHomeDevTools.Console.exe endurance-observe --worker PRIVATE_WORKER.json --observer PRIVATE_OBSERVER.json
```

For a saved Windows login, add `--credentials PRIVATE_BINDINGS_JSON`; see [named private inputs](../PrivateInputs.md). This avoids a credential handoff script. Alternatively, an existing secret-manager integration supplies this **observer-only** object on standard input and closes it:

```json
{ "userName": "WINDOWS_LOGIN", "password": "WINDOWS_PASSWORD" }
```

The surrounding `windows` and `smtp` objects shown under `endurance-watch` belong to that combined command, not `endurance-observe`. Local observation needs neither remote credentials nor that input. The result is the same health-report JSON consumed by the notification command. The remote reader runs from the installed library over pinned SSH; it does not upload a script, acquire a collector lock or contact a processor. Each query is bounded and is not automatically retried, apart from the snapshot reader's bounded inconsistent task-state reads.

A failed connection, authentication, query or malformed response produces an `observer-query-failed` attention report where possible. It does not fall back to a previous healthy snapshot. Invalid local inputs and explicit cancellation can return a nonzero exit without a report; the supervising job must also surface those errors. Exit 3 with a valid attention report is deliberately eligible for notification, not a reason to skip the notification step. Do not join the two commands with a success-only conditional. Retain the fresh stdout privately, inspect its schema and pass that report to the notification command even when observation returned 3. Never pass stderr as a health report.

### Notify the approved destination

```text
CrestronHomeDevTools.Console.exe endurance-notify --settings PRIVATE_SETTINGS.json --health FRESH_HEALTH.json --journal PRIVATE_EXISTING_DIRECTORY --send true
```

The protected calling process supplies `{ "userName": "SMTP_LOGIN", "password": "SMTP_PASSWORD" }` on standard input and closes the input stream. Do not place the password in arguments, logs, committed scripts or the configuration above. The command requires noninteractive input and a pre-existing protected journal directory. It rejects stale reports, reports for another run and raw free-text error messages instead of reason codes.

Successful collecting observations are quiet. The first attention report sends one message; further attention reports, including changed reason codes, remain suppressed until a verified healthy observation resets that incident. Completion sends at most one message per subscription when enabled. SMTP acceptance is recorded, but must not be described as confirmed inbox delivery.

Exit 0 means quiet, previously accepted or accepted by SMTP. Exit 2 means invalid input. Exit 3 means failed or uncertain delivery, cancellation, or journal access requiring inspection. The observer must surface **all nonzero exits**, not hide them. An email sender cannot reliably alert through the same failing mail service.

The journal has an exclusive local/file-share lock and flushed state before sending. Process restart preserves suppression. If the process stops during delivery, or the acceptance cannot be retained, the next invocation does not send again. A healthy observation cannot silently clear that unresolved attempt. Preserve the entire journal; deleting its checkpoint does not authorize replay. Do not create a new journal merely to bypass uncertainty.

Current source records notification events when state or delivery identity changes. Repeated healthy observations update the current checkpoint without creating another event file; actual send, acceptance, uncertainty and reconciliation records remain retained. The updated scheduled watcher also removes successful temporary diagnostics after retaining current status and a compact rotating history. Failed watcher attempts retain their original output. These reductions apply after upgrading and configuring new runs; do not replace pinned tools in a running collection.

## C# integration and explicit reconciliation

```csharp
var notifier = new SubmissionEnduranceNotifier(settings, smtpCredential, privateJournalDirectory);
SubmissionEnduranceNotificationResult result = await notifier.NotifyAsync(healthReport, cancellationToken);
```

Supply a freshly evaluated report from `SubmissionEnduranceHealth.Evaluate`, obtained through a trusted observer. Settings and credentials are ordinary C# objects; no Python is involved. Polling, authenticated remote observation, Windows startup and secret-store access remain the integrating application's responsibility. See [Windows endurance workers](WindowsEnduranceWorker.md) for passive snapshots. On a single-PC setup, an external receiver must detect that PC's loss of power; a task on the same PC cannot do so.

The built-in Windows observer can provide that report directly:

```csharp
var healthReport = await SubmissionEnduranceWindowsObserver.AssessAsync(
    worker.Plan, windowsTask, remoteWindowsEndpoint, windowsCredential, cancellationToken);
var delivery = await notifier.NotifyAsync(healthReport, cancellationToken);
```

Use a null endpoint/credential for local Windows observation. `ReadLocalAsync` and `ReadRemoteAsync` are also available when the caller needs the underlying snapshot. A query failure in `AssessAsync` becomes a fresh, run-bound attention report; argument errors and explicit caller cancellation remain errors. The same configured C# integration can service multiple separately bound runs without a fixed computer count. Scheduling, startup, access provisioning and secret retrieval still require deployment configuration.

After independently checking an unresolved delivery using its retained message ID, an operator can call:

```csharp
notifier.Reconcile(expectedMessageId, deliveryAccepted: true, reviewReference: "private-provider-review-42");
```

Use `false` only when the review establishes the message was not accepted and another attempt is authorized. Reconciliation itself sends nothing and records the review reference. Do not automate this decision from an exception, timeout or missing receipt. A changed recipient/run configuration requires its own reviewed subscription; it is refused against the original journal.

## Validation boundary

Offline tests cover incident suppression across process restarts, completion suppression, configured recipients, absence of attachments/secrets, connection and send failures, lost acceptance writes, locking, stale/wrong-run reports and explicit reconciliation. The CLI also runs as a real quiet process without a processor profile or SMTP connection. These tests use simulated senders. Real provider acceptance, inbox delivery and scheduled independent-observer operation remain deployment checks; no real alert delivery is claimed here.

The Windows observer also has offline tests for its bundled reader, command encoding and literal task parameters, malformed responses, cancellation and failed-query handling. Its source CLI has completed a read-only observation of an existing scheduled collector on a separate Windows PC using a verified ED25519 SSH key. This establishes that observation path, not scheduled alert delivery or a Windows restart test.
