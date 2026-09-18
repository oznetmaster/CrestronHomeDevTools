# Signing a reviewed self-test form

Command examples use the [bundled submission console](ConsoleTools.md); see that guide for source/release availability and setup.

The source tool `tools/submission/sign_self_test_form.py` applies a private PNG or JPEG signature and a date to the two reviewed official text fields. It preserves the printed pages, companion matrix and checkbox values. This is an image signature, not a certificate-backed PDF signature, authentication of the test producer, permission to send email, or evidence of Crestron acceptance.

This feature requires the matching DevTools source checkout and the dependencies in `tools/submission/requirements.txt`. It has been tested with synthetic forms/signatures, including the actual offline evidence validator. A real developer signature and final candidate signing remain separate validation steps.

## Prepare and review the exact signing copy

Use all the evidence arguments documented in [FormGeneration.md](FormGeneration.md), but select `for-signing` instead of `from-evidence`. The same .NET evidence validator and complete official-field mapping are required. Drafts and ordinary unsigned review copies cannot be signed by this tool.

Alternatively, use the [private CI review stage](ReviewStage.md) with `--prepare-for-signing` (workflow input `prepare_for_signing: true`). It retains the signing copy, exact form report and validated evidence bundle together. Use its `formSha256` and `formReportSha256` when reviewing the subsequent authorization; do not regenerate the form between review and signing. The release pins and signing authorization still come from separately trusted stages.

The optional [private signing stage](SigningStage.md) revalidates that retained bundle at signing time, applies the separately authorized signature and isolates the exact package/form delivery files. It includes a protected-job template, but does not enable real signing or delivery automatically.

The signing copy uses a neutral evidence-summary heading so it remains correct after signing. Its signature/date fields are still blank, its filename ends in `.review.pdf`, and its report records `signingCopy: true` and `submissionReady: false`. Render and review every page, including non-applicability rationales, before authorizing the exact PDF. A previously approved draft or a different form hash is insufficient.

Retain the package, evidence and report as immutable private candidate artifacts. Signing does not rebuild the package or rerun hardware tests. Candidate validation and evidence freshness must still be checked by the final submission gate.

## Private authorization

Create an authorization document in a protected signing job or after explicit developer review. Supply its SHA-256 through that trusted job independently of the hardware/evidence worker. Merely finding a JSON file beside the evidence is not authorization. The file and hash are integrity bindings, not proof of who approved them; account/environment permissions and the reviewed workflow provide that trust.

```json
{
  "schemaVersion": 1,
  "formSha256": "REVIEWED_SIGNING_COPY_SHA256",
  "formReportSha256": "EXACT_FORM_REPORT_SHA256",
  "inventorySha256": "REVIEWED_OFFICIAL_INVENTORY_SHA256",
  "candidateSha256": "APPROVED_CANDIDATE_SHA256",
  "packageSha256": "APPROVED_PACKAGE_SHA256",
  "signatureImageSha256": "PRIVATE_SIGNATURE_IMAGE_SHA256",
  "signer": "Authorized developer name",
  "signingDate": "2026-09-17",
  "expiresUtc": "2026-09-17T18:00:00Z",
  "signatureField": "Text95",
  "dateField": "Date93_af_date",
  "visualReviewCompleted": true,
  "signatureAuthorized": true
}
```

The date and expiry above are illustrative. The signing date must match the current UTC date and authorization must not have expired. Review the signature/date field roles against the pinned official inventory; do not infer them from field order. The names shown correspond to the currently inspected official inventories and are not a promise about future template revisions.

Keep the original signature image, authorization and unsigned/signed forms private. A signature image may contain camera metadata: the tool reconstructs its pixels before embedding, omitting the source image's EXIF metadata. Use a single PNG or JPEG up to 4096 pixels per side and 10 MiB. The image is fitted inside the signature field without cropping; inspect legibility on the rendered final PDF.

## Apply and verify

```text
CrestronHomeDevTools.Console.exe submission sign-self-test-form --form Driver-Self-Test.review.pdf --form-report form-report.json --inventory INVENTORY.json --authorization PRIVATE_AUTHORIZATION.json --authorization-sha256 TRUSTED_AUTHORIZATION_SHA256 --signature-image PRIVATE_SIGNATURE.jpg --output Driver-Self-Test.signed.pdf --report signing-report.json
```

The tool rejects changed inputs, incomplete decisions, inconsistent validation receipts, nonblank signing fields, expired/missing approval and existing output paths. It fills canonical field values and their visible appearances, then marks all form fields read-only. The PDF remains interactive; read-only flags are not tamper-proof protection. Retain and verify the resulting signed-form hash in the final delivery bundle.

After writing, the tool reopens the PDF and checks its canonical fields, page widgets, visible appearance streams and unchanged printed page content. Render every signed page and review it before delivery. The output report deliberately retains `visualReviewRequired: true` and `submissionReady: false`; this signing operation does not complete the delivery gate. A PDF left without its successful report is not a completed signing operation.

No signature image, authorization document, raw evidence or private credentials are automatically attached to an email or uploaded. Delivery uses the separately authorized [delivery journal](DeliveryJournal.md) and still needs supported provider adapters.
