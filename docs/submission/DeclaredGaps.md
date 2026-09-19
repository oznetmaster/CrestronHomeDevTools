# Review with declared gaps

**Source availability:** the assessment APIs, unsigned form mode and private review/bundle integration described here are source additions after 1.12.0. They are not in the released 1.12.0 packages. Signing and delivery integration for requests with gaps is still being implemented. The existing complete-only behavior remains the default.

Complete means complete against **our interpretation of Crestron's published submission requirements**, captured in the full reviewed verification plan. It does not mean finishing an arbitrarily reduced checklist. No result from this workflow implies or predicts acceptance, publication or certification. Crestron makes those decisions.

A developer may be unable or unwilling to complete a requirement. Declared-gaps mode records that choice and its reason without treating the requirement as passed or not applicable. It checks the entire policy, including requirements with no observations. Every unmet scope needs an explicit explanation; a declaration list cannot hide additional failures or omissions.

## Assess a candidate through C# or the console

`SubmissionReviewAssessment.Assess` assesses typed scoped observations against the full reviewed policy. `SubmissionReviewFiles.Check` additionally validates the actual package and binds the declaration document to the pinned candidate, policy, official template and source commit. Both preserve the original evidence outcome and validation issues.

The independently reviewed declaration document has this structure. All digests and IDs below are placeholders; use the exact candidate identity and policy scope IDs:

```json
{
  "schemaVersion": 1,
  "identity": {
    "packageSha256": "CANDIDATE_PACKAGE_SHA256",
    "sourceCommit": "FULL_RELEASE_SOURCE_COMMIT",
    "policySha256": "COMPLETE_REVIEWED_POLICY_SHA256",
    "templateSha256": "OFFICIAL_TEMPLATE_SHA256"
  },
  "mode": "DeclaredGaps",
  "declarations": [
    {
      "requirementId": "exact.scoped.requirement.id",
      "reason": "The required equipment is unavailable; this check was not performed."
    }
  ]
}
```

For complete mode, use `"mode": "Complete"` and an empty declarations array. The console requires an explicit matching mode. It never silently switches a complete review to declared gaps. A declaration for a now-verified scope must be reconciled rather than silently dropped.

```powershell
.\CrestronHomeDevTools.Console.exe submission-assess-review `
  --candidate C:\CI\Private\candidate.json --candidate-sha256 REVIEWED_CANDIDATE_SHA256 `
  --package C:\CI\Private\ExampleDeveloper_Platform_Example_IP.pkg `
  --policy C:\CI\Private\policy.json --template C:\CI\Private\official-form.pdf `
  --observations C:\CI\Private\observations.json --evidence C:\CI\Private\evidence `
  --mode declared-gaps --declarations C:\CI\Private\declarations.json `
  --declarations-sha256 REVIEWED_DECLARATIONS_SHA256
```

Keep the JSON output private. It contains scoped results and developer explanations and is not an outbound submission document. Producer authentication and reconciliation of the full policy with the official requirement inventory remain required. A valid digest alone does not establish trustworthy or sufficient evidence.

## Interpret the result

| Field or status | Meaning |
|---|---|
| `CompleteAgainstInterpretedRequirements` | Every requirement in the reviewed full policy passes its evidence checks, including justified non-applicability where allowed. |
| `GapsDeclared` | All unmet requirements have explicit reasons; the original validation still fails. |
| `NeedsCorrection` | At least one undeclared gap, invalid evidence record or other blocking problem remains. |
| `validation.validationChecksPassed` | Original package/evidence validation, unchanged by the declarations. |
| `readyForReview` | Eligible for further internal review only. It does not authorize signing or delivery. |

Exit 0 means eligible for further review. It **does not mean that all tests passed**; inspect the verification status and original validation. Exit 1 means correction is required. Exit 2 means invalid or unreadable input. A caller must never treat this command's success as completion of the form, signature, delivery or vendor decision stages.

Each row retains its original `observedOutcome`: Failed, Partial, Inconclusive and NotTested remain distinct. No observation is represented by null. A claimed Passed observation without retained measurements can still be a declared gap; only `VerifiedAgainstPlan` supports a verified scope. Valid non-applicability remains distinct from an omitted test and requires policy permission and a rationale.

