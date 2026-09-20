# Private signing stage

Command examples use the [bundled submission console](ConsoleTools.md); see that guide for source/release availability and setup.

The source tool `tools/submission/prepare_signed_review.py` connects a completed [unsigned signing-copy review](ReviewStage.md) to the [image signer](FormSigning.md). It revalidates the retained evidence at signing time and prepares the exact package and signed form in a separate delivery folder. It never uploads or sends them. Use the complete matching console as described in the setup guide above.

Use it only after the final driver Release candidate and all applicable evidence have passed review. Ordinary driver/library releases remain independent of portal submission. A completed signing stage means that authorized files were prepared, not that a driver was submitted or certified.

Version 1.14.0 and later accept an explicitly reviewed declared-gap signing copy as described in [Form signing](FormSigning.md#signing-with-declared-gaps-source-update). It reassesses the declared-gap archive, binds the exact declarations and verification status to the signing authorization, and preserves unchecked requirements. For this mode, the output also retains the evidence archive, declarations, mapping, inventory and unsigned form/report privately so the review-delivery coordinator can revalidate before each provider step. Only the package and signed PDF enter `delivery/`.

## Trust and private inputs

The release/review job supplies the SHA-256 of `review-receipt.json` independently of the worker's settings. The signing authority supplies a separate SHA-256 for the authorization document defined in [FormSigning.md](FormSigning.md). Review the exact unsigned PDF and its form report before granting that authorization. Neither a local JSON file nor matching hashes authenticate who approved it; access controls, the reviewed workflow and the protected job establish that trust.

Keep the signature image and authorization on the signing worker, outside source control and public artifacts. The hardware/evidence worker should not gain access to them. Configure a trusted private orchestration repository and a signing environment with required reviewers and restricted branches. Do not grant this worker to pull requests or untrusted code. A GitHub environment name or runner label alone does not provide isolation or approval; configure and verify those controls before enabling real signing.

The private settings file has exactly these fields:

```json
{
  "schemaVersion": 1,
  "reviewDirectory": "C:/CI/Private/reviews/approved-attempt",
  "authorization": "C:/CI/Private/signing/authorization.json",
  "signatureImage": "C:/CI/Private/signing/signature.jpg",
  "output": "C:/CI/Private/signed-reviews/unique-attempt"
}
```

The output parent must already exist with private access rules. Select a new output directory for each authorized preparation; an existing directory is never replaced. Use the complete console archive described in [Console tools](ConsoleTools.md); no separate interpreter installation is required.

```text
CrestronHomeDevTools.Console.exe submission prepare-signed-review --settings PRIVATE_SIGNING_SETTINGS --review-sha256 TRUSTED_REVIEW_RECEIPT_SHA256 --authorization-sha256 TRUSTED_SIGNING_AUTHORIZATION_SHA256
```

## Checks and outputs

The command requires a completed review created with `--prepare-for-signing`. It freezes and verifies the pinned form, form report, official inventory, mapping and evidence archive, then invokes the real .NET bundle validator again. Stale evidence, changed artifacts, mismatched candidate/observation identities or different authorization stop signing. The signer separately checks the current signing date, expiry, exact form and private image hashes. The retained candidate's source commit must match the review.

Success publishes a private directory containing:

- `delivery/`: exactly the original production `.pkg` and `Driver-Self-Test.signed.pdf`.
- `signing-report.json` and `validation-report.json`: signature and fresh evidence-validation records.
- `review-receipt.json` and `signed-review-receipt.json`: input/output identity bindings and the evidence revalidation time.
- `COMPLETE`: the signed-review receipt's SHA-256, written last.

No evidence ZIP, signature source image, authorization file, worker settings or raw log is copied into `delivery/`. Keep the entire output private until its intended disclosure has been reviewed; an official form's companion matrix can itself contain details from the evidence. Retain the original review and evidence archive separately. The tool does not redact or authorize their contents.

After a crash, a directory without a valid completion marker is incomplete and must not be consumed. The tool will not reuse that directory. Creating another signed copy is preparation only; never use a new directory to bypass an uncertain upload or email recorded by the [delivery journal](DeliveryJournal.md).

The signed-review receipt deliberately retains `visualReviewRequired: true`, `deliveryAuthorized: false`, `submissionReady: false` and `deliveryAttempted: false`. Render and review every signed page before authorizing the exact package/form pair for delivery. The [final delivery preparation stage](DeliveryPreparation.md) rechecks the trusted receipt, file hashes, evidence freshness and its separate delivery authorization before preparing a plan. An old signing receipt alone is insufficient.

## Optional GitHub job

[submission-signing.yml.example](submission-signing.yml.example) is a reusable template for the private orchestration repository. Replace its tooling commit placeholder, configure the `crestron-submission-signing` environment's reviewers/branch restrictions, and set up a trusted Windows worker with the corresponding label. Its private `CRESTRON_SUBMISSION_SIGNING_SETTINGS` environment variable selects the settings file; `validator` must point to the console built from the pinned checkout.

Call this job downstream of successful release/evidence/review jobs, using independently approved receipt and authorization pins. It remains disabled unless explicitly selected. The template does not create an environment, grant access, copy a real signature, configure a sender or upload artifacts. No real signing workflow has been enabled by adding it.

## Validation limits

The actual CLI has been tested with synthetic candidate evidence, the real .NET validator and a clearly marked synthetic signature. Regressions cover changed retained inputs, missing completion, stale/failed validation, identity mismatches, expired or unrelated authorization, exact delivery contents, existing outputs and interrupted publication. The published console has also applied an explicitly authorized signature to a reviewed real Extension form, preserving its checkboxes, identification and linked notes. Its exact signed PDF was subsequently delivered through the public coordinator. That actual run used the interactive Windows account, not a service account; see [validation status](ValidationStatus.md).
