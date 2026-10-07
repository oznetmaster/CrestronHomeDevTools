# Retained test boundary migration

## Explicit migration of a retained legacy run

New runs use the separate FinalizeTests phase. A schema-1 run stopped at
PrepareReview can move to that phase with the automation worker command:

```text
--migrate-test-boundary --settings FILE --settings-sha256 PIN --state-sha256 INSPECTED_PIN
```

Run this only after inspecting the stopped worker and the exact checkpoint.
The command holds the workflow lock, verifies completed receipts and any
configured post-endurance result, and rejects existing review, signing, delivery
or retention activity. It archives the original checkpoint bytes under their
SHA256 before changing the phase. The operation identity, original failure
reason, status and all completed receipts are preserved. Repeating the command
only reconciles the same unadvanced migration; it does not start or repeat tests.
Use the existing explicit recovery operation after resolving the retained
failure. Document preparation still requires a verified FinalizeTests receipt.
