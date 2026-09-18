# Final delivery preparation

`tools/submission/prepare_delivery.py` connects the [private signing stage](SigningStage.md) to the [delivery journal](DeliveryJournal.md). It prepares a `SubmissionDeliveryPlan` after final signed-page review and separate delivery approval. It never uploads a package or sends mail. This source addition is not included in the 1.5.0 tag or NuGet package.

Use this optional stage only for a driver being submitted to the portal. Ordinary releases, libraries and processor test packages do not need it.

## Review and authorization

Render and review every page of the exact signed PDF, including its evidence companion pages. Review the package and intended sender as well. The sender receives Crestron's correspondence. A signature authorization does not authorize delivery.

The protected approval process must supply the signed-review receipt hash and a separate final authorization hash independently of the evidence worker. Matching JSON and hashes do not authenticate an approver; the protected workflow and private access rules establish that authority. The authorization has exactly these fields:

```json
{
  "schemaVersion": 1,
  "signedReviewSha256": "SHA256_OF_APPROVED_SIGNED_REVIEW_RECEIPT",
  "candidateSha256": "SHA256_FROM_APPROVED_SIGNED_REVIEW",
  "packageSha256": "SHA256_OF_APPROVED_PACKAGE",
  "signedFormSha256": "SHA256_OF_VISUALLY_REVIEWED_SIGNED_PDF",
  "sender": "developer@example.org",
  "recipient": "drivers@crestron.com",
  "subject": "Driver Submission Package",
  "expiresUtc": "REPLACE_WITH_APPROVED_UTC_EXPIRY",
  "signedVisualReviewCompleted": true,
  "deliveryAuthorized": true
}
```

These values illustrate the schema; they are not an approval. Pins must be lowercase SHA-256. The recipient and subject follow the [published submission procedure](https://sdkcon78221.crestron.com/sdk/Crestron_Certified_Drivers_SDK/Content/Topics/Submit-a-Driver/Submit-a-Driver.htm). The sender must be one plain ASCII mailbox, without display names or address lists.

## Private settings and command

Retain the original completed signing-copy review, signed review and approval privately. Choose a new output directory under an existing private parent:

```json
{
  "schemaVersion": 1,
  "signedReviewDirectory": "C:/CI/Private/signed-reviews/approved-attempt",
  "reviewDirectory": "C:/CI/Private/reviews/approved-attempt",
  "authorization": "C:/CI/Private/approvals/delivery.json",
  "dotnet": "C:/Program Files/dotnet/dotnet.exe",
  "validator": "C:/CI/Tools/CrestronHomeDevTools.Console.dll",
  "output": "C:/CI/Private/delivery-preparations/unique-attempt"
}
```

```text
python tools/submission/prepare_delivery.py --settings PRIVATE_DELIVERY_SETTINGS --signed-review-sha256 TRUSTED_SIGNED_REVIEW_SHA256 --authorization-sha256 TRUSTED_FINAL_AUTHORIZATION_SHA256
```

Use a pinned source checkout and the dependencies described in [ReviewStage.md](ReviewStage.md). The command checks both completion markers, the signing report and review chain, exact file hashes, final approval and expiry. It freezes the package, signed PDF and retained evidence, runs the .NET bundle validator again, checks the production Release source and checks expiry again after validation. It does not regenerate or resign the PDF.

The private output contains:

- `delivery/`: only the exact `.pkg` and signed self-test PDF.
- `delivery-plan.json`: the nine fields of `SubmissionDeliveryPlan`; `reviewSha256` identifies the signed review and `authorizationSha256` identifies final delivery approval.
- `delivery-authorization.json`, `signed-review-receipt.json` and `validation-report.json`: retained approval and verification inputs/results.
- `delivery-review-receipt.json`: preparation identity, expiry and state.
- `COMPLETE`: the preparation receipt hash, written last.

The evidence ZIP, original signature image and worker settings are not copied into the output. Keep the entire output private: the form can contain evidence details, and the sender is correspondence information. Only the files in `delivery/` are intended for approved disclosure. A directory without its completion marker is unusable. Existing outputs are never overwritten.

`planFileSha256` hashes the JSON file bytes. It is not `SubmissionDelivery.PlanDigest`, which hashes the journal's own serialized representation. Verify the file pin before deserializing the plan.

## CI and dispatch

[submission-delivery-preparation.yml.example](submission-delivery-preparation.yml.example) provides an explicitly selected job for a trusted private orchestration repository. Configure the named environment's reviewers, branch restrictions, trusted worker and private settings. Pin its tooling checkout to an audited full commit containing these tools. The template does not configure protections, grant publishing access or send anything. Do not run untrusted pull-request code on the worker.

Preparation records `DeliveryPlanPrepared`, `deliveryAuthorized: true`, `deliveryAttempted: false` and `submissionReady: false`. The reviewed files and plan are prepared; supported transport configuration and dispatch remain outstanding.

The source [delivery revalidation command](DeliveryRevalidation.md) repeats the complete offline chain against the original approval and evidence and verifies the actual prepared send files. It is not in the published 1.7.0 package; guarded callback invocation and real transport integration remain separate work.

Before dispatch, the trusted transport job must revalidate the completed handoff, original approval and current expiry, current evidence/policy validity, and intended sender account. Run preparation immediately before dispatch; retaining a plan does not permit sending indefinitely. The published `SubmissionDelivery.ExecuteAsync` primitive does not enforce approval expiry or evaluate evidence. Current source adds [guarded dispatch](DeliveryJournal.md#authorization-immediately-before-each-step), which requires trusted revalidation before both upload and email and enforces the returned approval expiry and plan identity. Its callback still has to perform the complete handoff, evidence and approval checks; merely returning a matching digest is insufficient. The guarded API is not in the published 1.7.0 package. All attempts must use the same durable journal, including new preparations of the same files; never bypass an uncertain upload or send with a fresh directory.

The transport must follow the journal's no-replay/reconciliation rules, upload only the package, and attach only the signed PDF to the documented email containing the returned download URL. A supported uploader, configured mail provider, controlled-recipient validation and an authorized real submission remain pending. A provider receipt is not Crestron approval or certification.

## Validation

Synthetic integration tests exercise the real preparation CLI and .NET evidence validator, exact signed package/form bytes, separate final approval, expiry during validation, altered inputs, missing completion, failed/mismatched validation, sender/header rejection and interrupted publication. The signatures and approvals are explicitly synthetic. No test invokes an upload or mail provider.
