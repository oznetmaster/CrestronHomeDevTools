# Response measurements before and after endurance

The source-preview `ResponseComparison` setting compares observations retained by
the initial and post-endurance test producers. It sends no commands. It runs before
final removal and includes original measurements and comparisons in review evidence.
Offline integration is tested; the complete hardware workflow remains unverified.

Use a distinct, untimed policy requirement with method `combined`, required outcome
`Passed`, no physical-restoration requirement and no response or sample-gap limit.
Keep endurance duration separate: this cannot turn a short rehearsal into 24-hour evidence.

```json
"ResponseComparison": {
  "RequirementId": "system.response-comparison",
  "Pairs": [{
    "Name": "selected-outlet",
    "Before": "nunit/AndroidUI/outlet/response-measurements.json",
    "After": "post-endurance/installed-app/AndroidUI/outlet/response-measurements.json"
  }],
  "Limits": null
}
```

The initial path can instead start with `installed-app/` for a separate initial
app-test stage. Files must belong to intact completed producer receipts. Files
placed into the run directory later are not accepted as test evidence.

Each file uses the public `SubmissionResponseSeries` JSON contract:

- `SchemaVersion`: `1`.
- `Identity`: candidate package hash, source commit, policy hash and template hash.
- `DeviceIdentity`: stable actual-device identifier, including the child where relevant.
- `Method`: identical precise measurement description in both phases, including
  transport/observer overhead and the observed endpoint.
- `Measurements`: `Id`, `InputUtc`, `ObservedUtc`, positive finite
  `ElapsedMilliseconds`. IDs describe control and requested operation (for example,
  `room-tile:on`), are distinct within a series, and match across phases.

Use a monotonic clock for elapsed time. UTC timestamps locate observations before
and after the interval. Workers on different PCs need synchronized clocks. If an
initial-state change changes the measured operation, comparison stops rather than
silently pairing different operations.

Optional `Limits` contains `MaximumAfterMilliseconds`, `MaximumIncreaseMilliseconds`,
`MaximumRatio` and a nonempty `Rationale`. Increase must be nonnegative, ratio at
least one, and all values finite. All three limits must hold for every measurement.
These are explicit developer-reviewed criteria, **not thresholds prescribed by
Crestron**. Choose and document limits appropriate to the method; there is no
default tolerance. Exceeding any limit produces a failed observation.

With `Limits: null`, supply a scoped `Review.PlannedGaps` declaration or equivalent
candidate-bound declarations input. Results remain `Partial`, with measured deltas
and ratios available for review. Neither mode proves statistical equivalence,
physical relay latency, or first visible app response unless the producer actually
measured that endpoint.

The original inputs, selected limits and completed interval receipt are pinned in
`response-comparison/plan.json`; the report and observation have a retained receipt.
Re-entry verifies those files instead of replaying device controls or rewriting
results with new criteria. Interrupted output requires inspection before recovery.
