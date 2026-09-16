# Delivery intent, receipts and reconciliation

The `SubmissionDelivery` API, introduced in 1.5.0, records the upload/email boundary for a future Crestron delivery adapter. It has no built-in uploader, SMTP client or CLI send command. Tests use synthetic local transports; no driver has been submitted through this code.

Crestron's [published submission procedure](https://sdkcon78221.crestron.com/sdk/Crestron_Certified_Drivers_SDK/Content/Topics/Submit-a-Driver/Submit-a-Driver.htm) requires its file-sharing service for the `.pkg`, then an email to `drivers@crestron.com` with subject `Driver Submission Package`, the returned download URL and the signed self-test plan attached. The sending email address receives subsequent correspondence. An email-provider acceptance receipt is not proof that Crestron received, approved or certified the driver.

## Required upstream checks

Before calling `ExecuteAsync`, the trusted workflow must validate the exact Release package, complete applicable evidence, reviewed form, signature authorization and final signed PDF. `SubmissionDeliveryPlan` binds candidate, review, authorization, package and signed-form SHA-256 values, exact filenames, sender and recipient. The API checks digest syntax and the actual delivery file bytes. It does not authenticate the signer, inspect a signature, approve the recipient, or establish that an arbitrary document called `self-test.pdf` is a completed official form. The calling workflow must establish those facts and retain the plan and authorization separately.

Keep the plan and journal in an existing private durable directory outside source control and public artifacts. Receipts may contain private download URLs and provider references. Every cooperating process sending the same artifacts must use the same journal directory. Different workers with independent directories are not coordinated; this is not a distributed delivery lock. Do not delete lock/journal files as routine cleanup or create a fresh directory to bypass an unresolved result.

The journal key identifies package/form bytes and the sender/recipient pair. Changing an authorization, review pin or filename for the same delivery does not create an independent attempt: the retained plan digest will conflict and stop execution. A different signed PDF is a different delivery identity; the higher-level release policy must still decide whether a revised submission is authorized.

## State transitions

| State | Meaning and next action |
|---|---|
| Prepared | Verified copies are ready; upload has not been attempted. |
| UploadPending | Intent was persisted before calling the uploader. After interruption, reconcile instead of replaying. |
| Uploaded | A confirmed HTTPS downloader URL/provider receipt was retained; proceed only to email. |
| SendPending | Intent was persisted before calling the mail provider. After interruption, reconcile instead of replaying. |
| Submitted | The transport returned a confirmed mail-provider receipt. Repeated execution returns this receipt without another upload/send. |
| OutcomeUnknown | The external step threw, was cancelled after intent, returned an invalid receipt or could not be recorded conclusively. Reconciliation is required. |

`Read` inspects the retained record under the same exclusive file lock. Malformed or inconsistent records fail rather than being silently reset. Files are hashed into bounded read-only memory copies before external work (64 MiB maximum per file), so later source-path changes cannot alter uploaded or attached bytes. No source package or signed form is needed merely to return a previously completed receipt.

Intent/receipt writes use a new temporary file, flush to disk and atomic replacement while holding a separate exclusive lock for the whole operation. If recording an error also fails, the original exception is preserved and a prior Pending record still stops replay. This protects cooperating processes from ordinary process interruption; it is not an exactly-once delivery guarantee against loss/corruption of the journal storage or filesystem power-loss behavior. Back up and protect the durable record.

Cancellation before a side effect is handled at that boundary. For example, cancellation after a confirmed upload leaves Uploaded, allowing a later run to send without uploading again. Cancellation during an external request is ambiguous even if the local task reports cancellation.

## Transport and reconciliation contract

`ISubmissionDeliveryTransport` receives only verified package/form streams and the selected delivery metadata. An implementation must disable automatic upload/send retries, use the documented destination and retain confirmed provider references. The uploader-specific adapter must validate its returned download URL against the service's actual contract; the generic journal only requires HTTPS without embedded user credentials. The mail adapter must use the documented subject, download URL and signed-form attachment, and must not attach the private evidence ZIP or signature source image.

The message has a stable Message-ID for this plan. A Message-ID assists lookup; it does not cause SMTP servers to deduplicate messages and is not proof of delivery. Provider acceptance semantics and sent-mail reconciliation must be tested with a controlled recipient before actual submission.

`Reconcile` is an explicit operation after a provider lookup or authorized operator establishes what happened. Supply the outstanding Upload/Send step, whether it actually occurred, a retained explanation/reference and the corresponding positive receipt if it did. Confirming an upload resumes at Uploaded; confirming a send records Submitted. Independently confirmed non-delivery permits only that step to run again. Missing search results, elapsed time or a timeout alone must never be treated as proof that nothing was sent.

Reconciliation appends its decision/reference to the record. This is an audit trail supplied by the caller, not an authenticated assertion. The workflow must restrict who can make these decisions and retain the referenced provider evidence privately. There is intentionally no force-retry flag.

## Validation status

Offline tests cover repeated completion, conflicting plans, altered bytes, preserved verified copies, simultaneous attempts, failures at each external boundary, pending-intent recovery, cancellation between stages, corrupt receipts and positive/negative reconciliation. All provider operations in these tests are synthetic. Live uploader behavior, mail-provider setup, actual process/worker recovery and an authorized end-to-end signed submission remain pending in the [submission plan](../CrestronSubmission.md).
