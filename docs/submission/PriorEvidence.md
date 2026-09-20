# Reviewing evidence from an earlier candidate

Use this optional review path when a driver changes after testing and a reviewer determines that particular earlier tests remain relevant. It preserves the earlier package, source commit, timestamps, measurements and failures. The resulting outcome is `ReviewedPriorPass`, not a new execution on the current candidate.

Only Crestron decides whether the submitted evidence is acceptable. A successful import or checked form does not imply acceptance or certification. This API checks the recorded review and evidence; it cannot establish that a behavioral-equivalence judgment is correct.

Keep driver-specific decisions, original evidence, forms and orchestration in private storage. Public driver repositories do not need to contain submission files or build hooks. Ordinary tests and generic tooling remain independent of submission.

## When this is appropriate

Review the actual source, resources, dependencies and runtime changes. Identify the precise assertions they could affect. Changes to shared polling, startup, cancellation or command follow-up paths may affect tests whose immediate handler did not change. Run affected checks as needed and retain their actual results separately.

This mechanism requires an originally passing, fully validated assertion with exactly the same ID, target, method, duration, timing and restoration requirements. It cannot promote failed, partial, inconclusive or unperformed work. It cannot chain an earlier review into another review. Other outcomes in the original run remain retained, including failures unrelated to the selected assertions.

## Prepare and import

1. Retain the original policy, observation document and complete referenced evidence beneath the current private evidence directory. Preserve their bytes and record their SHA-256 digests.
2. Prepare a `SubmissionChangeImpactReview` with the original identity, current package SHA-256 and source commit, reviewer, date, and explicit decisions for individual requirement IDs. Each decision needs a rationale and retained evidence references supporting the dependency review.
3. Add `SubmissionPriorEvidenceRequirements` only to the corresponding requirements in the current policy. It pins the original identity, policy, observations, retained evidence directory and change review. The trusted coordinator must approve these decisions before pinning the updated policy and candidate declaration. The tool does not supply that approval.
4. Import the reviewed observations, then compose them with the current observations using the [composition API](EvidenceComposition.md). Do not include a second observation for an imported requirement or silently replace an actual failure on the current candidate with an older pass.
5. Run the ordinary [review stage](ReviewStage.md) against the full policy, package and composed evidence. Missing or incomplete assertions still require testing or explicit [gap declarations](DeclaredGaps.md).

The console command uses private paths and never overwrites an existing output:

```text
CrestronHomeDevTools.Console.exe submission-import-prior-evidence --candidate PRIVATE_CANDIDATE_JSON --candidate-sha256 REVIEWED_SHA256 --policy PRIVATE_POLICY_JSON --evidence PRIVATE_EVIDENCE_DIRECTORY --output NEW_PRIVATE_OBSERVATIONS_JSON
```

The equivalent C# entry point is:

```csharp
SubmissionEvidenceDocument imported = SubmissionPriorEvidence.ImportFiles(
    candidatePath, approvedCandidateSha256, policyPath, evidenceDirectory,
    DateTimeOffset.UtcNow, cancellationToken);
```

`Import` is also available for callers already holding the independently reviewed identity and policy. Neither entry point performs hardware tests, applies a signature or delivers anything. Import validates only the selected subset; it does not establish full submission readiness.

## How the results appear

The assessment uses `VerifiedPriorEvidence` for a valid review. Original physical measurements remain in the retained source document; the imported observation records the review date without invented execution measurements. The form companion identifies prior evidence separately from fresh passes. A checkbox is checked only when every mapped assertion has sufficient validated support; mixed incomplete items remain unchecked with their explanations.

Portable bundles retain and revalidate the original evidence and the change review, so validation does not depend on the original working directory still existing. Changed hashes, missing provenance or an altered original scope are errors that cannot be waived as ordinary verification gaps.

The existing `SubmissionEvidenceMapping` API remains restricted to the same candidate. It does not transfer executions between package versions; use this explicitly reviewed path instead.
