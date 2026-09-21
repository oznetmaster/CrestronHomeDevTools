# Configure the optional submission workflow

Start with the [normal-path runbook](Runbook.md) for stage order, expected results and resuming an interrupted attempt. This page supplies the one-time worker and repository configuration.

These templates belong in your own trusted orchestration repository. They do not depend on the author's private CI repository or require another physical PC. See [development configurations](DevelopmentConfigurations.md) for one or many PCs and processors. Use a private repository for this supplied configuration, with restricted write access and a protected Windows worker; ordinary builds and releases remain independent.

The templates connect the public review, signing, delivery-preparation and delivery commands. They do not run missing acceptance tests or establish that a driver is ready for submission. The public console has completed a real authorized submission. The consuming repository's GitHub protections, worker identity and private credentials still require their own validation; see [verified scope](ValidationStatus.md). Copying the files leaves it disabled until you configure and explicitly enable it.

## Install the five workflow files

Copy these files into `.github/workflows` in your repository, removing the `.example` suffix:

- [submission.yml.example](submission.yml.example): the single manual entry point, **Crestron submission**.
- [submission-review.yml.example](submission-review.yml.example): validate evidence and prepare the unsigned signing copy.
- [submission-signing.yml.example](submission-signing.yml.example): apply the separately authorized signature and retain the signed review.
- [submission-delivery-preparation.yml.example](submission-delivery-preparation.yml.example): prepare an approved delivery plan without sending anything.
- [submission-delivery.yml.example](submission-delivery.yml.example): revalidate and deliver the exact approved artifacts.

Review and replace `REPLACE_WITH_AUDITED_FULL_TOOLING_COMMIT` in the three source-building stages with the full commit of the DevTools release you have accepted. They build the bundled console automatically; developers do not edit or maintain Python. The delivery stage uses a separately installed, fully inventoried console through [bundled delivery settings](DeliverySetup.md). Do not use a moving branch as the tooling pin.

