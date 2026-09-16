# Generating the official self-test review form

`tools/submission/self_test_form.py` generates an unsigned, interactive review PDF. It preserves the official form's printed pages, adds a companion matrix, and can populate checkboxes from evidence checked by the existing DevTools offline validator. It does not apply a signature/date, upload, email, authorize delivery or claim Crestron acceptance. These tools are source-only and are not included in the published 1.4.0 binaries.

The [submission plan](../CrestronSubmission.md) remains authoritative for the work still required. A generated form is one build artifact, not a replacement for the complete driver-specific policy, trusted evidence producer, visual review and signing authorization.

## Inputs and installation

Install the pinned Python dependencies in `tools/submission/requirements.txt` and build the DevTools console in Release. ReportLab adds the companion pages; pypdf preserves and fills the interactive official pages. Neither Word nor a desktop application is needed. The separate document-test workflow builds the console and runs all discovered tests on Windows and Linux; hosted execution of these new changes remains to be verified.

```text
python -m pip install -r tools/submission/requirements.txt
dotnet build CrestronHomeDevTools.Console/CrestronHomeDevTools.Console.csproj -c Release
python tools/submission/run_tests.py
```

Download the applicable original form from Crestron's published source. Do not commit the proprietary template or a signed form to this repository. The [Extension inventory](extension-inventory.json) and [Video Server inventory](video-server-inventory.json) contain the inspected template hashes, page/field mappings and known minimum observation durations. Pin the reviewed inventory's own SHA-256 separately in the trusted workflow. A template or field-layout revision requires inspection and an explicit inventory update.

## Draft without attestations

```text
python tools/submission/self_test_form.py draft --template OFFICIAL.pdf --inventory INVENTORY.json --inventory-sha256 PINNED_INVENTORY_SHA256 --output Driver-Self-Test.review.pdf --title "Driver self-test plan" --author "Developer name" --report draft-report.json
```

This produces an unsigned review matrix followed by the original form. Every item is marked not evaluated and all checkboxes remain off. Signature/date fields stay blank. Candidate/evidence arguments are rejected in this mode so an unevaluated draft cannot be mistaken for an evidence-backed result. Use fresh output/report paths; an existing artifact is never overwritten.

The Wiser draft was generated with the actual pinned Extension form. All seven pages were rendered and inspected. Its matrix contains no passing claims, and all original printed page streams, field identities and blank signature/date values were verified. This is not a test result for a Wiser submission candidate.

## Evidence-backed unsigned review

First prepare the candidate, approved policy and retained observations described in [EvidenceCli.md](EvidenceCli.md). Add a separately reviewed form mapping:

The source [coverage blueprint generator](CoveragePlanning.md) can prepare the draft policy and complete mapping from explicit driver-specific scopes. It leaves every producer unbound and generates no observations; complete review and execution remain necessary before using those files here.

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
python tools/submission/self_test_form.py from-evidence --template OFFICIAL.pdf --inventory INVENTORY.json --inventory-sha256 PINNED_INVENTORY_SHA256 --mapping form-mapping.json --mapping-sha256 APPROVED_MAPPING_SHA256 --candidate candidate.json --candidate-sha256 TRUSTED_CANDIDATE_SHA256 --policy policy.json --observations observations.json --package Driver.pkg --evidence EVIDENCE_DIRECTORY --dotnet ABSOLUTE_DOTNET_EXE --validator ABSOLUTE_DEVTOOLS_CONSOLE_DLL --output Driver-Self-Test.review.pdf --title "Driver self-test plan" --author "Developer name" --report form-report.json
```

Use the same package, official template, policy and observation bytes retained by the candidate workflow. The generator invokes `submission-evidence-check` itself; it does not accept a user-supplied `passed: true` report. A failed validator or changed candidate/observation/policy/template identity stops generation. It then checks the complete mapping and any inventory duration floor. The known 24-hour requirement must map to a policy observation requiring at least 24 hours; adding shorter observations together does not satisfy that floor. Continuous functional observation and the remaining outage subconditions still belong in the approved policy and test producer.

Only a checkbox whose mapped observations all passed is checked. If any mapped observation is permitted `NotApplicable`, the checkbox remains off and the rationale appears in the companion matrix. This preserves the distinction between pass and non-applicability. Crestron's preferred representation of non-applicable items must be confirmed before signing/submission; this tool does not invent that convention. Failed, partial, inconclusive, missing or duplicate observations cannot yield an evidence-backed form.

The report records candidate, package, inventory, mapping, policy, observations, validation-report and generated-form hashes. It retains the validator's exact JSON response as `validationReportJson` so its UTF-8 bytes can be checked against `validationReportSha256`; this is null for a draft. Keep the source inputs and report with the private candidate evidence. Inspect the companion text for private information before disclosure: an observation's rationale is included in that document. No raw evidence files, credentials, local file paths or signature image are automatically attached to the form.

## Verification and limits

Before writing, the generator compares canonical AcroForm fields with the page widgets, exact page positions, field types, appearance states and pinned inventory. It rejects ambiguous/orphaned fields rather than repairing them by guesswork. Already checked or signed templates are refused. After filling, it reopens the generated bytes and verifies that the checkbox values, widget values and visible appearance states agree. The official pages' printed content streams, text and page dimensions must be unchanged. The form stays interactive; it is not flattened.

The original checked appearance depends on an unembedded symbol-font glyph. A local render showed empty boxes despite correct `/V` and `/AS` values. The generator now retains each checkbox's original off appearance and draws its checked tick as a vector path, avoiding that font dependency. A synthetic checked copy of the actual Extension form was rendered to verify the visible ticks; it is not driver-test evidence. The regenerated blank Wiser draft remained pixel-identical to all seven visually inspected pages.

The synthetic integration test invokes the real .NET validator, generates a populated form and verifies the stored values and appearances. Altering its retained evidence subsequently causes validation to fail. The fixture is explicitly synthetic, never deployed and never used as driver-test evidence. The actual Extension template has been exercised through draft generation; the actual Video Server template's fields/widgets have been checked against its inventory. An evidence-populated real driver form and its visual review remain pending.

Render and inspect every page before proceeding. `visualReviewRequired`, `signingAuthorizationRequired` and `submissionReady: false` remain explicit in every generated report. These checks are point-in-time validation, not an immutable bundle or authenticated producer. The later bundle/signing stage must protect and revalidate the exact package, completed form and authorization. An output PDF without a successful report is not a successful generation step. Signature handling and delivery adapters remain separate implementation work.
