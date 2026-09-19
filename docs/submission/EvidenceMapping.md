# Carry reviewed evidence into the submission policy

Availability: implemented in source after DevTools 1.10.0; not included in that released console. The C# API is `SubmissionEvidenceMapping.MapFiles`; the matching console command is `submission-map-evidence`. No Python code or interpreter configuration is required.

A producer can legitimately use a narrower policy than the complete submission. For example, a periodic endurance collector measures a defined continuous interval, while the complete policy separately requires subsequent functional tests and a performance comparison. Keep the original collector records and policy unchanged. Do not edit their identities or extend their timestamps to include later tests.

This handoff creates derived observations under an independently reviewed mapping. It preserves outcomes, timestamps, samples, response measurements and restoration records. It only changes the requirement ID, target name and destination policy identity. Each derived observation also retains references to the original observations document, original policy, destination policy and mapping itself, with their exact digests.

## Review before mapping

The trusted workflow must authenticate the source producer and review that its actual assertion satisfies the destination assertion. Matching names, a successful NUnit result or a duration alone is insufficient. This API cannot determine behavioral equivalence from free text. Approving a mapping that overstates a test's scope still produces an unsupported claim, even when all structural checks pass.

The mapping must be one-to-one. It cannot combine several partial tests into one pass or expand a single narrow observation into many claimed checks. Both policies must have explicit execution scopes using the same observation method. Package, source commit and official form identity must match; the policy identity may differ. New requirements remain uncovered until real evidence is supplied.

Prepare the following private JSON mapping, using the exact original and destination identities:

```json
{
  "schemaVersion": 1,
  "sourceIdentity": {
    "packageSha256": "PACKAGE_SHA256",
    "sourceCommit": "FULL_RELEASE_COMMIT",
    "policySha256": "ORIGINAL_POLICY_SHA256",
    "templateSha256": "OFFICIAL_FORM_SHA256"
  },
  "destinationIdentity": {
    "packageSha256": "PACKAGE_SHA256",
    "sourceCommit": "FULL_RELEASE_COMMIT",
    "policySha256": "REVIEWED_DESTINATION_POLICY_SHA256",
    "templateSha256": "OFFICIAL_FORM_SHA256"
  },
  "sourceObservationsSha256": "ORIGINAL_OBSERVATIONS_SHA256",
  "requirements": [
    {
      "sourceRequirementId": "collector.periodic-function",
      "destinationRequirementId": "submission.continuous-function",
      "sourceTarget": "gateway",
      "destinationTarget": "$endurance",
      "rationale": "Describe the reviewed assertion, retained functional measurements and exact limits of this correspondence."
    }
  ]
}
```

Uppercase values are placeholders, not accepted digests. Retain the mapping's SHA-256 through the trusted review process separately from producer output. A producer's self-reported mapping digest is not approval.

For the original output of `endurance-export`, add `"sourceFormat": "endurance-export"` to the mapping. That command emits one PascalCase observation, rather than a camelCase observations document. The importer reads those exact pinned bytes without rewriting or wrapping the retained file. Omit this field (or use `"document"`) for the normal versioned observations document. Keep the original approved policy alongside it; the worker's plan is not itself a policy document.

If the collector's original policy is a behavioral collection document rather than the generic `requirements` schema, also include its original reviewed worker file:

```json
"sourceWorker": {
  "relativePath": "original-worker.json",
  "sha256": "REVIEWED_ORIGINAL_WORKER_SHA256"
}
```

The importer then reads the executable requirement from that worker, verifies its candidate/policy identity and producer inventory identity, and retains both the original policy and worker. It never creates a replacement policy and pretends its digest is the original. Before approving the mapping, review that the worker's executable duration, cadence and scope implement the collection policy; the importer cannot infer the meaning of arbitrary policy prose. This mode is only available with `endurance-export`. It validates the pinned inventory identity without executing the producer or contacting its processor. Authentication, the original bundle verification and successful reservation cleanup must already be established by the trusted export/handoff workflow.

## Run the public command

Arrange the original documents and referenced files inside one private evidence tree. Preserve all relative paths referenced by the original observations, including response, restoration and sample references. If two runs use the same relative filename for different content, reconcile their evidence layout upstream; this command does not rewrite source records or merge conflicting trees.

```text
CrestronHomeDevTools.Console.exe submission-map-evidence --evidence PRIVATE_EVIDENCE_DIRECTORY --mapping mapping.json --mapping-sha256 REVIEWED_MAPPING_SHA256 --source-policy original-policy.json --source-observations original-observations.json --destination-policy submission-policy.json --output NEW_PRIVATE_OUTPUT_DIRECTORY
```

All four input paths are relative to the evidence tree. The output must not already exist; its parent must exist. The operation runs offline and makes no processor changes.

It validates the entire source policy/run before importing anything. Failed, duplicate, missing or altered source observations cannot be discarded to select a passing row. It then validates each imported observation against the destination requirements. A longer duration, missing restoration, stricter response deadline or missing endurance cadence cannot be filled with invented values.

Exit codes:

| Code | Meaning |
| --- | --- |
| 0 | The mapped subset passed structural checks. `observations.json` and `report.json` were retained. |
| 1 | Source or destination evidence checks failed. A report is retained, without an observations document. |
| 2 | Invalid arguments, changed pins, unsupported mapping or storage failure. Do not consume any partial output. |

`report.json` lists `unmappedRequirementIds`. Even an empty list does not establish producer authenticity, policy completeness or certification. Always run the complete candidate/package/form/evidence validation after assembling all observations, followed by the normal review and signing gates. Do not use this command's exit code as the full submission gate.

## C# API

```csharp
SubmissionEvidenceMappingReport result = SubmissionEvidenceMapping.MapFiles (
    evidenceDirectory, "mapping.json", reviewedMappingSha256,
    "original-policy.json", "original-observations.json", "submission-policy.json",
    DateTimeOffset.UtcNow, cancellationToken);
```

The API does not write files. `Observations` is null on an evidence-check failure; invalid inputs throw. `MappingChecksPassed` refers only to the requested subset. The original documents are left intact and included in each derived observation's retained-file list, so the existing evidence bundle retains this chain of provenance.

This handoff does not bind executable producers in a draft coverage contract, prove chronology between separate official requirements, perform a performance comparison or manufacture evidence for tests that have not run.
