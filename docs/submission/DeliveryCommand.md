# Protected submission delivery command

DevTools 1.8.0 adds `submission-deliver` to the console. This optional command uploads the prepared package and emails its verified download link and signed PDF; it is separate from ordinary GitHub and NuGet releases.

```text
CrestronHomeDevTools.Console submission-deliver --settings ABSOLUTE_PRIVATE_JSON --settings-sha256 REVIEWED_SHA256 --execute-approved
```

The argument order is exact. Interactive console mode refuses this command. Provide the four credential fields as JSON on redirected standard input, then close the input stream:

```json
{"uploadUserName":"AUTHORIZED_UPLOADER_LOGIN","uploadPassword":"PRIVATE_UPLOADER_PASSWORD","smtpUserName":"AUTHORIZED_MAILBOX_LOGIN","smtpPassword":"PRIVATE_MAILBOX_PASSWORD"}
```

These are schema examples, not values to commit. The command accepts at most 32,768 credential characters within 30 seconds. It does not prompt, obtain a password from Outlook, save credentials or emit their values. Use a private secret store in protected orchestration; do not put real credential JSON in command arguments, shell history, source or artifacts. Clearing the input buffer is not a guarantee that all managed-memory copies are erased.

## Reviewed settings

The UTF-8 settings file is limited to 1 MiB, must have an absolute path and must match the independently approved lowercase SHA-256. Unknown, duplicate, missing or null required fields are rejected. Use these exact camel-case fields:

| Field | Value |
| --- | --- |
| `schemaVersion` | `1` |
| `plan` | Exact prepared `SubmissionDeliveryPlan`: `candidateSha256`, `reviewSha256`, `authorizationSha256`, `packageSha256`, `signedFormSha256`, `packageFileName`, `signedFormFileName`, `sender`, `recipient`. |
| `journalDirectory` | Existing protected journal directory. Preserve it across retries and restarts. |
| `packagePath`, `signedFormPath` | Absolute paths to the reviewed package and signed form. |
| `revalidation` | Complete `SubmissionDeliveryRevalidationSettings` object using camel-case property names; see the [process bridge fields](DeliveryProcessBridge.md#prepare-protected-tooling-and-settings). `timeout` is a .NET TimeSpan string such as `00:05:00`. |
| `uploadReceiptDirectory` | Existing protected storage for upload attempts and private URLs. |
| `reviewedUploadFormSha256`, `acceptedUploadTermsSha256` | Hashes from the reviewed uploader form and explicitly accepted terms. |
| `uploadTimeoutSeconds` | From 1 to 600. |
| `mailReceiptDirectory` | Existing protected storage for composed messages and mail receipts. |
| `smtpHost`, `smtpPort` | Authorized SMTP DNS hostname and TLS port `465` or `587`. |
| `mailTimeoutSeconds` | From 1 to 600. |

Journal, upload receipts, mail receipts and revalidation attempts must be separate, non-nested directories. The operator must protect their full directory ancestry and the complete installed tooling from modification by evidence-producing workers. The command checks the final paths for redirection; it is not an ACL provisioning tool or a defence against an administrator changing its executable or dependencies. Do not derive the trusted settings hash from an untrusted worker's proposed file and treat that as approval.

No credentials belong in this settings file. Its sender and recipient come from the approved plan. There is no command-line override for either, the download URL, signed form, package hash, authorization or reviewed uploader terms.

## Execution and recovery

The command composes the [Crestron uploader](CrestronUploader.md), [SMTP provider](SmtpDelivery.md), [durable journal](DeliveryJournal.md) and [fresh revalidation bridge](DeliveryProcessBridge.md). It always calls guarded delivery. The pinned Python/.NET revalidator must approve the exact plan immediately before each pending external step. A command flag or settings hash alone does not authorize delivery.

A refused email-stage revalidation preserves a confirmed upload. A confirmed completed journal returns its receipt without another upload or email. An uncertain outcome blocks replay until independently reconciled; this command provides no force, reset or reconciliation switch. Preserve the original journal and provider evidence. Never use a different directory to evade it.

The console prints only a compact state summary, without private URLs, addresses, signatures or provider responses. Exit 0 means the journal is `Submitted`, which establishes provider acceptance, not recipient receipt or Crestron certification. Exit 2 requires inspecting private evidence. Cancellation returns 130; Ctrl+C requests cancellation, and the command has a 45-minute overall limit. A hosted workflow terminating the process can leave an uncertain journal, which must not be automatically replayed.

See the [final-stage workflow example](submission-delivery.yml.example). Install and pin the complete CLI deployment separately; the example deliberately uses protected, preinstalled tooling and does not execute an evidence-producing repository's scripts. Configure the environment approvers, authorized workflow/ref restrictions, runner isolation and private settings before enabling it.

## Validation scope

Offline command tests cover settings pins/schema, bounded credential input, generic errors, per-step authorization refusal and completed replay. Command integration tests also run the actual command parser and dispatch path through the pinned Python and .NET revalidator using synthetic forms. They verify both authorized stages, changed settings and package refusal, authorization changing after upload, preserved upload receipts, and completed replay without another send. Only the test executable substitutes the transport; the production command has no simulation switch. These results do not establish a completed protected CI submission. Real candidate acceptance, an approved signed form, provider/account verification and the final authorized delivery remain required.