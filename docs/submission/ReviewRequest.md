# Prepare an unsigned request with declared omissions

**Requires 1.13.0 or later.** This command prepares a reviewable outbound packet. Continue through [independent approval and protected request delivery](ReviewApproval.md). Preparation itself signs nothing and sends nothing.

Use this stage after [preparing a declared-gap review](DeclaredGaps.md) when the developer deliberately omits a signature or the official self-test form. The internal `.review.pdf` is not an outbound attachment. This stage creates a separate `.request.pdf` with explicit omission wording from the retained, revalidated evidence.

## Choose the document disposition

Create a private JSON file identifying the exact review and its already reviewed gap declarations. The pins below are placeholders for the actual SHA-256 values:

```json
{
  "schemaVersion": 1,
  "reviewReceiptSha256": "REVIEW_RECEIPT_SHA256",
  "candidateSha256": "CANDIDATE_SHA256",
  "declarationsSha256": "REVIEWED_TEST_DECLARATIONS_SHA256",
  "reviewMode": "DeclaredGaps",
  "attachmentKind": "UnsignedSelfTest",
  "omissions": [
    {
      "id": "signature",
      "reason": "The developer has chosen not to sign this incomplete request."
    }
  ]
}
```

`UnsignedSelfTest` includes the original official form with honest checkbox values and blank signature/date fields. To omit the official form as well, choose `DisclosureOnly` and supply a second omission with `id: officialSelfTestForm` and its own reason. The output then contains only the disclosure report, with no official form or signature fields. Duplicate, unknown, missing or empty explanations are refused.

Review the reasons as public text: they appear in the PDF. Retain an independent SHA-256 of this disposition file. No secrets or private paths belong in the reasons. Testing gaps remain in the separate candidate-bound declarations; document omissions cannot hide an undeclared failed or unperformed test.

If testing evidence meets all interpreted requirements but the signature is omitted, the **overall request remains `GapsDeclared`**. Its separate `evidenceVerificationStatus` preserves the testing result. Complete means complete against our interpretation of Crestron's full published submission requirements, including documents. Only Crestron can decide acceptance, publication or certification.

## Run the bundled console

Private settings contain these fields, with paths suited to your computer:

```json
{
  "schemaVersion": 1,
  "reviewDirectory": "C:\\PrivateSubmission\\review",
  "disposition": "C:\\PrivateSubmission\\document-disposition.json",
  "output": "C:\\PrivateSubmission\\request",
  "title": "Example driver submission request",
  "author": "Example Developer"
}
```

Run:

```text
CrestronHomeDevTools.Console submission prepare-review-request --settings PRIVATE_SETTINGS_JSON --review-sha256 REVIEW_RECEIPT_SHA256 --disposition-sha256 REVIEWED_DISPOSITION_SHA256
```

The self-contained Windows console bundles the document runtime and validator; the developer does not need to install Python or edit a Python program. Use a new private output directory. The pins must come from the trusted review/coordinator rather than being substituted from an untrusted worker's output.

## What is retained

The command freezes the original review inputs, revalidates the retained archive and its actual candidate, policy, observations, evidence and declarations, then reconstructs the official-item matrix. It verifies that the current results match the prior reviewed results. Only the intended production Release package and `Driver-Review.request.pdf` enter `delivery/`. The evidence archive, mapping, inventory, declarations, disposition, original review receipt, validation reports and any retained Android audit/pins remain outside that folder.

The original official form's printed content stays unchanged when included. Unsupported items stay unchecked. A disclosure-only report describes verified portions as supporting evidence, never as supplied official checkboxes. No signature is created or inferred. Render and review **every** output page before approving any later stage.

`request-receipt.json` records `UnsignedRequestWithDeclaredGapsPrepared`, the exact attachment and package hashes, document disposition hash, overall and evidence verification statuses, and the source review identity. `signatureApplied`, `deliveryAuthorized`, `deliveryAttempted` and `submissionReady` are false; visual review and producer authentication remain required. The `COMPLETE` marker means preparation finished consistently, not that the submission requirements are complete or that Crestron will accept it.

## Remaining boundaries

This command presently models omission of the official form and signature. Other required documents, including package help, still use the existing validation rules and cannot be silently omitted. The official template and full interpreted requirement inventory are still required internally even when the developer elects not to attach the form.

The retained Android audit is carried forward, not promoted to authenticated producer evidence. The trusted final coordinator must still verify provenance, current evidence/policy validity, reservations and restoration. Omission of a test or document does not authorize a device change or release a reservation.

The source [approval and request coordinator](ReviewApproval.md) connects this prepared receipt to independently pinned final approval, exact correspondence, current per-step evidence checks and the [review delivery API](ReviewDelivery.md). Its separate `submission-review-request-deliver` command supports protected dispatch; see the guide for current setup limits. Do not invoke the legacy complete-only delivery command with these files or relabel an unsigned attachment as a signed form.
