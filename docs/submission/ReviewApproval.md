# Final approval of a reviewed request

**Requires 1.13.0 or later.** These APIs and commands connect an independently reviewed packet to C# and protected console delivery. The existing `submission-deliver` command remains complete-only; unsigned requests use the separate command below.

Approval covers the exact candidate, prepared request receipt, package and PDF bytes and filenames, sender and recipient, verification mode, every disclosure pin, public explanations and generated email wording. **Complete means complete against our interpretation of Crestron's published submission requirements**, including testing, evidence and documents. Only Crestron can decide acceptance, publication or certification; approval by the developer and confirmed provider delivery do not establish any of those decisions.

## Review the packet

For a prepared unsigned request, set `SubmissionReviewDeliveryPlan.ReviewSha256` to the independently retained hash of `request-receipt.json`. Use the receipt's exact package, attachment, candidate and declaration identities. The overall verification status remains `GapsDeclared` even if all tests passed but the signature or required form is omitted. Include the public explanations in `GapSummary` and `DocumentOmissions`. Keep private evidence, credentials and local paths out of those public fields.

The initial `AuthorizationSha256` can be 64 zeros. Call `SubmissionReviewApproval.Preview(plan)`, or use the console:

```text
CrestronHomeDevTools.Console submission-review-approval-preview --plan PRIVATE_PLAN_JSON --plan-file-sha256 REVIEWED_PLAN_FILE_SHA256 --output NEW_PRIVATE_PREVIEW_JSON
```

All file paths must be absolute. The command writes a new private file containing the exact generated subject/body, packet digest and correspondence digest. It never creates approval. Review every PDF page, the full interpreted requirement inventory, the evidence and its producer provenance, disclosed omissions, restoration/reservation state, recipients and correspondence before proceeding.

## Approve independently

The trusted approval channel supplies a `SubmissionReviewApprovalDocument` with schema version 1, the preview's `PacketSha256` and `CorrespondenceSha256`, an explicit UTC `ExpiresUtc`, and the three actual decisions: `VisualReviewCompleted`, `ProducerAuthenticationConfirmed` and `DeliveryAuthorized`. All three must be true for delivery. Do not let an evidence worker approve its own output. These boolean fields cannot authenticate themselves; the protected coordinator must establish the approver's identity and authority independently.

Retain the approval file and its SHA-256 through that protected channel. Set the final plan's `AuthorizationSha256` to that hash. This does not alter the preview's packet digest: the preview neutralizes **only** the approval-file hash, avoiding an impossible self-referencing hash. The final `SubmissionDelivery.ReviewPlanDigest` includes the real approval hash. Any other packet or wording change requires fresh approval.

`SubmissionReviewApproval.Verify(plan, approvalPath, independentlyApprovedSha256, now)` verifies the exact bounded approval bytes, all assertions, packet and wording identities, and unexpired UTC deadline. It returns the original expiry without extending it. It checks approval only, not the current evidence. The offline equivalent is:

```text
CrestronHomeDevTools.Console submission-review-approval-check --plan PRIVATE_FINAL_PLAN_JSON --plan-file-sha256 REVIEWED_FINAL_PLAN_FILE_SHA256 --approval PRIVATE_APPROVAL_JSON --approval-sha256 INDEPENDENT_APPROVAL_SHA256 --output NEW_PRIVATE_CHECK_JSON
```

## Deliver a prepared review request from C#

Use `SubmissionReviewRequestDelivery.ExecuteAsync` with the prepared request directory, final plan, approval path and independently trusted hash, existing private journal and scratch directories, and an `ISubmissionReviewDeliveryTransport`. `CrestronSubmissionTransport` supplies the real upload and SMTP implementations; tests can supply simulated implementations.

```csharp
var receipt = await SubmissionReviewRequestDelivery.ExecuteAsync(
    requestDirectory, approvedPlan, approvalPath, independentlyApprovedHash,
    privateJournalDirectory, privateScratchDirectory, transport,
    cancellationToken: cancellationToken);
```

Before each pending upload or email the coordinator checks the receipt and preparation marker, retained source review, inventory, mapping, declarations, document disposition, reports, any retained Android audit/pins, and actual outgoing bytes. It reassesses the pinned archive's candidate, package, policy, observations and evidence at the current time, then verifies the independently pinned approval again. Evidence verification remains separate from overall document completeness. A failed original test remains failed even when the developer explicitly requests review with that gap.