Corrupt evidence, mixed candidate identities, unknown scopes/outcomes, duplicate observations and invalid timestamps cannot be waived. Correct the review packet while retaining the original failure for diagnosis; do not relabel bad evidence as a pass. Declaration changes invalidate their independent digest. Later signing or sending must bind the exact reviewed packet, mode, gaps and correspondence, not merely this assessment result.

## Generate an unsigned form with declared gaps

The source `submission self-test-form declared-gaps` mode uses the same actual candidate validation and independently reviewed pins. Supply the standard [evidence-backed form inputs](FormGeneration.md), plus `--declarations FILE --declarations-sha256 REVIEWED_SHA256`. The mode runs the .NET review assessment itself; it does not trust a supplied assessment report.

It reconciles every policy scope with the complete official-item mapping. An item with any declared gap remains unchecked. The companion groups the original outcomes and each developer explanation by official item; internal scope IDs and raw measurements remain in private records. Failed, partial, inconclusive, untested and missing observations are distinct. A claimed pass whose measurements or duration are insufficient is described as incomplete verification, not a passed portion. Verified non-applicability remains separately explained and unchecked.

The output remains an **unsigned review, not a delivery packet**. The report retains `reviewMode`, `verificationStatus`, the declaration digest and original validation JSON. Signature/date fields stay blank and the printed official form is unchanged. This mode cannot yet produce a signing copy. It does not add missing required documents or bypass the package validator. Render and inspect every page before progressing to a later review stage.

## Retain a review and its evidence

`SubmissionBundle.CreateReview` and `CheckReview` retain and reassess the archive's own candidate, policy, original observations, referenced evidence and exact declarations. The archive and declaration digests must be retained independently by the trusted coordinator. Unreferenced files are excluded. Original validation failures remain visible under `review.validation`; `readyForReview` never means all tests passed. The strict `Create`/`Check` methods are unchanged and do not accept review archives containing declarations.

Console entry points are `submission-review-bundle-create --help` and `submission-review-bundle-check --help`. Both require an explicit matching mode and independent declaration pin. They use the same bounded archive handling, path checks and no-overwrite behavior as complete-only archives.

To retain the form, bundle and matching receipt together, use the standard [review stage](ReviewStage.md) with these additional arguments:

```text
--review-mode declared-gaps --declarations PRIVATE_FILE --declarations-sha256 REVIEWED_SHA256
```

The command revalidates the candidate and declarations during form generation and archive creation. It retains `declarations.json` beside the private outputs and inside `evidence.zip`. The receipt uses `state: UnsignedReviewWithDeclaredGapsPrepared`, with `reviewMode`, `verificationStatus` and `declarationsSha256`. The final `COMPLETE` marker means that this local preparation operation finished consistently, not that all interpreted Crestron requirements passed. No delivery occurred.

If every Android scope is wholly unperformed and explicitly declared, the review can be prepared without Android runs. Any supplied Android observation, including a failure, still requires the raw-run audit and independent pins. Existing supplied runs cannot be silently dropped. Physical reservations and unresolved restoration are not released by creating a review.

The reusable private CI template accepts `review_mode` and `declarations_sha256`; the worker's private `CRESTRON_SUBMISSION_DECLARATIONS` variable locates the document. Complete mode remains the default. This template extension is source-only and has not yet been validated as a packaged-console service run.

## Remaining integration

Missing required documents or signatures need explicit disclosure; no signature may imply work that was not done. These document omissions are not yet accepted by the current file assessment/package validation path.

The new source [review delivery API](ReviewDelivery.md) binds mode, gap declarations, document omissions, attachment disposition and actual generated correspondence to approval. Its separate receipt preserves verification status alongside provider delivery state. Signed forms, unsigned forms and disclosure-only attachments have distinct wording. It shares the existing journal and SMTP provider; offline tests include a synthetic journal-plus-mailer rehearsal without external transmission.

The signing and delivery preparation commands still need this integration, complete document-omission handling and synthetic end-to-end acceptance from evidence to delivery. Until that integration is available, this command is an assessment component, not an end-to-end incomplete-submission command. It does not bypass reservations or resolve uncertain previous send outcomes.

Keep three separate facts throughout: verification against our interpreted requirements, actual delivery state, and any actual decision communicated by Crestron. Successful tests, signed forms, upload receipts, email delivery and silence cannot establish that last fact.
