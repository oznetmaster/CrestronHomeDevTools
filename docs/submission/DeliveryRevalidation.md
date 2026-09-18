# Revalidate a prepared delivery before each external step

Command examples use the [bundled submission console](ConsoleTools.md); see that guide for source/release availability and setup.

Current source provides `tools/submission/revalidate_delivery.py`. It is not included in the published DevTools 1.7.0 package. It supplies the offline signed-review and evidence checks needed by the [guarded delivery callback](DeliveryJournal.md#authorization-immediately-before-each-step). It never uploads or sends mail. The source [process bridge](DeliveryProcessBridge.md) supplies the guarded callback invocation; provider adapters and real protected-worker validation remain separate.

Use this only for an explicitly authorized driver submission. Ordinary driver/library releases and test workflows do not need it.

## Private inputs

Retain the completed original [delivery preparation](DeliveryPreparation.md), its original preparation settings, original unsigned and signed reviews, current authorization file and the independently approved hashes. The protected orchestration job supplies the hashes; do not derive trusted values from the files that an evidence worker has just produced.

```json
{
  "schemaVersion": 1,
  "preparedDirectory": "C:/CI/Private/delivery-preparations/approved-attempt",
  "preparationSettings": "C:/CI/Private/settings/delivery.json",
  "output": "C:/CI/Private/revalidations/before-upload-unique-attempt"
}
```

Use an existing private parent for `output` and a new output directory for every check. The output must be separate from retained review trees and settings. Files inherit that parent's protection. Credentials, signatures, raw evidence and private paths must not enter public workflow artifacts or logs.

```text
CrestronHomeDevTools.Console.exe submission revalidate-delivery --settings PRIVATE_REVALIDATION_SETTINGS --delivery-review-sha256 APPROVED_DELIVERY_RECEIPT_SHA256 --signed-review-sha256 APPROVED_SIGNED_REVIEW_SHA256 --authorization-sha256 APPROVED_FINAL_AUTHORIZATION_SHA256
```

Pin and protect the tooling checkout, its Python dependencies and the actual .NET validator selected by the original preparation settings. The [review-stage prerequisites](ReviewStage.md) apply. Configure these paths from trusted orchestration, not pull-request inputs. The revalidator verifies artifacts and authorization content; it does not authenticate whoever supplies the pins, enforce external access-control policy, or discover an approver's intent from a JSON boolean.

## What is rechecked

The command verifies the original completion marker, receipt, plan, retained signed-review receipt, authorization and validation-report hashes. It then runs the complete preparation chain again against the original unsigned/signed reviews and original current approval file, including the real `.NET submission-bundle-check` validator and production Release identity check.

The regenerated plan must match the original plan bytes exactly. Both actual prepared send files must still match their approved hashes; checking only upstream copies is insufficient. Finally, the original handoff marker and original authorization file are read again, and approval expiry is checked again after the potentially lengthy validation.

Removing or changing the original authorization blocks the check even if the preparation directory retains an old copy. Changed evidence, changed recipients, altered package/PDF bytes, missing completion, validation failure and expired approval all block usable output. Revalidation does not renew or extend an approval. An external revocation system, if used, must also be consulted by the trusted callback.

## Result and dispatch integration

Successful stdout is private JSON with state `DeliveryRevalidated`, the exact `plan`, original `expiresUtc`, original approval/review pins and fresh validation-report hash. It records `deliveryAttempted: false` and `submissionReady: false`. It is a revalidation result, not a provider receipt or certification.

The private output preserves a fresh preparation chain, exact delivery copies and `revalidation-receipt.json`. Its `COMPLETE` marker hashes **that revalidation receipt**. It is a different handoff type from the original preparation; do not feed it back as `preparedDirectory`. Original signature images, evidence ZIP and private settings are not copied into the output. Failed or interrupted outputs lack a usable completion marker and must not be overwritten.

Use the [source process bridge](DeliveryProcessBridge.md) to connect this command to `SubmissionDelivery.ExecuteAuthorizedAsync`. It implements the following checks; custom integrations must preserve them:

1. Hold the existing durable delivery journal and invoke this command from the trusted callback separately before Upload and Send, using a distinct private revalidation output each time. Do not reuse the before-upload result after uploading.
2. Check successful process exit, completed private output and receipt hash. Parse its `plan` as `SubmissionDeliveryPlan` and require `SubmissionDelivery.PlanDigest(returnedPlan)` to equal the plan being dispatched. A JSON file's SHA-256 is not this API digest.
3. Return `SubmissionDeliveryAuthorization` using that checked digest and the verified original `expiresUtc`. The API checks expiry and cancellation again before recording the next external intent.
4. If revalidation fails after an upload, preserve the `Uploaded` receipt and send no email. Retain the same durable journal for every attempt. Neither a fresh revalidation directory nor a changed approval permits bypassing uncertain delivery state.

The callback must not recursively read or execute the delivery journal while its lock is held. This command only touches its own private review inputs/output and the offline evidence validator. A production callback must capture command output privately and enforce a bounded process deadline; a hung or failed validator is not permission to continue delivery.

The source process bridge is integration-tested with this command and synthetic providers. The supported upload adapter, mail-provider configuration, actual protected-worker setup and controlled end-to-end delivery still need implementation/validation. Source revalidation and synthetic tests do not establish those capabilities.

## Validation

Tests use synthetic signed forms and approvals with the real .NET evidence validator. They cover the actual CLI, exact-plan preservation, altered prepared files, removed/changed authorization, failed or changed original evidence, expiry after validation, mid-validation handoff changes, independent pin mismatches, output separation and interrupted publication. No upload or email transport is invoked.
