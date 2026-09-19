# Endurance notifications (source development)

This optional API and CLI are in current source after 1.11.0, not that released archive. They send operational email alerts; they do not submit a driver or contact the processor. The health assessment, notification sender and independent observer are separate components. Configure and test the entire observation-to-inbox path before relying on unattended monitoring.

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

### Obtain a fresh observation

The current source `endurance-observe` command includes its Windows snapshot reader, so developers do not write or transfer a PowerShell script. Give it the existing trusted worker plan and a private observer configuration:

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

For remote observation, the protected calling process supplies `{ "userName": "WINDOWS_LOGIN", "password": "WINDOWS_PASSWORD" }` on standard input and closes it. Local observation does not need that input. The result is the same health-report JSON consumed by the notification command. The remote reader runs from the installed library over pinned SSH; it does not upload a script, acquire a collector lock or contact a processor. Each query is bounded and is not automatically retried, apart from the snapshot reader's bounded inconsistent task-state reads.

A failed connection, authentication, query or malformed response produces an `observer-query-failed` attention report where possible. It does not fall back to a previous healthy snapshot. Invalid local inputs and explicit cancellation can return a nonzero exit without a report; the supervising job must also surface those errors. Exit 3 with a valid attention report is deliberately eligible for notification, not a reason to skip the notification step. Do not join the two commands with a success-only conditional. Retain the fresh stdout privately, inspect its schema and pass that report to the notification command even when observation returned 3. Never pass stderr as a health report.

### Notify the approved destination

```text
CrestronHomeDevTools.Console.exe endurance-notify --settings PRIVATE_SETTINGS.json --health FRESH_HEALTH.json --journal PRIVATE_EXISTING_DIRECTORY --send true
```

The protected calling process supplies `{ "userName": "SMTP_LOGIN", "password": "SMTP_PASSWORD" }` on standard input and closes the input stream. Do not place the password in arguments, logs, committed scripts or the configuration above. The command requires noninteractive input and a pre-existing protected journal directory. It rejects stale reports, reports for another run and raw free-text error messages instead of reason codes.

Successful collecting observations are quiet. The first attention report sends one message; further attention reports, including changed reason codes, remain suppressed until a verified healthy observation resets that incident. Completion sends at most one message per subscription when enabled. SMTP acceptance is recorded, but must not be described as confirmed inbox delivery.

Exit 0 means quiet, previously accepted or accepted by SMTP. Exit 2 means invalid input. Exit 3 means failed or uncertain delivery, cancellation, or journal access requiring inspection. The observer must surface **all nonzero exits**, not hide them. An email sender cannot reliably alert through the same failing mail service.

The journal has an exclusive local/file-share lock and flushed state before sending. Process restart preserves suppression. If the process stops during delivery, or the acceptance cannot be retained, the next invocation does not send again. A healthy observation cannot silently clear that unresolved attempt. Preserve the entire journal; deleting its checkpoint does not authorize replay. Do not create a new journal merely to bypass uncertainty.

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

Offline tests cover incident suppression across process restarts, completion suppression, configured recipients, absence of attachments/secrets, connection and send failures, lost acceptance writes, locking, stale/wrong-run reports and explicit reconciliation. The CLI also runs as a real quiet process without a processor profile or SMTP connection. These tests use simulated senders. Real provider acceptance, inbox delivery and scheduled independent-observer operation remain deployment checks; no real alert delivery is claimed by this source documentation.

The Windows observer also has offline tests for its bundled reader, command encoding and literal task parameters, malformed responses, cancellation and failed-query handling. Its source CLI has completed a read-only observation of an existing scheduled collector on a separate Windows PC using a verified ED25519 SSH key. This establishes that observation path, not scheduled alert delivery or a Windows restart test.
