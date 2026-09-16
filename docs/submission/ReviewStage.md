# Optional private CI review stage

The source `tools/submission/prepare_review.py` connects evidence validation, unsigned form generation and private evidence retention. This is the review preparation portion of the final submission stage. It does not execute missing tests, authenticate the evidence producer, approve a policy, render/sign a form or upload/email a submission. It is not in the published 1.4.0 tools.

Use this only after testing an immutable actual-driver Release candidate. Ordinary build/test/publication jobs remain independent. Client/library and processor-test releases must not invoke it. The command rejects those artifact kinds and Debug revision numbers. A GitHub release does not imply portal submission.

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
  "dotnet": "C:/Program Files/dotnet/dotnet.exe",
  "validator": "C:/CI/Tools/CrestronHomeDevTools.Console.dll",
  "output": "C:/CI/Private/reviews/unique-release-attempt",
  "title": "Example driver release self-test review",
  "author": "Example Developer"
}
```

The output's parent must already exist with appropriate private access rules. Use a unique attempt directory; an existing output is never overwritten. No credentials are needed for this offline stage. Raw screenshots, household details and observation rationales can be private, so do not upload these outputs as public Actions artifacts or GitHub release assets.

```text
python tools/submission/prepare_review.py --settings PRIVATE_SETTINGS_FILE --artifact-kind driver --source-commit FULL_RELEASE_COMMIT --candidate-sha256 TRUSTED_CANDIDATE_SHA256 --inventory-sha256 REVIEWED_INVENTORY_SHA256 --mapping-sha256 REVIEWED_MAPPING_SHA256
```

The command invokes the real .NET evidence validator before filling any checkbox, then creates a bundle whose copied contents are validated again. Form and bundle must identify the same candidate, observations and package. A mutation that invalidates retained evidence between stages fails the operation. Inventory and mapping copies are retained against their independent pins. The signature/date fields remain blank.

Success produces `self-test.review.pdf`, form/bundle reports, `evidence.zip`, the reviewed inventory/mapping, `review-receipt.json` and finally `COMPLETE`. The completion marker contains the receipt SHA-256. A missing marker, nonzero exit or missing receipt is incomplete; never consume a partially published directory after a crash. These local hashes detect content changes but do not authenticate the worker. Keep independent trusted records and verify files again before any future signing/delivery stage.

The receipt deliberately says `UnsignedReviewPrepared`, `submissionReady: false` and `deliveryAttempted: false`. Visual inspection of every rendered page, policy approval, producer authentication, signature authorization and supported delivery remain required. A successful review job is not a successful submission.

## GitHub integration template

[submission-review.yml.example](submission-review.yml.example) is a reusable workflow template for a private orchestration repository and a dedicated Windows worker. Copy it to `.github/workflows/submission-review.yml`, replace the tooling commit placeholder with an audited full commit, and install the documented Python dependencies and .NET 10 on that worker. The private environment variable `CRESTRON_SUBMISSION_REVIEW_SETTINGS` points to its settings file. Set `validator` to the console built by the template in that worker's checkout.

Call it as a separate downstream job with `needs` referencing the successful release/evidence jobs. Set `enabled` only through an explicit driver submission choice. The template never changes ordinary release gates and does not upload private outputs. Use a private, trusted orchestration repository; do not call it from pull-request workflows or expose the worker to untrusted source. Current rollout policies still lack complete real Release evidence, so no production submission job has been enabled by adding this template.

On 16 September 2026, a separate manual workflow executed the submission validation and all document-tool integration tests under the installed Windows GitHub runner service (NETWORK SERVICE), using DevTools source commit `889441c8fb2406d70c0958acb42b7a68848d8687`. This includes actual .NET/Python review preparation with clearly labelled synthetic evidence and rejection when evidence changes between form generation and bundle creation. No processor, emulator, real signature or sender was used. The reusable template itself still needs validation with a real reviewed Release candidate; this service check is not driver acceptance evidence.

### Python on a Windows service worker

The ordinary `setup-python` installer failed under this service account. The successful workflow instead used Python's [official NuGet distribution for CI](https://docs.python.org/3.12/using/windows.html#the-nuget-org-packages), extracted into the job's temporary directory. It verified the downloaded package SHA-256, ran `tools/python.exe -m ensurepip --default-pip`, and installed this repository's pinned `tools/submission/requirements.txt` using that same executable. The validated package was `python` 3.12.10, SHA-256 `0eb85c2dfccccf1b17352de4c397f69194035b7d37149eacc16f1147d93de3b8`.

Use an explicitly selected interpreter throughout the job, and review new versions before updating its pin. This portable approach does not require installing Python globally or changing the developer's desktop Python. It is distinct from Python's minimal embedded distribution. Private results and inputs still need appropriate service-account access. See the [full submission plan](../CrestronSubmission.md) for remaining hardware and delivery work.
