# Checking submission evidence in CI

The source-only `submission-evidence-check` command combines package preflight and evidence validation. It makes no processor connection, writes no files, signs nothing and sends nothing. It is not included in the published 1.4.0 tools.

```text
CrestronHomeDevTools.Console submission-evidence-check --candidate candidate.json --candidate-sha256 TRUSTED_SHA256 --package NeilColvin_Platform_Example_IP.pkg --policy policy.json --template Extension-Test-Plan.pdf --observations observations.json --evidence evidence
```

When running the console DLL directly, prefix the command with `dotnet` and use the DLL path. Exit 0 means the combined structural checks passed; 1 means a validation or file-operation failure; 2 means invalid arguments or JSON; 130 means cancellation. The JSON report on stdout includes `ValidationChecksPassed`, package defects, evidence defects, and digests of the candidate and observations documents. Missing files and invalid input must fail the CI step; do not treat the absence of a JSON report as success.

## Trust and ordering

1. Build the exact production package with its help PDF already embedded. Retain its digest and source commit in the trusted release job.
2. Review the driver-specific policy against every official requirement and all applicable subconditions. Pin the exact policy and official form bytes. The field inventories in this directory are not approved policies.
3. Produce the candidate declaration below from those trusted inputs. Retain its SHA-256 in the release job separately from test artifacts. Do not obtain `--candidate-sha256` from the hardware worker or recalculate it from a downloaded, untrusted candidate declaration immediately before validation.
4. Run the approved test producer against that candidate. Preserve observations and their referenced files in a private evidence store.
5. Run this command against the retained files. Any changed package, policy, form, observation identity or evidence digest blocks the check. A missing requirement, duplicate claim, unsupported outcome or insufficient duration also blocks it.

The CLI hashes and parses the same candidate/policy/observation bytes, and holds those files and the package/form open during checking. It hashes each retained evidence file while reading it. This is validation at a point in time, not an immutable archive or an authenticated test producer. A later signing/bundle stage must consume a protected snapshot and revalidate its digests. A passing report alone does not authorize submission or prove that the policy measured everything required.

No fixed test count is used. Each policy requirement needs exactly one observation. Use a distinct stable ID for each required subcondition; a form checkbox may depend on several IDs. The eventual form generator must require all of them. The CLI currently validates the supplied policy, not its completeness against the official form. Approved driver policies and that mapping remain implementation work.

## Input format

All documents use schema version 1, exact camel-case property names, UTF-8 without a byte-order mark, and JSON string enums. Unknown/duplicate properties, absent required fields, null required values and numeric enums are rejected. JSON documents are limited to 16 MiB. Hashes identify the exact bytes, including whitespace; changing formatting requires a new digest. Source commits must be full 40- or 64-character hexadecimal commit IDs.

The following examples explain the format. Replace the uppercase placeholders with actual values before use. They are intentionally not passing evidence.

`candidate.json`:

```json
{
  "schemaVersion": 1,
  "identity": {
    "packageSha256": "PACKAGE_SHA256",
    "sourceCommit": "FULL_SOURCE_COMMIT",
    "policySha256": "APPROVED_POLICY_SHA256",
    "templateSha256": "OFFICIAL_FORM_SHA256"
  },
  "packageRequirements": {
    "driverId": "DRIVER_GUID",
    "driverVersion": "1.0.000.0000",
    "kind": "NewDriver",
    "developerFilenameToken": "NeilColvin",
    "publicSupportEmail": "support@marvelous.com"
  }
}
```

Use `ExistingDriverUpdate` only for a driver already accepted on the Crestron portal; an existing GitHub release does not qualify.

`policy.json` (illustrative subset, not a complete submission policy):

```json
{
  "schemaVersion": 1,
  "requirements": [
    {
      "id": "extension.views.home-placement",
      "minimumDuration": "00:00:00",
      "allowNotApplicable": false
    }
  ]
}
```

`observations.json`:

```json
{
  "schemaVersion": 1,
  "observations": [
    {
      "requirementId": "extension.views.home-placement",
      "identity": {
        "packageSha256": "PACKAGE_SHA256",
        "sourceCommit": "FULL_SOURCE_COMMIT",
        "policySha256": "APPROVED_POLICY_SHA256",
        "templateSha256": "OFFICIAL_FORM_SHA256"
      },
      "outcome": "NotTested",
      "startedUtc": "2026-09-16T08:00:00Z",
      "finishedUtc": "2026-09-16T08:00:00Z",
      "files": [],
      "rationale": "No observation has been made."
    }
  ]
}
```

`Passed` observations require one or more retained files, each shaped as `{"relativePath":"run/observation.json","sha256":"FILE_SHA256"}`. Paths are relative to `--evidence`; traversal, absolute paths and links/junctions are rejected. A retained machine-readable observation should include actual environment, instance, device, measured values, test identity and restoration results. Naming a file does not establish those facts; the approved producer must record and verify them.

`Failed`, `Partial`, `Inconclusive` and `NotTested` block validation. `NotApplicable` requires policy permission and a nonempty rationale. Observations cannot be in the future or have reversed timestamps. Durations use the .NET `TimeSpan` string format; 24 hours is `1.00:00:00`. Two timestamps separated by 24 hours do not prove continuous operation: the endurance producer must supply periodic functional observations and account for gaps.

## Privacy and release behavior

Keep raw evidence private: app screenshots, hierarchies and logs may identify the household or contain sensitive information even after masked controls are removed. This command does not redact or upload evidence. Only the selected package, completed signed form and explicitly approved support material belong in the eventual Crestron delivery.

An ordinary GitHub/NuGet release may use the existing hardware-unavailable override. It must leave Crestron submission pending; it cannot fabricate observations or bypass this submission check. Uploader, signing, immutable-bundle and final submission-job integration are still pending. See the [complete implementation plan](../CrestronSubmission.md).
