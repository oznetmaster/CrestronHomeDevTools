# Review with declared gaps

**Requires 1.13.0 or later; signing with declared gaps requires 1.14.0 or later.** This workflow assesses explained gaps and prepares an unsigned review. The developer can then authorize the exact form for [signing](FormSigning.md#signing-with-declared-gaps-1140), or explicitly disclose an omitted form or signature through [unsigned request preparation](ReviewRequest.md). Delivery requires separate approval of the resulting request. Complete mode remains the default. Other required documents, including embedded package help, cannot be omitted through these commands.

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

## Reconcile retained work before requesting a signature

A successful form build, matching hashes and a legible PDF prove artifact integrity, not that every completed test has been represented. Before requesting approval to sign, review each unchecked official item against the retained test originals, scoped change-impact decisions and applicability evidence. Record which original assertions support each scope and which measurements are actually missing.

Do not use a generic lack of a current-candidate, same-ID machine row as the reason to discard earlier completed work. Where original native policy and observations exist, evaluate the supported `ReviewedPriorPass` route. Where retained evidence supports an explicit interpretation, use the named `interpretationReview` route and preserve its original outcome and limitations. A changed polling path does not by itself invalidate an unrelated unchanged configuration or navigation assertion; assess the affected behavior for each scope. Conversely, prior evidence cannot establish a new candidate's measured timing or endurance without an explicit, truthful qualification.

When comparing an earlier submission, bind the actual delivered attachment through its delivery receipt. A later rehearsal or superseded unsigned review is not the submitted comparator. Compare evidence treatment and applicable behavior, not just checkbox totals. Keep justified N/A distinct from missing tests, and do not expand the checklist into exhaustive testing of unavailable supported models when the reviewed scope is representative installed hardware.

Keep missing evidence-schema structure distinct from missing functional evidence. Do not fabricate old policies, alter raw outcomes or mark an unperformed test as passed to fill a checkbox. Resolve evidence-mapping omissions before proposing the remaining genuine gaps for human attestation.

## Generate an unsigned form with declared gaps

The source `submission self-test-form declared-gaps` mode uses the same actual candidate validation and independently reviewed pins. Supply the standard [evidence-backed form inputs](FormGeneration.md), plus `--declarations FILE --declarations-sha256 REVIEWED_SHA256`. The mode runs the .NET review assessment itself; it does not trust a supplied assessment report.

It reconciles every policy scope with the complete official-item mapping. An item with any unresolved declared gap remains unchecked. Numbered notes after the checklist group the original outcomes and each developer explanation by official item; links work in both directions. Internal scope IDs and raw measurements remain in private records. Failed, partial, inconclusive, untested and missing observations are distinct. A claimed pass whose measurements or duration are insufficient is described as incomplete verification, not a passed portion. Verified non-applicability remains separately explained and unchecked.

Version 1.17.5 also preserves explanations from verified passing observations in these notes. This keeps the test method and its limits visible even when an item is checked. Repeated identical explanations are printed once per official item; failed or insufficient claims are not promoted into verified explanations.

A named reviewer can record an `interpretationReview` on a declaration when retained evidence supports the applicable requirement under an explicit interpretation. Supply `reviewer`, `rationale`, and `evidence` containing the exact file references already retained by that observation. This is useful when undefined optional behavior is inapplicable or an agreed interpretation differs from the automatic measurement contract. It cannot accept a missing, failed or unperformed test, or waive corrupted evidence or identity errors. Ordinary non-applicability should still use the policy's N/A result.

The assessment records `AcceptedInterpretation`, preserves the original outcome and every automatic finding, and stays in declared-gaps mode. A corresponding checkbox may be checked with a numbered interpretation note; it is not reported as a new automatic pass. Review authorization binds these declarations and the exact form before signing. Neither the reviewer nor the tools decide whether Crestron will accept that interpretation. The `interpretationReview` option requires version 1.16.0 or later of the console and form tools; older releases do not support it.

The output remains an **unsigned review, not a delivery packet**. The report retains `reviewMode`, `verificationStatus`, the declaration digest and original validation JSON. Signature/date fields stay blank and the printed official form is unchanged. Use `submission prepare-review --review-mode declared-gaps --prepare-for-signing` to prepare an unsigned signing copy, then follow [form signing](FormSigning.md) after exact-form approval. It does not add missing required documents or bypass the package validator. Render and inspect every page before progressing to a later review stage.

## Retain a review and its evidence

`SubmissionBundle.CreateReview` and `CheckReview` retain and reassess the archive's own candidate, policy, original observations, referenced evidence and exact declarations. The archive and declaration digests must be retained independently by the trusted coordinator. Unreferenced files are excluded. Original validation failures remain visible under `review.validation`; `readyForReview` never means all tests passed. The strict `Create`/`Check` methods are unchanged and do not accept review archives containing declarations.

Console entry points are `submission-review-bundle-create --help` and `submission-review-bundle-check --help`. Both require an explicit matching mode and independent declaration pin. They use the same bounded archive handling, path checks and no-overwrite behavior as complete-only archives.

To retain the form, bundle and matching receipt together, use the standard [review stage](ReviewStage.md) with these additional arguments:

```text
--review-mode declared-gaps --declarations PRIVATE_FILE --declarations-sha256 REVIEWED_SHA256
```

The command revalidates the candidate and declarations during form generation and archive creation. It retains `declarations.json` beside the private outputs and inside `evidence.zip`. The receipt uses `state: UnsignedReviewWithDeclaredGapsPrepared`, with `reviewMode`, `verificationStatus` and `declarationsSha256`. The final `COMPLETE` marker means that this local preparation operation finished consistently, not that all interpreted Crestron requirements passed. No delivery occurred.

If every Android scope is wholly unperformed and explicitly declared, the review can be prepared without Android runs. Any supplied Android observation, including a failure, still requires the raw-run audit and independent pins. Existing supplied runs cannot be silently dropped. Physical reservations and unresolved restoration are not released by creating a review.

### Interrupted raw runs (unreleased source workflow)

The source `prepare-review` and `freeze-review-inputs` commands can preserve a run interrupted **before all terminal records were produced**. This narrowly scoped route requires `--review-mode declared-gaps`, the existing pinned declarations, and a newly reviewed Android pins document. The installed 1.26.2 tools do not contain this correction; use a reviewed source snapshot containing `interrupted_android.py` and the matching review modules until a release includes it.

Add `interruptedFiles` to the affected run in the independently pinned Android pins document. It is a nonempty array of `{ "relativePath": "context.json", "sha256": "EXACT_SHA256" }` entries covering **every file** beneath that run's original Android evidence root, including producer dependencies, logs and interrupted physical-test records. The example entry is illustrative, not a complete inventory. Keep the pins document outside the evidence root. The trusted coordinator must review the inventory against the retained original collection and its independent receipts before accepting its new digest. This post-interruption preservation inventory never replaces the existing pre-execution candidate, assembly, discovery, producer-manifest or selection pins.

This route requires `completion.json`, `coverage.json` and root TRX results to be genuinely absent. It rejects present terminal files, even malformed or failed ones; do not remove records to make a run eligible. Failed terminal runs and partially written capture triples still need a separate supported audit implementation. Existing complete-mode and standalone `audit-android` checks remain strict. A run without `interruptedFiles` still undergoes the ordinary successful-run audit, even in declared-gaps mode.

The auditor checks the same candidate context, complete producer inventory, discovery and selection identities. It rejects changed, extra, missing, duplicate, escaping or linked files. The complete raw inventory is bounded to 8,192 files, 32 MiB per file and 512 MiB total. Completed capture records retain their original Passed or Failed outcome and must have matching run/candidate identities, ordered timezone-bearing timestamps and unchanged screenshot/hierarchy bytes. There is no fabricated TRX interval. Other supplemental files are retained as opaque original bytes: their content is not interpreted as passing measurements by this preservation audit.

The private report is `InterruptedAndroidEvidencePreserved`, with `completePassingRun: false`, unknown executed count/restoration, no official requirements satisfied and no producer authentication. Every raw file is copied into the portable private input snapshot and rechecked before review publication. The evidence archive still contains only the separately referenced observation evidence; raw directories and their preservation report remain private.

An interrupted run cannot support a Passed Android observation or an accepted interpretation. Applicable Android scopes without an independently audited complete producer binding require explicit gaps; source-supported NotApplicable observations still pass ordinary policy validation. Complete historical evidence may support a named interpretation through the scoped binding below. Retain prior candidates and their measured outcomes separately. A later independent restoration receipt cannot establish the interrupted run's missing timed recovery or completion. Physical reservations, signing and delivery keep their existing separate guards.

The reusable private CI template accepts `review_mode` and `declarations_sha256`; the worker's private `CRESTRON_SUBMISSION_DECLARATIONS` variable locates the document. Complete mode remains the default. Packaged-console preparation and the template's delivery preflight have automated checks; each developer must verify their protected worker configuration before supplying real delivery credentials.

## Scoped complete historical Android evidence (unreleased source workflow)

The independently pinned Android pins document may include `observationBindings`, an array of `{ requirementId, runId, files }`. Each `files` entry contains an evidence-root-relative `relativePath` and exact `sha256`. Every entry must also belong to that requirement's observation. Bindings require a complete audited producer and original context, coverage, completion, TRX and at least one audited observation/hierarchy/screenshot triple. They cannot point at an interrupted run, unknown scope or unknown producer. Every supplied run still undergoes its own audit; adding a complete run does not hide incomplete evidence.

For an earlier candidate, add `historicalReview` to that run's independent pins. This is available only in `declared-gaps` mode, never with `interruptedFiles` on the same run. It has two objects:

- `identity`: `packageSha256`, `sourceSha256`, `releaseSourceCommit`, `driverVersion`, `driverId`, `installedDriverId`, copied exactly from the original producer. A literal null `releaseSourceCommit` stays null. The driver ID must match the target driver and the historical version must have revision zero.
- `sourceReview`: named `reviewer`, timezone-bearing `reviewedUtc`, specific `rationale`, independently reviewed `sourceCommit`, `package` and `files`. The last two use `{ relativePath, sha256 }` references beneath the ordinary evidence root. `files` must include the exact historical package bytes and independent retained source/build attestation. If the original raw commit exists, the review cannot contradict it. The reviewer must actually reconcile the source/build attestation; hashing a document does not prove its contents.

There is no fabricated historical submission policy or replacement raw candidate declaration. The audit uses the original raw identity for all context, coverage, selection and capture comparisons, including nulls. Original producer inventory, discovery, selected cases, successful TRX, capture timestamps/hashes and confirmed restoration remain mandatory. The report is `HistoricalAndroidEvidenceAudited`; its original `identity` and target `targetIdentity` are separate, and `releaseSourceAuthenticated`, `producerAuthenticated` and `submissionReady` remain false.

Every historical run needs at least one explicit `observationBindings` entry and an `interpretationReview` for that same requirement. Its authored observation must remain `Inconclusive` or `Partial`; this route cannot produce a new `Passed` or `ReviewedPriorPass` observation. The interpretation must reference all bound producer files and the independent source-review files. Explain the exact original assertion, changes since execution, why the scope still applies, and what was not measured. A complete run is not blanket evidence for other checklist items. Existing native `ReviewedPriorPass` remains the preferred route when the original native policy and observations exist.

The portable snapshot retains the source-review files as well as every audited original producer file, and re-audits them at preparation. All current candidate, policy, mapping, declaration, form, signature and delivery checks remain in place. These source modules are not included in installed 1.26.2 until a release is published.

## Delivery and current limits

The [unsigned request preparation command](ReviewRequest.md) creates a separate outbound copy for an explicitly omitted signature or official form, from a revalidated retained review. It requires its own pinned document disposition and another visual review. It does not authorize delivery.

Missing required documents or signatures need explicit disclosure; no signature may imply work that was not done. The unsigned request stage handles form/signature omissions. Other required documents, including package help, still use the existing file assessment/package validation rules and cannot yet be omitted.

The [review delivery API](ReviewDelivery.md) binds mode, gap declarations, document omissions, attachment disposition and actual generated correspondence to approval. Its separate receipt preserves verification status alongside provider delivery state. Signed forms, unsigned forms and disclosure-only attachments have distinct wording. It shares the existing journal and SMTP provider; offline tests include a synthetic journal-plus-mailer rehearsal without external transmission.

Use the [approval and unsigned-request delivery guide](ReviewApproval.md) to continue through C# or the protected console. Integration tests exercise generated requests through approval, fresh evidence checks and simulated delivery. The existing complete-only signing and delivery-preparation commands remain separate; do not relabel an unsigned request as a signed form. The workflow does not bypass reservations or resolve uncertain previous send outcomes.

Keep three separate facts throughout: verification against our interpreted requirements, actual delivery state, and any actual decision communicated by Crestron. Successful tests, signed forms, upload receipts, email delivery and silence cannot establish that last fact.

## Scoped static documentary grouping (unreleased source workflow)

A guarded documentary capture may establish static grouping without having been an NUnit acceptance test. In `declared-gaps` mode only, independent Android pins may include `documentaryBindings`. The grouping schema below applies only to `extension.controls.08.all-declared-button-groups`; the separate static-gauge section defines two additional narrow scopes. Documentary review does not reclassify policy as configuration, create a producer run, or establish device-driven gauge changes, actions, latency, recovery or endurance. Ordinary complete/interrupted run inventories and their audits remain mandatory and unchanged.

Each entry has these exact fields:

- `requirementId`: the static grouping scope above.
- `identity`: original `deviceId`, four-part reported `version`, 32-character owner ID, timezone-bearing `completedUtc`, `installedPackageSha256: null`, `testPassClaimed: false` and `candidateAccepted: false`. Null remains null: source equivalence does not authenticate the original installed package.
- `captureResult`: an evidence-root-relative `{ relativePath, sha256 }` reference to the original public factual-help result. Its purpose must be `Factual help figures only`, identity and false claim flags must match, and original state, selected identity and released reservations must be confirmed with no Home restoration pending.
- `captureArchive` and `archivePrefix`: an exact pinned original ZIP plus its single safe directory prefix (for example, `documentary/`). The archived `result.json` must be byte-identical to `captureResult`. Every original screenshot/hierarchy entry is checked against its recorded hash and length; duplicate members and excessive sizes are rejected.
- `restoration`: pinned `screenshot` and `hierarchy` references to the exact original `restore-original-home.png` and `restore-original-home.masked.xml`, also present in the original inventory/archive.
- `sourceReview`: named `reviewer`, timezone-bearing `reviewedUtc` after capture, specific `rationale`, original/current 40-character source commits, and pinned `originalUiDefinition`, `currentUiDefinition` and `attestation`. The current commit must match the candidate; current XML bytes must match its pinned package's single UI definition. The independent attestation identifies both source commits. The named reviewer must review its contents and the actual images, not merely their hashes.
- `groups`: every current declared button group, with exact `pageId`, one-based `groupIndex`, ordered `controlIds`, pinned `screenshot`/`hierarchy`, `appPackage`, `resourceId` and one exact Android `controlBounds` string per button. Every original/current page subtree and control enumeration must agree. Captured controls must uniquely match the same application and non-password ancestor path, share one container, and form an ordered adjacent row within the PNG dimensions. The reviewer maps the captured controls to their declared IDs and verifies the visual grouping; these structural checks do not replace visual review.

The authored observation remains `Partial` or `Inconclusive`. A named `interpretationReview` with the same reviewer and a specific static-scope rationale must retain *every* binding reference, as must the observation. Neither `Passed` nor `ReviewedPriorPass` is allowed. The audit reports `StaticDocumentaryGroupingReviewed`, keeps original reported identity separate from source review, and explicitly leaves producer/installed-package authentication and test-pass claims false. It does not report official acceptance or complete supported-driver functionality.

Portable preparation retains and rechecks the complete original archive, selected captures, Home restoration and source-review bytes. An interrupted run still cannot support any other interpretation or passing observation. Do not manufacture NUnit context, coverage, completion or TRX for documentary work; do not attach it to an unrelated complete run. Checkbox interpretation, artifact review, signing and delivery retain their separate guards. Installed 1.26.2 does not provide this source-only contract until a release is published.


Native wrapped grouping captures retain the existing direct-sibling path. The
alternative accepted shape is exactly the application's
`customdevice_buttonGroup_container`, whose ordered children are one unary
wrapper per selected `customdevice_button_root`. Every wrapper and ancestor
must be in the same application and non-password. Unknown containers, extra
children, multiple roots in a wrapper, omitted/reordered controls and arbitrary
common-ancestor matching are refused. This is a static documentary layout
check; it adds no executed-test, timing or installed-package authentication.
The portable regression models the retained Energy/Impact hierarchy structure
without embedding private capture content.


## Scoped static gauge labels and declared percentage scale (unreleased source workflow)

Two further documentary scopes are supported in `declared-gaps` mode: `extension.controls.06.every-gauge-labels` and `extension.controls.06.device-min-max`. The latter is a **declared percentage-scale interpretation**, not a claim that the physical device was driven to its minimum and maximum. `extension.controls.06.device-change-immediate-gauge` remains excluded. There is no dynamic state-change, response-time, recovery, endurance or new test-pass claim. Original complete/interrupted Android audits remain unchanged.

Use a separate `documentaryBindings` entry per scope. It retains the same exact `identity`, original `captureResult` and bounded `captureArchive`, archive inventory, `archivePrefix`, original Home `restoration`, packaged current XML and original/current source identity guards as grouping. Replace `groups` with `gauges`, enumerating **every radial gauge in the current UI**, not merely the selected page or one convenient gauge. Each named layout and gauge ID must occur once and its complete original/current page subtree must be equivalent.

The gauge `sourceReview` adds pinned `originalTranslations`, `currentTranslations`, `originalPropertyDefinitions`, `currentPropertyDefinitions` and an exact `translationPackagePath`, such as `translations/en-US.json`. Current translation bytes must equal that uniquely named bounded member of the pinned candidate package, and both original/current pinned translation files must parse as strictly equivalent JSON dictionaries with duplicate keys rejected. Original file bytes and hashes are retained independently; harmless JSON whitespace or LF/CRLF differences do not rewrite or repin them. Current bytes must still exactly match the package member. The original/current property source must each contain exactly one uncommented numeric `EntityProperty` declaration for the gauge's bound property, with `RangeMinimum = 0`, `RangeMaximum = 100` and `Units = "Percent"`. This intentionally supports only static declared percentage scales; it is not a general parser for arbitrary device ranges, computed limits or C# behavior.

A **new gauge-specific named review** is required. In addition to both source commits, its pinned `attestation` must identify the exact `requirementId`, `reviewer`, `reviewedUtc`, and ordered `gaugeIds` as `[pageId, controlId]` pairs for every declared gauge. These must match the entry's actual source reviewer/time and current XML enumeration. An earlier grouping attestation is insufficient. The named reviewer must examine the original images, label-to-source mapping and percentage semantics, and explicitly record any original installed-package authentication limit.

Each `gauges` entry contains `pageId`, `controlId`, `propertyId`, pinned `screenshot`/`hierarchy`, `appPackage`, `containerBounds`, `gaugeBounds`, four `labelBounds`, four `labelText`, and `scale: { "minimum": 0, "maximum": 100, "units": "Percent" }`. The UI's `radialvalue` must bind that property, and title/three line labels must be defined. The original native title must equal the literal or exact packaged translation. Line values are documentary text, not independent API telemetry or proof of changes.

The accepted native shape is one exact `android.view.ViewGroup` with an empty resource ID and exactly five direct leaf children, ordered `customdevice_radialgauge_title`, `customdevice_radialgauge_gauge`, `customdevice_radialgauge_label_one`, `customdevice_radialgauge_label_two`, `customdevice_radialgauge_label_three`. Children must match the entry's original bounds/text; their bounds must lie within the container and PNG. Every ancestor/descendant must be in the same application and non-password. The captured page must contain the declared number of native gauges; a selected container cannot serve two gauge declarations. Unknown containers, wrappers, omitted/extra/reordered children and arbitrary common-ancestor matching are refused. The portable regression fixture models this structure without embedding private captured content.

Reports are `StaticDocumentaryGaugeLabelsReviewed` or `StaticDocumentaryGaugeScaleReviewed`, with `dynamicDeviceChangeObserved: false`, `physicalExtremesObserved: false` and `staticDeclaredScaleOnly: true`. Raw observations remain `Partial` or `Inconclusive`, original `installedPackageSha256: null`, `testPassClaimed: false` and `candidateAccepted: false` remain unchanged, and producer/installed-package authentication remain false. The observation and its same-reviewer named interpretation must retain every exact original/source reference. Neither a raw `Passed` nor `ReviewedPriorPass` is permitted.

Portable freeze rechecks and copies these files alongside ordinary raw Android runs. This source-only contract is not present in installed 1.26.2. A scoped interpretation, a checkbox, a prepared artifact, a signature, delivery and actual Crestron acceptance remain different facts.


## Scoped firsthand history observations

The maintained source-only declared-gaps workflow accepts `humanObservationBindings`
in independent Android pins, separately from all complete/interrupted machine runs.
Only `extension.controls.06.device-change-immediate-gauge` and
`extension.controls.18.repeat-all-applicable-controls-on-every-subpage` are eligible.
This retains positive firsthand history observations as a named, qualified technical
interpretation. It does not authenticate an Android test, claim timed device change,
or establish every control or an unspoken repeated-button sequence. Ordinary Android
producer auditing remains unchanged; interrupted runs stay interrupted.

Each binding contains `requirementId`, `provenance` (an evidence path/SHA-256),
`statementIndex`, the exact `quote` and `reportedBehavior`, `reportedIdentity`
(`deviceId`, `version`, `packageSha256`), all original `limitations`,
`corroboratingScopes`, and `sourceReview`. The pinned provenance records `source`,
`recordedUtc` and `statements`; the selected statement records `quote`,
`reportedObservation`, `scopeLimits` and optionally the exact `reportedIdentity`.
If the human did not identify an instance/version/package, preserve nulls. Unknown
identity qualifies the observation; it is not grounds to erase firsthand evidence or
infer identity from a separate screenshot. A viewed period such as yesterday is not
an invented observation timestamp.

Gauge-history corroboration requires independently audited documentary gauge-label
and declared-scale scopes; subpage corroboration requires the audited button-group
scope. These existing audits preserve original archive, captures, restoration and
current package/layout/source checks. Their identity remains separate from the human
observation. `sourceReview` records reviewer, actual reviewed UTC, rationale, current
source commit, `originalHistorySource` and `currentHistorySource` path/hash references,
bounded evidence `files`, and pinned `attestation`. The attestation
must exactly match the binding's eight observation fields and the reviewer's four
review fields plus both history-source references. Review files include provenance,
attestation and both nonempty source files. The raw observation
must remain Partial or Inconclusive; the declaration must have a named interpretation
retaining every exact human/source/corroboration reference. All references are frozen
and re-audited through the ordinary portable review workflow. Complete mode, unknown
scopes, rewritten statements, omitted limits, inferred identity and raw Passed or
ReviewedPriorPass claims are rejected. This route is not yet in the published tools.

## System 04 applicable removal view

Preserve the official wording: after deleting the driver, it should no longer be
listed in the room **or** seen in the end-user UIs; the err-log obligation is separate.
Evaluate the applicable view. A driver with no room UI does not acquire a room-view
requirement. The legacy `delete-instance-room-and-ui-absence` identifier is retained
for compatibility, not as an additional AND requirement. The checklist does not
require deleting a particular retained production/test instance. Identify the actual
removed instance and its driver/model/version; credit genuine prior presence and
final API absence without pretending API absence proves end-user UI absence or an
err-log scan. Conditional cleanup source that writes a removal receipt even when the
instance was already absent must not be described as proof the removal command ran.
Restoration policies protect unaffected state; they do not require recreating an
intentionally deleted test instance or deleting a retained candidate. Keep unobserved
applicable end-user UI and error-log facts as explicit residuals.
