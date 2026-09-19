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

After independently checking an unresolved delivery using its retained message ID, an operator can call:

```csharp
notifier.Reconcile(expectedMessageId, deliveryAccepted: true, reviewReference: "private-provider-review-42");
```

Use `false` only when the review establishes the message was not accepted and another attempt is authorized. Reconciliation itself sends nothing and records the review reference. Do not automate this decision from an exception, timeout or missing receipt. A changed recipient/run configuration requires its own reviewed subscription; it is refused against the original journal.

## Validation boundary

Offline tests cover incident suppression across process restarts, completion suppression, configured recipients, absence of attachments/secrets, connection and send failures, lost acceptance writes, locking, stale/wrong-run reports and explicit reconciliation. The CLI also runs as a real quiet process without a processor profile or SMTP connection. These tests use simulated senders. Real provider acceptance, inbox delivery and scheduled independent-observer operation remain deployment checks; no real alert delivery is claimed by this source documentation.
