# Recover individual failed postchecks

The existing explicit `--recover-app-step` command accepts a schema-3 recovery request for ordinary post-endurance cases. It retains successful NUnit executions from a terminal mixed-result invocation and selects exactly its outstanding cases for the next invocation. It never rewrites the original TRX or produces a synthetic all-pass TRX.

The request retains the normal inspected state hash, complete original evidence inventory hash, failed outcome path, replacement fixture/source/profile pins and attempt identity. Add a `CaseRecovery` object containing:

- `Reason`: the applicability review explaining why the original successful cases remain valid after the fixture repair.
- `OriginalTrx`: the retained original TRX relative path and independently inspected SHA-256.
- `Observations`: explicit `Path` / exact NUnit `TestName` mappings. Include every configured postcheck observation and response measurement, every selected case, and any additional reviewed case evidence to preserve.

Candidate, processor, devices, profile, budgets and fixture settings remain bound. Source roots/project locations may identify repaired tooling. The replacement selection must equal the failed cases in the original TRX; passed cases cannot be replayed by this route. Explicit tests, physical operator prompts and managed-child provisioning are excluded. The original invocation must confirm restoration, cleanup, candidate identity and reservation release before any new invocation.

A successful continuation retains both original and new producer directories and writes `case-recovery.json` with each accepted case's exact original TRX digest and execution ID. Observation mappings select the appropriate producer without replacing its files. Finalization still verifies the complete coordinator inventory. Interruptions and failed follow-ups remain terminal until explicitly inspected; running the same request again never invokes its tests again.

For retained schema-1 runs, postcheck repair is allowed at a stopped PrepareReview boundary. Success does not advance that legacy checkpoint. The separately validated explicit test-boundary migration must run before finalization and phase three. Schema-2 runs use FinalizeTests.

Current limitation: this request expects the selected original invocation to contain the full original case selection. A failed partial continuation is retained and cannot automatically be used as a new full-selection basis. Extending the coordinator to consume a reviewed chain of partial results requires preserving each earlier attempt's bindings and evidence; the case-result evaluator already supports exact outstanding selections across multiple snapshots. Do not rerun the full suite to bypass this limitation.
