# Add later evidence before signing

This source-preview path adds results for previously unobserved requirements to
an unsigned review of the **same** driver package. For example, hardware may
become available after endurance finishes. It does not restart endurance, replace
its measurements, change the frozen candidate or grant signing/delivery authority.

The original run, completed-stage receipts and review directory remain intact.
Do not edit frozen settings or overwrite a generated review to add evidence.

1. Complete additional tests through the public test/observation APIs. Retain
   original output and producer identities. New Android observations require
   pre-execution pins and the raw-run audit described in [review preparation](ReviewStage.md).
2. Use [evidence composition](EvidenceComposition.md) with the same candidate,
   source, full policy and official template. Keep every original observation
   unchanged, including failures, timestamps, measurements and file hashes.
   Only previously unobserved requirement IDs can be added through this path.
   A later pass cannot replace an earlier failure, partial result or N/A.
3. Prepare a new unsigned signing copy through `submission prepare-review`, under
   `review-revisions/<name>` in the original run. Use the original candidate,
   inventory and mapping pins. Keep all remaining gap declarations. Remove an
   omission only when the new evidence supports doing so. Inspect the new PDF.
4. Select it in the independently installed protected worker's `Plan`:

   ```json
   "ReviewRevision": {
     "RelativeDirectory": "review-revisions/additional-sensors",
     "OriginalReviewSha256": "SHA256_OF_ORIGINAL_REVIEW_RECEIPT",
     "ReviewSha256": "SHA256_OF_NEW_REVIEW_RECEIPT"
   }
   ```

   Replace these placeholders with independently verified digests. Re-pin the
   protected configuration in its installed task, or pass the new configuration
   and digest to `SubmissionAutomationStages.CreateProtected`. Adding this
   selection to evidence-worker settings alone has no effect.
5. Review the new `signing-request-<review-sha256>.json` in the run root. The
   original `signing-request.json`, if present, remains historical. Supply exact
   approval for the revised form through the independent signing channel.
   Delivery still needs its separate approval.

The protected reader checks both receipt pins, artifact hashes, full revised
bundle validation and unchanged original observations. The candidate declaration,
source, inventory and mapping must match. Signing revalidates the packet through
the bundled document tool. Delivery checks that the selected review was actually
signed. Once signing starts, changing the selection cannot replace that operation;
inspect the retained operation rather than resetting it.

Without `ReviewRevision`, the original review is used. Synthetic tests cover
supplement selection and missing-approval behavior; real supplemental hardware
evidence and revised-packet delivery remain separate validation steps. No result
predicts Crestron acceptance.
