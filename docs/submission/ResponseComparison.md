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

With `Limits: null` and no `Assessment`, supply a scoped `Review.PlannedGaps` declaration or equivalent
candidate-bound declarations input. Results remain `Partial`, with measured deltas
and ratios available for review. Neither mode proves statistical equivalence,
physical relay latency, or first visible app response unless the producer actually
measured that endpoint.

The original inputs, selected limits and completed interval receipt are pinned in
`response-comparison/plan.json`; the report and observation have a retained receipt.
Re-entry verifies those files instead of replaying device controls or rewriting
results with new criteria. Interrupted output requires inspection before recovery.

A failed comparison blocks phase-two finalization before removal. Retained finalization receipts are also checked, so a historical finalization receipt cannot conceal a failed comparison. Document preparation cannot relax limits, subtract observer overhead retrospectively, or relabel the result. Transport overhead can explain a measurement without establishing a driver defect; preserve the original result when investigating the method.

The updated outlet producer uses concurrent API observation (method v2): it watches attributed command completion while the single guarded Android input is in flight. Input transport return is recorded separately and must also succeed. This removes serial observer delay, but dispatch and API polling overhead remain. The method identity differs from the earlier serial observer, so the comparison rejects mixing their series. This change does not repair or reinterpret an already-retained failed comparison.


## Visible response evidence

Crestron's Extension Test Plan, System Tests 3, requires continued functionality and no performance degradation, with immediate functions and prompt, accurate feedback. It supplies no 3-second, +1-second or 2x response thresholds. A diagnostic comparison alone must not be described as the official requirement or used to infer a driver defect from transport delay. Existing frozen diagnostic results remain preserved; changing their interpretation is a separate phase-two assessment, never document-stage repair.

The Kasa/Tapo fixture now accepts an explicit optional `CaptureResponseVideo: true` setting. Each measured guarded outlet tap retains a bounded H.264 display recording and metadata alongside the command and independent physical-state evidence. `AndroidDevice.StartScreenRecordingAsync` waits for the first encoded display-frame NAL before returning; codec headers or a fixed startup delay do not authorize input. The existing fresh page guard still runs. Recording ending before the input prevents that tap, without replay. All ordinary restoration remains required.

The 15-second capture window is a recording limit, not a response-time acceptance threshold. A reviewer must decode the original video, verify coverage of the before/input/after sequence, and compare visible behavior before and after the same interval. Use Android's overlaid frame times, not nominal playback frame rate or host transport return. Recording itself does not prove physical relay latency or automatically create a performance pass. This source-preview option still requires a live before/after validation; the recorder smoke check sends no input and is not a driver performance result.

The stream options come from Android's screenrecord implementation: https://android.googlesource.com/platform/frameworks/av/+/refs/heads/android11-mainline-extservices-release/cmds/screenrecord/screenrecord.cpp . They are capability-dependent and unsupported recorders fail before input; no device setting or installation is changed to enable them.

## Qualitative performance assessment in phase two

For the actual qualitative performance requirement, configure
`Assessment: "performance-assessment/assessment.json"` and leave `Limits` null.
After the post-endurance capture and its normal restoration have completed, the final
NUnit stage waits for this phase-two assessment. It reuses completed producer receipts;
it does not replay device controls while waiting. The reviewer assesses the retained
evidence and writes this separate record. Original test outputs remain unchanged.

The `SubmissionPerformanceAssessment` identifies the same candidate, requirement and
completed interval, names its reviewer, and covers each compared device/control once.
Each finding records preserved functionality, no performance degradation, immediate
functions and prompt accurate feedback, with a reason and hash-bound supporting evidence
from both original producers. Appropriate recordings, observations or other test
evidence can support it; video is not a Crestron requirement. Timing series alone
cannot support the qualitative finding.

All affirmative findings produce Passed; an explicit negative produces Failed; an
unknown produces Inconclusive. Failed and Inconclusive results block finalization.
This records an assessment, not an inference from file presence or numerical deltas.
The assessment and its source evidence are pinned by the phase-two comparison receipt.
Re-entry rejects changes. Phase three cannot create or amend this assessment, and
this path cannot replace a frozen failed comparison.
