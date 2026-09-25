# From a GitHub release to a submission

The intended entry point is publishing an opted-in **driver** release. One-time private setup supplies the developer, driver, equipment, test plan, credentials and permitted operations. A persistent controller should advance the work; an assistant should be needed for exceptions and product decisions, not to carry files between stages or remember when endurance finishes.

For an already registered release, the source-preview [GitHub entry point](AutomationWorker.md#github-rehearsal-option) offers **Rehearsal** (default) and **Submit**. Rehearsal uses the same permitted tests and unsigned document preparation, then stops before signing or delivery. Submit still requires the exact authorizations. This selector is in Actions **Run workflow**, not GitHub's standard release editor. The optional private Windows watcher detects published releases every 15 minutes and selects the saved profile's mode; no submission files are added to the public driver repository.

**Source preview with a completed release-to-review rehearsal.** This branch implements release discovery/intake, persistent sequencing and a [Windows automation worker](AutomationWorker.md) connecting public tests, endurance, review, signing, delivery and retention. Following live GitHub intake of an existing published release, the configured Windows service completed real candidate deployment, Windows/processor/live/Android tests, temporary-test cleanup, one-hour endurance, evidence export and unsigned review preparation without intervention after startup. The shortened interval and retained qualifications were disclosed. An actual Windows restart separately validated background startup; synthetic integration validated exact-authority waits, signing, provider sequencing and duplicate-send prevention. A newly published release followed through real protected delivery remains unvalidated. See [validation scope](ValidationStatus.md) and the [single starting document](START-DRIVER-SUBMISSION.md).

## What is implemented

`GitHubSubmissionRelease` resolves the published tag to its actual commit instead of treating GitHub's `target_commitish` branch as a commit. It selects the exact configured `.pkg` asset, checks its uploaded state and published SHA-256, downloads through the GitHub asset endpoint, verifies length and digest, and checks identity again after transfer. A release published before its package finishes uploading waits. Drafts and, by default, prereleases are not selected. An existing retained package is verified, never overwritten. Credentials on the supplied `HttpClient` are not inferred from a token prefix or fixed length.

`SubmissionReleaseIntake` checks the configured frozen setup and tooling files against their recorded digests, retains the candidate and release metadata, and opens the corresponding workflow. It does not execute downloaded code or start tests. See GitHub's [release metadata](https://docs.github.com/en/rest/releases/releases) and [asset API](https://docs.github.com/en/rest/releases/assets).

`SubmissionWorkflow` persists one run per repository/release ID. Its ordered stages are:

1. Validate candidate and coverage.
2. Run Windows NUnit tests.
3. Run processor NUnit tests.
4. Run applicable app, configuration and physical-device tests.
5. Collect, verify and retain endurance evidence.
6. Prepare the review packet.
7. Apply the authorized signature.
8. Upload, verify the returned download and send the authorized email.
9. Retain the completed evidence and finish cleanup.

The sequence advances through ready stages in one call. Each stage has a durable operation ID before execution. On restart, running/waiting stages call their recovery adapter with that ID; completed stages are not executed again. Failed, missing-input and uncertain-provider states remain stopped until the exact state is reviewed and recovery requested. A recovery request is not permission to repeat a provider operation.

Completed stage receipts are retained and hashed. Changing a completed receipt or the frozen release inputs stops continuation. Repeated unchanged waiting observations do not create a new state/history file. One file lock serializes workers sharing this run directory. This **does not** replace the existing cross-job processor/device resource reservations, provide replication between independent storage roots, or infer a pass from a receipt's existence: each production adapter must validate the domain-specific evidence.

## Try intake from the source build

Build the console with .NET 10. Keep the following settings in an access-restricted directory outside the driver repository. The setup snapshot may be the encrypted snapshot file produced by the [setup app](SetupApp.md). The tooling manifest should describe the inventoried, pinned tools for this attempt. Record both digests when accepting those inputs; do not automatically bless changed files by recalculating their expected digests.

```json
{
  "schemaVersion": 1,
  "repository": "YOUR_ORGANISATION/YOUR_DRIVER",
  "releaseId": 123456,
  "packageName": "YourManufacturer_YourType_YourModel_IP.pkg",
  "privateRoot": "C:/CI/Private/submission-runs",
  "profileSnapshotPath": "C:/CI/Private/snapshot.setup",
  "profileSnapshotSha256": "REPLACE_WITH_RECORDED_LOWERCASE_SHA256",
  "toolingManifestPath": "C:/CI/Private/tooling-inventory.json",
  "toolingSha256": "REPLACE_WITH_RECORDED_LOWERCASE_SHA256",
  "allowPrerelease": false
}
```

```powershell
CrestronHomeDevTools.Console.exe submission-release-intake --settings C:/CI/Private/intake.json
```

For authenticated access, the caller can supply a token on protected standard input with `--token-stdin true`. Never put the token in a command argument, driver repository or log. The current CLI has no named GitHub-token-store integration; that is a remaining setup integration task. C# callers can supply an authenticated `HttpClient` directly. No GitHub credential is required for public-repository inspection within GitHub's unauthenticated limits.

Exit codes: `0` means the candidate was retained and its checkpoint opened; `4` means waiting for the package/digest; `5` means an unselected release; `2` means an input/access/transfer problem requiring inspection. A successful intake is **not** a completed submission or passed test. Repeat the same settings to resume intake. Changed inputs under the same release ID are rejected rather than silently starting over. API metadata without an asset digest remains waiting; this preview does not support legacy assets lacking that digest.

The run contains `candidate.pkg`, `release.json`, `state.json` and small lock files. Credentials and signature bytes are not copied into it. Protect the root before use. Failed transfers remove their temporary file; the next intake also removes its exactly named abandoned transfer files after a process termination, while holding the exclusive intake lock. Original evidence, unrelated files and uncertain-provider journals are retained.

## Work still required, in implementation order

| Work | Done when |
|---|---|
| Stage validation | Configured real deployment/test/app/endurance/export/review handoffs now passed without intervention after startup. Protected stages have separate synthetic integration coverage. Preserve this distinction when reporting results. |
| Automatic handoff | Validate the installed release watcher with a complete saved driver profile. Discovery/intake/checkout/registration has a live GitHub check; registered jobs are advanced by the background worker. The driver repo needs no submission files or secrets. |
| Setup integration | The form saves equipment roles, test bindings, release selection and named GitHub access alongside existing developer/driver/credential profiles; validates these under the actual service account. |
| Durable continuation | The startup task passed an actual reboot under LocalService with an empty registry. Actual-account credential access and waiting endurance continuation also passed in the configured rehearsal. A restart during that active rehearsal was not exercised; shared resources still use the existing leases. |
| Review experience | One review view presents the complete identified packet, material gaps and exact permitted signature/delivery actions. Saving a signature alone grants no authority. |
| Final validation | A fresh opted-in GitHub release completes the real route using public tools/docs, including cleanup, with no private helper or supervising assistant. Record the executed workflow and provider receipts. |

Use one configured Windows worker and one processor as the first complete route, then verify the already supported separate build/monitor/UI roles. Do not require additional PCs or Linux knowledge. Physical interruptions still need a person or explicitly configured controllable hardware. A shared household device's restrictions still apply when its processor is dedicated to testing.

The controller needs no new NuGet release for every fix during development. Batch changes, test the source build, and publish a coherent version when the ordinary route is ready. A delivered packet means provider-confirmed submission; Crestron decides acceptance.

Saved setup can prepare either a fresh Rehearsal profile or a Submit profile through
the form, CLI or public API; see [profile preparation](SetupApp.md#prepare-a-controller-profile).
Submit preparation requires a submission-purpose snapshot and preserves explicit
protected-stage references. It grants no signing/delivery authority and starts no
worker. Named GitHub access and independent clean-machine/operator validation
remain separate integration work.

Copyright (c) 2026 Neil Colvin. MIT licensed.
