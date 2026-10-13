# Generating the official self-test review form

Command examples use the [bundled submission console](ConsoleTools.md); see that guide for source/release availability and setup.

The `submission self-test-form` command generates an unsigned, interactive review PDF. It preserves the official form's printed pages, adds numbered notes after the checklist, and can populate checkboxes from evidence checked by the existing DevTools offline validator. It does not apply a signature/date, upload, email, authorize delivery or claim Crestron acceptance. Use the matching bundled console; the document runtime is not a dependency of the NuGet configuration library.

The [submission plan](../CrestronSubmission.md) remains authoritative for the work still required. A generated form is one build artifact, not a replacement for the complete driver-specific policy, trusted evidence producer, visual review and signing authorization.

Version 1.13.0 also supports the [declared-gaps unsigned form mode](DeclaredGaps.md). It preserves incomplete official items as unchecked, with grouped explanations. The separate [unsigned request workflow](ReviewRequest.md) prepares an outbound copy for independent approval and delivery; it does not apply a signature.

Version 1.16.1 prints the supplied title and developer name in the first page's upper margin. Include the driver name and submitted version in `--title` (or the review settings' `title`), and the developer name in `--author`. The official printed text, form fields and checkbox meanings are unchanged. A changed title produces a new document hash and requires approval of that exact form before signing.

## Inputs and installation

Use the complete console described in [Console tools](ConsoleTools.md). No Python installation or Word application is needed. The bundled PDF libraries generate the companion pages and preserve the interactive official form. Run `CrestronHomeDevTools.Console.exe submission runtime-check` before preparation.

Download the applicable original form from Crestron's published source. Do not commit the proprietary template or a signed form to this repository. The [Extension inventory](extension-inventory.json) and [Video Server inventory](video-server-inventory.json) contain the inspected template hashes, page/field mappings and known minimum observation durations. Pin the reviewed inventory's own SHA-256 separately in the trusted workflow. A template or field-layout revision requires inspection and an explicit inventory update.

## Draft without attestations

```text
CrestronHomeDevTools.Console.exe submission self-test-form draft --template OFFICIAL.pdf --inventory INVENTORY.json --inventory-sha256 PINNED_INVENTORY_SHA256 --output Driver-Self-Test.review.pdf --title "Example Driver 1.2.3 - Crestron Home driver" --author "Developer name" --report draft-report.json
```

This produces the original form followed by numbered review notes. Every item is marked not evaluated and all checkboxes remain off. Signature/date fields stay blank. Candidate/evidence arguments are rejected in this mode so an unevaluated draft cannot be mistaken for an evidence-backed result. Use fresh output/report paths; an existing artifact is never overwritten.

Source-preview note rendering now describes an unevaluated item explicitly without
claiming a supporting test result. Empty explanations for checked items refer only
to the supplied observation mapping; reviewer interpretations remain distinguished
from new automatic passes, and qualified items remain unchecked. Nonempty supplied
explanations and the existing N/A checkbox behavior are preserved. A note never
adds evidence or broadens its scope. Previously generated drafts are unchanged;
regenerate with fresh output paths and review the new exact bytes before later
signing or disclosure. This wording repair is not yet in a published console.

The sample driver draft was generated with the actual pinned Extension form. All seven pages were rendered and inspected. Its matrix contains no passing claims, and all original printed page streams, field identities and blank signature/date values were verified. This is not a test result for a sample driver submission candidate.

## Evidence-backed unsigned review

First prepare the candidate, approved policy and retained observations described in [EvidenceCli.md](EvidenceCli.md). Add a separately reviewed form mapping:

The bundled [coverage blueprint generator](CoveragePlanning.md) can prepare the draft policy and complete mapping from explicit driver-specific scopes. It leaves every producer unbound and generates no observations; complete review and execution remain necessary before using those files here.

```json
{
  "schemaVersion": 1,
  "inventorySha256": "PINNED_INVENTORY_SHA256",
  "policySha256": "APPROVED_POLICY_SHA256",
  "requirements": [
    {
      "id": "extension.configuration.01",
      "observationIds": [
        "catalogue.device-type",
        "catalogue.manufacturer",
        "catalogue.supported-models"
      ]
    }
  ]
}
```

This illustrative subset cannot pass: a real mapping must cover every official inventory item and every policy observation. Each checkbox needs a nonempty list of distinct policy requirement IDs; an observation ID cannot be reused for another checkbox. Review the mapping against all applicable subconditions, controls, pages and instances. Merely assigning one arbitrary test to each checkbox does not establish complete coverage. The tool checks mapping consistency; the trusted workflow must establish who approved that mapping and whether it measures the full requirement.

Retain the mapping's SHA-256 in the trusted workflow separately from hardware-worker output, then run:

```text
CrestronHomeDevTools.Console.exe submission self-test-form from-evidence --template OFFICIAL.pdf --inventory INVENTORY.json --inventory-sha256 PINNED_INVENTORY_SHA256 --mapping form-mapping.json --mapping-sha256 APPROVED_MAPPING_SHA256 --candidate candidate.json --candidate-sha256 TRUSTED_CANDIDATE_SHA256 --policy policy.json --observations observations.json --package Driver.pkg --evidence EVIDENCE_DIRECTORY --output Driver-Self-Test.review.pdf --title "Example Driver 1.2.3 - Crestron Home driver" --author "Developer name" --report form-report.json
```