`SubmissionReviewRequestDelivery.Check` performs the request and evidence checks without approving or sending anything. It preserves original validation outcomes in the returned report. Its scope is byte integrity and evidence reassessment; it does not authenticate the producer, repeat physical tests, perform visual review or reinterpret PDF content. Exact generated documents and the original full inventory/mapping must already have been independently reviewed. It neither releases processor reservations nor restores physical equipment.

Execution uses the existing durable journal and freezes outgoing bytes. Changed approval or evidence after upload stops email while retaining the confirmed upload. An uncertain provider outcome is not automatically retried; use the [journal's reconciliation procedure](DeliveryJournal.md). Cooperating workers must share the same durable journal. A completed operation returns its existing receipt without transmitting again. `Submitted` records provider-confirmed upload and send, not inbox delivery or a Crestron decision.

The released 1.13.1 coordinator accepts the output of [unsigned request preparation](ReviewRequest.md): `UnsignedSelfTest` or `DisclosureOnly`. The current source additionally accepts the declared-gap output of [signed review preparation](SigningStage.md). It never applies signatures during delivery or bypasses required-document validation.

For a signed declared-gap request, use the signed-review directory as `requestDirectory`, set `ReviewSha256` to the independently reviewed `signed-review-receipt.json` hash, and select `SignedSelfTest`. Map `signedFormSha256` and `signedFormFileName` to the plan's attachment fields. Preserve the receipt's exact verification status and declarations digest, supply the reviewed public gap summary, and leave `DocumentOmissions` null. This mode checks the retained signing report and original unsigned review as well as freshly reassessing the evidence archive. The signature source image is not needed or transmitted. Use the same approval preview and protected command below; legacy complete-only delivery commands remain separate.

## Protected console and CI delivery

```text
CrestronHomeDevTools.Console submission-review-request-deliver --settings PRIVATE_DISPATCH_JSON --settings-sha256 INDEPENDENT_DISPATCH_SHA256 --execute-approved
```

This is a noninteractive command. Supply the same bounded private credentials JSON on standard input as [complete delivery](DeliveryCommand.md): `uploadUserName`, `uploadPassword`, `smtpUserName`, `smtpPassword`. Never place credentials on the command line or in the dispatch settings. Console output contains only provider progress, or a neutral failure category; inspect the private journal for details. Unknown outcomes require reconciliation before another attempt.

The dispatch settings have these exact camel-case fields:

| Fields | Value |
|---|---|
| `schemaVersion` | `1` for this unsigned-request command |
| `plan` | The final independently approved `SubmissionReviewDeliveryPlan`, with string enum values |
| `requestDirectory` | Absolute prepared-request directory |
| `approvalPath`, `approvalSha256` | Absolute approval path and separately trusted hash matching the plan |
| `journalDirectory`, `scratchDirectory`, `uploadReceiptDirectory`, `mailReceiptDirectory` | Existing private absolute directories, separate from one another and the request directory; none nested inside another |
| `reviewedUploadFormSha256`, `acceptedUploadTermsSha256` | Independently reviewed uploader form and accepted terms hashes, as in complete delivery |
| `uploadTimeoutSeconds`, `mailTimeoutSeconds` | Each between 1 and 600 |
| `smtpHost`, `smtpPort` | DNS hostname and TLS port 465 or 587 |
| `tooling` | Required by the CI template: `consoleDirectory` and `consoleFiles`, where each entry contains `relativePath` and `sha256` for one installed console file |

The private coordinator must supply and independently pin the settings; approval of package correspondence does not approve arbitrary SMTP endpoints or tooling. A convenience settings generator for this route remains to be added. The complete-only `submission-delivery-settings` generator must not be used with unsigned request files.

The [final-stage CI template](submission-delivery.yml.example) selects `submission-review-request-deliver` only for explicitly declared-gap settings containing a complete tooling inventory. It verifies the independently pinned settings and every installed file before starting the console, retaining the same protected environment, repository/branch restrictions and credential isolation as complete delivery. It never changes mode automatically after a validation failure. Ordinary library, client, driver and test-package releases need not invoke this optional submission stage.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This independent project is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.
