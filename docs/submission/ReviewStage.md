# Optional private CI review stage

Command examples use the [bundled submission console](ConsoleTools.md); see that guide for source/release availability and setup.

The source `tools/submission/prepare_review.py` connects evidence validation, unsigned form generation and private evidence retention. This is the review preparation portion of the final submission stage. It does not execute missing tests, authenticate the evidence producer, approve a policy, render/sign a form or upload/email a submission. The bundled console supplies this command and its matching validator. The optional signing-copy handoff described below requires the 1.6.0 tagged source or later; see [source availability](../../README.md#get-started).

Use this only after testing an immutable actual-driver Release candidate. Ordinary build/test/publication jobs remain independent. Client/library and processor-test releases must not invoke it. The command rejects those artifact kinds and Debug revision numbers. A GitHub release does not imply portal submission.

The source [Android evidence audit](AndroidEvidence.md) checks the Android workflow's retained run before scoped observations are prepared. The source addition described below connects it to this review stage; it is not included in the 1.8.0 tag. The stage re-audits raw results before form generation and again before completing the review. It does not authenticate a producer, and an audit receipt alone cannot fill missing official requirements.

The trusted release job supplies four independent pins: candidate declaration SHA-256, reviewed official inventory SHA-256, reviewed form mapping SHA-256 and full source commit. Do not calculate replacement pins from untrusted hardware-worker output. The candidate declaration already pins the package, policy and official template. The caller is responsible for establishing the driver identity, complete applicable policy and trusted evidence provenance.

Create a private settings file on the worker with exactly these fields (replace the example paths):

```json
{
  "schemaVersion": 1,
  "candidate": "C:/CI/Private/candidate.json",
  "inventory": "C:/CI/Private/inventory.json",
  "mapping": "C:/CI/Private/mapping.json",
  "policy": "C:/CI/Private/policy.json",
  "observations": "C:/CI/Private/observations.json",
  "package": "C:/CI/Private/NeilColvin_Platform_Example_IP.pkg",
  "template": "C:/CI/Private/Extension-Test-Plan.pdf",
  "evidence": "C:/CI/Private/evidence",
  "output": "C:/CI/Private/reviews/unique-release-attempt",
  "title": "Example driver release self-test review",
  "author": "Example Developer"
}
```

The output's parent must already exist with appropriate private access rules. Use a unique attempt directory; an existing output is never overwritten. No credentials are needed for this offline stage. Raw screenshots, household details and observation rationales can be private, so do not upload these outputs as public Actions artifacts or GitHub release assets.

```text
CrestronHomeDevTools.Console.exe submission prepare-review --settings PRIVATE_SETTINGS_FILE --artifact-kind driver --source-commit FULL_RELEASE_COMMIT --candidate-sha256 TRUSTED_CANDIDATE_SHA256 --inventory-sha256 REVIEWED_INVENTORY_SHA256 --mapping-sha256 REVIEWED_MAPPING_SHA256
```

The command invokes the real .NET evidence validator before filling any checkbox, then creates a bundle whose copied contents are validated again. Form and bundle must identify the same candidate, observations and package. A mutation that invalidates retained evidence between stages fails the operation. Inventory and mapping copies are retained against their independent pins. The signature/date fields remain blank.

When this review will be followed by signing, add `--prepare-for-signing`. This uses the same evidence checks but prepares the unsigned signing copy documented in [FormSigning.md](FormSigning.md), with a neutral companion heading that remains correct after signing. The receipt and form report identify `signingCopy: true`; the receipt also pins the form report's exact bytes in `formReportSha256`. Review and authorize those exact retained files, then pass them directly to the signing tool without regenerating the PDF. This flag neither applies a signature nor grants signing or delivery permission. Omitting it preserves ordinary review output, which the signing tool refuses.

Success produces `self-test.review.pdf`, form/bundle reports, `evidence.zip`, the reviewed inventory/mapping, `review-receipt.json` and finally `COMPLETE`. The completion marker contains the receipt SHA-256. A missing marker, nonzero exit or missing receipt is incomplete; never consume a partially published directory after a crash. These local hashes detect content changes but do not authenticate the worker. Keep independent trusted records and verify files again before any future signing/delivery stage.

The receipt deliberately says `UnsignedReviewPrepared`, `submissionReady: false` and `deliveryAttempted: false`. Visual inspection of every rendered page, policy approval, producer authentication, signature authorization and supported delivery remain required. A successful review job is not a successful submission.

## GitHub integration template

### Include Android evidence in the review

When the policy contains an `execution.method` of `android`, review preparation requires Android evidence and independent pins. Other producers that use Android, including combined UI/device checks, must opt into the same audit. Non-Android policies can still use the original invocation. This checks every run named by the trusted coordinator; deciding that those runs cover every applicable assertion remains a separate producer-binding review.

Add this optional field to the private settings:

```json
"androidEvidence": [
  { "runId": "0123456789abcdef0123456789abcdef", "path": "C:/CI/Private/run/AndroidUI" }
]
```

Separately retain a pins document outside the test worker's control. Its values must come from the trusted coordinator's pre-execution records, not fresh hashes of returned results:

```json
{
  "schemaVersion": 1,
  "candidateSha256": "TRUSTED_CANDIDATE_SHA256",
  "runs": [
    {
      "runId": "0123456789abcdef0123456789abcdef",
      "assembly": "Example.AndroidTests.dll",
      "assemblySha256": "PRE_EXECUTION_ASSEMBLY_SHA256",
      "discoverySha256": "PRE_EXECUTION_DISCOVERY_SHA256",
      "producerManifestSha256": "PRE_EXECUTION_MANIFEST_SHA256"
    }
  ]
}
```

Supply `--android-pins PRIVATE_PINS_JSON --android-pins-sha256 TRUSTED_PINS_SHA256` in addition to the original review arguments. All hashes shown above are placeholders. The private paths locate evidence only; they cannot change the trusted run inventory. Missing/extra/duplicate runs, failed audits or evidence changes during review preparation prevent completion. Existing passing audit reports are not accepted as a substitute for raw results.

The review retains `android-pins.json` and `android-audit.json` beside its other private outputs and records both hashes in `review-receipt.json`. Signing, delivery preparation and pre-send revalidation check and preserve those exact reports privately. Neither report enters the outbound delivery folder. Raw Android output must be retained separately; the existing evidence ZIP still contains the files explicitly referenced by the scoped observations. The new reports do not create observation mappings or fill additional form checkboxes. Producer authentication, reviewed policy completeness and final authorization remain required.

### Configure the private job

For Android evidence, supply the independent `android_pins_sha256` workflow input and configure `CRESTRON_SUBMISSION_ANDROID_PINS` as the private retained pins-file path on the review worker. This digest comes from the trusted coordinator, not the test worker's results. Omission cannot bypass a policy's Android-method requirements. The modified template and the review/signing/delivery integration require source newer than 1.8.0; pin the reviewed commit that contains them.

[submission-review.yml.example](submission-review.yml.example) is a reusable workflow template for a private orchestration repository and a dedicated Windows worker. Copy it to `.github/workflows/submission-review.yml`, replace the tooling commit placeholder with an audited full commit, and install PowerShell 7 and the .NET 10 SDK on that worker. The build script provisions the pinned internal runtime automatically. The private environment variable `CRESTRON_SUBMISSION_REVIEW_SETTINGS` points to its settings file. Omit `dotnet` and `validator`; the prepared console selects its matching validator.

Call it as a separate downstream job with `needs` referencing the successful release/evidence jobs. Set `enabled` only through an explicit driver submission choice. Its optional `prepare_for_signing` boolean defaults to false; selecting it only prepares the unsigned signing copy. The template never changes ordinary release gates and does not upload private outputs. Use a private, trusted orchestration repository; do not call it from pull-request workflows or expose the worker to untrusted source. Current rollout policies still lack complete real Release evidence, so no production submission job has been enabled by adding this template.

On 16 September 2026, a separate manual workflow executed the submission validation and all document-tool integration tests under the installed Windows GitHub runner service (NETWORK SERVICE), using DevTools source commit `889441c8fb2406d70c0958acb42b7a68848d8687`. This includes actual .NET/Python review preparation with clearly labelled synthetic evidence and rejection when evidence changes between form generation and bundle creation. No processor, emulator, real signature or sender was used. The reusable template itself still needs validation with a real reviewed Release candidate; this service check is not driver acceptance evidence.

### Windows service workers

The bundled console removes the need to install or select a Python interpreter on the service account. Use the same complete console and private settings as an interactive run. A source-pinned workflow calls `BuildSubmissionConsole.ps1` to provision its runtime automatically. The earlier September16 service rehearsal predates this packaging change; packaged-console service validation and real Release-candidate workflow validation remain separate from local offline acceptance.
