# CrestronHomeDevTools 1.30.0

Adds two submission commands to the complete Windows console:

- `submission check-combined-approval` checks one explicit authority covering
  signing and submission. It binds the exact unsigned packet and correspondence,
  then checks the actual signed output without inventing its future hash or
  requiring another human decision. Only the approved signature/date and verified
  uploader URL may change. Changed scope or expired authority stops the operation;
  deriving a technical stage record never renews that authority.
- `submission sign-reviewed-packet` signs an explicitly selected manual technical
  review using the encrypted named-signature store and in-memory handoff. It
  preserves printed notes and checklist marks, checks the approved signature
  identity, and emits a receipt that remains explicitly manual. It does not
  fabricate automated producer validation or bypass the existing evidence signer.
  It performs no upload or email.

[Combined approval and command usage](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/submission/CombinedApproval.md)
include private packet, authority, technical-review and signed-inspection templates.
The normal operator procedure defaults to one combined approval after review of
the concrete final packet. Internal signature inspection remains required. Existing
protected stages retain their schemas; legacy CI adapters do not automatically
convert combined authority into their own stage records.

The [reusable submission procedure](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/submission/START-DRIVER-SUBMISSION.md)
and [retained-evidence guide](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/submission/RetainedEvidenceAndApprovals.md)
now distinguish genuine retained measurements from missing producer schema,
absent features from untested supported features, provider/driver/tile clocks,
network interruptions from power loss, and collector faults from device failures.
They cover change-scoped updates, endurance continuity, restoration and uncertain
external-operation reconciliation. These are documented interpretations, not new
claims of Crestron acceptance or waived requirements.

No existing submitted package, signature or evidence is rewritten. No production
driver, device-client or processor protocol changes are included. Requires .NET 10;
configuration-management compatibility remains as documented for Home 4.12 and 4.13.
The interface remains unofficial and firmware-dependent.

Validation covers synthetic approval/signature cases, altered inputs and marks,
expiry, signature access guards, existing automated signing and mocked delivery,
and the isolated Windows console, including the new manual-review command with
a saved synthetic signature. The Windows help/packaging workflow now allows
25 minutes after its previous 10-minute limit cancelled a progressing run.
Required tests and packaging checks remain enabled. Synthetic validation is not
a new real-signature/provider or physical-device test.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are
trademarks of Crestron Electronics, Inc. This independent project is not
affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.