The dispatcher uses relative references to the four copied workflows. All stages use one shared concurrency group with cancellation disabled and `queue: max`; the parent dispatcher deliberately has no concurrency group that could deadlock its child. GitHub currently permits up to 100 pending jobs/runs with this setting; further arrivals can be canceled. Queue order follows when jobs start waiting, not necessarily dispatch order. This is a repository-scoped scheduling aid, not a cross-repository resource lock. See [GitHub's concurrency documentation](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/control-workflow-concurrency). Dispatch the next stage only after the preceding stage has completed and its artifacts have been reviewed.

## Configure the repository and worker

Set these **repository variables** before enabling the workflow:

| Variable | Value |
| --- | --- |
| `CRESTRON_SUBMISSION_REPOSITORY` | Your exact `owner/repository` name. |
| `CRESTRON_SUBMISSION_BRANCH` | Your protected orchestration branch name, without `refs/heads/`. |
| `CRESTRON_SUBMISSION_RUNNER_LABEL` | A custom label selecting the intended protected Windows worker. |
| `CRESTRON_SUBMISSION_STAGES_ENABLED` | Leave absent or `false` during setup; set to `true` only after review. |

The repository and branch checks apply to both the dispatcher and each called stage. Use the configured branch when selecting **Run workflow**. A tag-triggered release has a different ref and will not pass this branch gate; a trusted release pipeline can instead dispatch the selected stage on the protected orchestration branch after its evidence handoff is ready. Enabling dispatch is not signature or delivery authorization.

Use a label that resolves to one intended worker for this profile. All stages need the same persistent private files; do not attach that label to unrelated PCs and rely on whichever runner GitHub selects. Multiple independently configured workers and repositories are supported, each with its own label and private paths. This template deliberately serializes one repository's protected stages; it is not a distributed evidence-storage or failover system. If several repositories share a worker, protect their directories and accounts appropriately and do not share mutable output paths.

The worker may be on the development PC. Keep its identity and private access separate from untrusted source-build jobs. Runner labels select a runner; they are not an access-control boundary. Restrict which repositories/workflows may use the worker, and do not execute untrusted pull-request code with access to signatures, processor credentials or delivery credentials. Install the tools required by the pinned console build, and keep the protected delivery installation outside ephemeral checkout directories.

## Configure protected environments

Create these environments, with branch restrictions and the required human review appropriate to your account's available GitHub protections. Verify the protections actually work before using real signatures or delivery credentials. The templates reference environment names but do not create their protections.

| Environment | Environment variables / secrets |
| --- | --- |
| `crestron-submission-review` | Variable `CRESTRON_SUBMISSION_REVIEW_SETTINGS`: absolute path to reviewed private settings. Optional variable `CRESTRON_SUBMISSION_ANDROID_PINS`: path to independent pre-execution Android pins when required. For declared gaps, `CRESTRON_SUBMISSION_DECLARATIONS`: path to the candidate-bound declarations. |
| `crestron-submission-signing` | Variable `CRESTRON_SUBMISSION_SIGNING_SETTINGS`: absolute path to private signing settings. Optional `CRESTRON_SUBMISSION_CREDENTIAL_BINDINGS`: absolute path to the signing account's private named-input bindings. |
| `crestron-submission-delivery` | Variables `CRESTRON_SUBMISSION_DELIVERY_SETTINGS`, `CRESTRON_SUBMISSION_DISPATCH_SETTINGS` and `CRESTRON_SUBMISSION_DISPATCH_SETTINGS_SHA256`: private preparation settings, reviewed dispatch settings and their independently retained digest. Either variable `CRESTRON_SUBMISSION_CREDENTIAL_BINDINGS` for named provider inputs, or secret `submission_credentials_json` for protected stdin credentials. |

Keep the actual files, signature image, raw evidence and credentials outside source control and public artifacts. Configure their access permissions on the worker. Environment variables in this table contain paths and digests, not file contents. Environment-specific values allow repositories sharing a machine to use different private profiles without changing the runner's global environment.

For the saved-input option, use DevTools 1.17.0 or later and [private input setup](../PrivateInputs.md) to provision only the entries that the actual service identity needs. Set `CRESTRON_SUBMISSION_CREDENTIAL_BINDINGS` separately in each protected environment. Signing bindings select `Signature`; omit `signatureImage` from signing settings. Delivery bindings select `Smtp` and `Uploader`; leave `submission_credentials_json` absent. The templates pass only the absolute binding-file path to the CLI. They do not decrypt a signature to disk, copy secrets to GitHub, collect missing inputs interactively or grant access to the service. Verify decryption under the real worker identity before enabling a stage. Exact signing/delivery approvals remain required.

Without a bindings variable, signing retains its private image-file input and delivery uses its protected environment's `submission_credentials_json` secret. A repository secret of that name may also be passed by the dispatcher, but do not provision a less-protected duplicate merely for convenience. Delivery refuses simultaneous saved bindings and a credentials secret instead of silently choosing one. No provider credentials are needed by the earlier stages. GitHub documents environment-secret precedence for [reusable workflows](https://docs.github.com/en/actions/how-tos/reuse-automations/reuse-workflows). Validate the intended credential resolution with synthetic inputs and a non-delivering rehearsal before enabling real delivery.

Follow the detailed input schemas in [review preparation](ReviewStage.md), [signing](SigningStage.md), [delivery preparation](DeliveryPreparation.md) and [delivery settings](DeliverySetup.md). The templates cannot infer or approve your private paths, sender, signature or evidence policy.

## Advance one reviewed stage at a time

Select **Actions > Crestron submission > Run workflow**, choose the protected branch, tick `enabled`, and select the next stage:

| Stage | Required dispatch inputs | Result |
| --- | --- | --- |
| `review` | Full release `source_commit`; `candidate_sha256`, `inventory_sha256`, `mapping_sha256`; `android_pins_sha256` when the policy requires Android evidence. Select `review_mode`; declared gaps additionally require `declarations_sha256`. | Validated unsigned signing copy and retained evidence. The candidate pin identifies the declaration JSON, not just the package file. |
| `sign` | `review_sha256` for the unsigned review receipt; `authorization_sha256` for its exact signing authorization. | Signed review retained privately. |
| `delivery-plan` | `review_sha256` for the signed review receipt; `authorization_sha256` for the separate delivery authorization; matching `review_mode`. | Complete mode prepares a delivery plan. Declared-gaps mode verifies the exact independently approved plan as described below. Neither sends anything. |
| `deliver` | No new artifact pins in dispatch; the protected environment pins the reviewed dispatch settings and the plan they identify. | Delivery receipts, or a retained failure/uncertain outcome requiring inspection. |

Obtain digests from independently reviewed retained artifacts; do not replace a failed pin with the current file's hash just to pass a check. The dispatcher validates input syntax on a hosted runner before routing a job to the protected worker. Each actual command still validates contents, identities and exact authorization. A green skipped workflow is not a completed stage; inspect the called job and its private completion receipt.

Never automatically retry upload or email after an uncertain outcome. Inspect the [delivery journal](DeliveryJournal.md) and use its supported reconciliation path. Confirmed provider acceptance records submission delivery, not Crestron certification or portal acceptance.

## Signed submissions with declared gaps

Copy the matching updated templates together from DevTools 1.17.3 or later. The dispatcher exposes `review_mode` and `declarations_sha256`; older copies can reject declared-gap signing even with a recent console. These template changes use existing public commands also tested with DevTools 1.17.2. Keep complete mode unless the developer deliberately chooses and explains the gaps.

For `review`, choose `declared-gaps` and supply the reviewed declarations digest. The protected review environment locates the declarations file. The generated signing copy preserves the gaps. The `sign` stage follows the same exact-form approval procedure; its authorization must also identify the declared-gap mode, verification status and declarations digest. An earlier complete-only authorization cannot sign this form.

Before `delivery-plan`, follow [review approval](ReviewApproval.md) to prepare a `SubmissionReviewDeliveryPlan` for the signed-review directory, preview its correspondence, obtain separate approval, and retain the final plan with its exact approval hash. Configure `CRESTRON_SUBMISSION_DELIVERY_SETTINGS` for declared-gaps mode with this different schema:

```json
{
  "schemaVersion": 1,
  "plan": "C:/CI/Private/approved-review-plan.json",
  "planFileSha256": "INDEPENDENTLY_REVIEWED_FINAL_PLAN_SHA256",
  "approval": "C:/CI/Private/delivery-approval.json",
  "output": "C:/CI/Private/checks/new-approval-check.json"
}
```

Use an existing private output parent and a new output filename. Select `delivery-plan` with `review_mode: declared-gaps`, the signed-review receipt digest and the separate delivery-approval digest. The template checks the pinned plan identifies that signed review and approval, then runs `submission-review-approval-check`. It records verification of the exact packet/correspondence authorization; it does **not** repeat evidence validation or prepare the complete-mode delivery directory. A changed plan, mismatched receipt, expired approval or existing output stops the stage.

Next prepare and independently pin the schema-1 dispatch settings in [ReviewApproval.md](ReviewApproval.md#protected-console-and-ci-delivery), using that same final plan and signed-review directory. Set the protected dispatch-settings path and digest. The existing `deliver` stage selects the declared-gap command from those pinned settings. That command revalidates the full retained evidence, outgoing files and approval before upload and again before email. The preparation check is not a substitute for that final validation. Neither stage changes a gap to a pass or determines Crestron's decision.

Requests deliberately omitting a signature or official form use the separate [unsigned request preparation](ReviewRequest.md) path. They do not go through this signed-form dispatcher sequence; the final delivery template can consume their independently approved request settings as documented in ReviewApproval.md.

## Rehearse before real delivery

The Windows console acceptance suite executes the actual review, signing and delivery-preparation template scripts in sequence against the packaged console, with synthetic evidence, Android audit inputs and a synthetic signature. It checks rejection of library submissions and stale authorization/review pins, preservation of private Android evidence, and the resulting delivery plan through fake upload/email transports. Replaying the completed synthetic delivery sends nothing. This is a local command-handoff test: it does not validate GitHub environment protections, the deployed worker account, real evidence or external delivery. The separate template tests check dispatcher routing and input contracts.

Validate wrong-branch/disabled requests, missing settings, stale pins, incorrect candidate identity, missing evidence and missing authorization without touching external providers. Confirm that private artifacts stay on the protected worker and that a second stage cannot overlap the first. Test the intended worker identity and environment protections, including recovery after interruption. Any uploader or email rehearsal needs explicit authorization for its recipient and artifacts.

Only after real candidate tests, endurance, form review, signature authorization and protected handoff are complete should this become the final stage of a driver release workflow. Libraries, clients and processor test packages do not enter submission.

## Windows worker separation validation

On 19 September 2026, a separate Windows GitHub runner service using LocalService passed harmless file-access checks alongside an ordinary source-test runner using NetworkService on the same PC. The submission account could read protected input files, could not modify the input/approval/tooling directories, and could write its output/log directories. The ordinary account was denied reading the probe and writing to the checked submission directories. The submission service was configured for delayed automatic startup.

This validates that particular account and folder separation; it does not isolate the worker from administrators, SYSTEM or other processes using the same service identity. It does not establish restart recovery or GitHub environment approval enforcement. No actual signing material or provider credentials were provisioned by these checks. The synthetic template-chain rehearsal also passed under the ordinary service account; actual external signing and delivery were subsequently completed under the interactive Windows account through the protected public console. That does not establish service-account credential access or a live GitHub Actions send; see [validation status](ValidationStatus.md).