Use the same package, official template, policy and observation bytes retained by the candidate workflow. The generator invokes `submission-evidence-check` itself; it does not accept a user-supplied `passed: true` report. A failed validator or changed candidate/observation/policy/template identity stops generation. It then checks the complete mapping and any inventory duration floor. The known 24-hour requirement must map to a policy observation requiring at least 24 hours; adding shorter observations together does not satisfy that floor. Continuous functional observation and the remaining outage subconditions still belong in the approved policy and test producer.

An item is checked when every applicable mapped assertion is supported. Policy-permitted `NotApplicable` subconditions within an applicable item are separately explained. An entirely non-applicable item remains unchecked and labelled N/A, without a numbered note or note link. The retained evidence still records its applicability decision. Notes for the remaining items are numbered consecutively. Failed, partial, inconclusive, missing or duplicate observations cannot yield a complete evidence-backed form; use the explicit declared-gaps path when appropriate.

From 1.17.5, numbered notes also retain nonempty explanations from verified passing observations, once per distinct explanation within each official item. Record how a test was performed and any material scope limits there, for example configuration API and shipped-schema verification rather than a visual dialog inspection. The explanation does not broaden the evidence or change its outcome. A claimed pass that fails verification is still described as incomplete; its unverified explanation is not presented as a verified observation. Review the resulting text for private information before signing or disclosure.

The report records candidate, package, inventory, mapping, policy, observations, validation-report and generated-form hashes. It retains the validator's exact JSON response as `validationReportJson` so its UTF-8 bytes can be checked against `validationReportSha256`; this is null for a draft. Keep the source inputs and report with the private candidate evidence. Inspect the companion text for private information before disclosure: an observation's rationale is included in that document. No raw evidence files, credentials, local file paths or signature image are automatically attached to the form.

## Verification and limits

Before writing, the generator compares canonical AcroForm fields with the page widgets, exact page positions, field types, appearance states and pinned inventory. It rejects ambiguous/orphaned fields rather than repairing them by guesswork. Already checked or signed templates are refused. After filling, it reopens the generated bytes and verifies that the checkbox values, widget values and visible appearance states agree. The official pages' printed content streams, text and page dimensions must be unchanged. The form stays interactive; it is not flattened.

The original checked appearance depends on an unembedded symbol-font glyph. A local render showed empty boxes despite correct `/V` and `/AS` values. The generator now retains each checkbox's original off appearance and draws its checked tick as a vector path, avoiding that font dependency. A synthetic checked copy of the actual Extension form was rendered to verify the visible ticks; it is not driver-test evidence. The regenerated blank sample driver draft remained pixel-identical to all seven visually inspected pages.

The synthetic integration test invokes the real .NET validator, generates a populated form and verifies the stored values and appearances. Altering its retained evidence subsequently causes validation to fail. The fixture is explicitly synthetic, never deployed and never used as driver-test evidence. The actual Extension template has been exercised through draft generation; the actual Video Server template's fields/widgets have been checked against its inventory. An evidence-populated official Extension form has subsequently been rendered, reviewed, explicitly authorized and signed through the published console. This does not establish real Video Server submission coverage; see [validation status](ValidationStatus.md).

Render and inspect every page before proceeding. `visualReviewRequired`, `signingAuthorizationRequired` and `submissionReady: false` remain explicit in every generated report. These checks are point-in-time validation, not an immutable bundle or authenticated producer. The later bundle/signing stage must protect and revalidate the exact package, completed form and authorization. An output PDF without a successful report is not a successful generation step.

For the separate [authorized signing step](FormSigning.md), use `for-signing` with the same evidence arguments. It produces a neutral companion summary, still with blank signature/date fields, for review of the exact bytes to be signed. This mode cannot accept a blank draft or omit evidence validation. Ordinary `from-evidence` output keeps its prominent unsigned-review notice and is deliberately refused by the signer. Real signatures, final visual review and delivery remain separately authorized work.

### Diagnosing a local review failure

Use the pinned inventory's signing field names and verify canonical values against
page widgets; do not guess field names from another form. When using pypdf, resolve
indirect annotation arrays before inspecting their length. Check the available
renderer first, and retain its actual exit status and stderr before assertions.
A failed inspector is not evidence that form generation failed.

Keep the original PDF and render hashes when investigating apparent missing text.
Inspect the affected page at full resolution and, if needed, a crop of the existing
render and an independent renderer. Reconcile conflicting observations against the
same saved bytes before attributing a layout defect or regenerating. Text extraction
and coordinates support diagnosis but do not replace visual inspection. Withdraw
unsupported findings explicitly without deleting their original review record.

If a renderer cannot open a long input path, retain the failure and try a shorter
copy only after verifying byte identity. This is a local path workaround, not a
change to the reviewed PDF or proof of a fixed renderer. Keep unresolved causes
and any nonfatal renderer warnings in the review record.

## Retained validation failures

A nonzero evidence validator result is retained beside the requested private output in a fresh `validator-failure-*` directory, including original stdout, stderr, exit and hashes. Inspect the complete exit-1 assessment before changing inputs; a valid negative report must not be discarded as a generic exception. Do not publish these private diagnostics or replay equipment tests to repair input/schema errors. See [workflow reliability](WorkflowReliability.md).
