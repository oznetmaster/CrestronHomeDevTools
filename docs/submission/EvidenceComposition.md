# Combine observations for final review

**Source implementation, not included in the released 1.12.0 packages.** The C# API is `SubmissionEvidenceComposition.CombineFiles`; the console command is `submission-combine-evidence`. Both run offline. They do not need Python commands, processor credentials or an Android connection.

Different phases can produce separate observation documents: configuration acceptance, controls, recovery and the completed endurance handoff. Composition collects those already scoped observations into the single document consumed by [review preparation](ReviewStage.md). It does not interpret NUnit test names or screenshots, generate missing assertions, combine partial assertions into a pass, change a candidate identity or authenticate a producer.

## Prepare the source inventory

The trusted coordinator must retain and review every relevant phase and its outcome. Each source must already be a `SubmissionEvidenceDocument` using the exact final candidate, source commit, policy and official template identity. Use [reviewed evidence mapping](EvidenceMapping.md) first when a legitimate original producer uses a different policy; composition does not rewrite identities. Keep original evidence files at their existing paths under one private evidence root.

Create a private plan with a complete source inventory. These are illustrative names and placeholder digests:

```json
{
  "schemaVersion": 1,
  "identity": {
    "packageSha256": "PACKAGE_SHA256",
    "sourceCommit": "FULL_RELEASE_COMMIT",
    "policySha256": "REVIEWED_POLICY_SHA256",
    "templateSha256": "OFFICIAL_FORM_SHA256"
  },
  "sources": [
    { "relativePath": "controls/observations.json", "sha256": "TRUSTED_CONTROL_OBSERVATIONS_SHA256" },
    { "relativePath": "endurance/mapped-observations.json", "sha256": "TRUSTED_ENDURANCE_OBSERVATIONS_SHA256" }
  ]
}
```

Pin that plan's exact digest through the trusted coordinator. A source hash supplied by an untrusted worker alongside its own file is not independent authentication. The command cannot discover an omitted failed phase; the reviewed inventory and protected retention must account for all required execution. Never build the inventory by filtering for successful tests.

Every source must have a unique safe relative path, a matching digest and a nonempty schema 1 observation document. Inputs are limited to 16 MiB each, 64 MiB altogether and 1,024 source documents. JSON inputs are held open during evaluation. Traversal and links are refused by the evidence path checks. The plan and policy are also pinned and retained as provenance.

## Run the checked composition

```text
CrestronHomeDevTools.Console.exe submission-combine-evidence --evidence PRIVATE_EVIDENCE_ROOT --plan composition.json --plan-sha256 TRUSTED_PLAN_SHA256 --policy policy.json --output NEW_PRIVATE_RESULT_DIRECTORY
```

Paths other than the evidence root and output directory are relative to the evidence root. The output must be a new directory with an existing private parent. Do not publish raw evidence or reports as public release assets.

- Exit **0** writes `report.json` and `observations.json`. The complete supplied policy passed structural evidence validation.
- Exit **1** writes the incomplete `report.json`, preserving observations and problems for review, but does **not** write a consumable `observations.json`.
- Exit **2** indicates an invalid input or I/O failure. Stop; any partial output is not a completed result.

Duplicate requirement claims are retained and rejected, whether both pass or one fails. Composition never chooses the newest or successful result. Failed, Partial, Inconclusive and NotTested remain blocking; missing scopes stay missing. Required response measurements, restoration, continuous samples, retained file digests and original identities are evaluated through the existing full evidence validator. Provenance files cannot repair an originally missing measurement file.

From C#:

```csharp
var report = SubmissionEvidenceComposition.CombineFiles(
    evidenceRoot, "composition.json", trustedPlanSha256,
    "policy.json", DateTimeOffset.UtcNow, cancellationToken);

if (!report.CompositionChecksPassed)
    throw new InvalidOperationException("Submission evidence remains incomplete.");

SubmissionEvidenceDocument combined = report.Observations;
```

The method reads files and returns typed results; it does not write them. The CLI performs the checked output step. Existing fixture code uses `SubmissionObservation` to report its measured assertions; this method only composes those records. No global test-count threshold is involved.

## Continue to the submission review

Retain the successful observations and all referenced original files without relocating relative paths. Pass the document to the normal package/evidence validation and unsigned review stage. Full candidate/package validation, approved policy completeness, authenticated producers, Android audits, behavioral assertion review and visual form review remain mandatory. A successful composition is neither certification nor signing or delivery authorization. Ordinary driver, library and client releases remain independent of this optional submission stage.
