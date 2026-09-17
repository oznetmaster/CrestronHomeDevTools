# Processor uptime observations

This API is a local source addition after DevTools 1.6.0. It is not included in the published 1.6.0 package. Release availability will be documented when the endurance integration is ready.

`ProcessorUptime.ReadAsync(host, credential, sshFingerprint, timeout, cancellationToken)` opens an authenticated SSH console, waits for its observed prompt, and sends the read-only `uptime` command once. It does not reboot, change configuration, disable logging or acquire a processor reservation. The caller owns coordination with other development operations.

The result contains the console duration, its local last-started timestamp, and the worker's UTC timestamps immediately before command submission and after the complete response. `EarliestStartUtc` and `LatestStartUtc` bound the inferred start time using the request/response interval and a conservative 10 ms allowance on each side for the console's hundredth-second precision.

The local last-started timestamp is **diagnostic only**. Real CP4-R observations varied by one second without a reboot. It has no timezone, so do not label it UTC or use its string as an immutable boot ID. Comparing inferred windows also depends on the worker's clock: a discontinuity or excessive skew must fail or require review, rather than being silently attributed to the processor. These observations are not cryptographic boot or loaded-code attestation.

An endurance producer must bind its original start window and approved clock tolerances to its reviewed run, verify each new observation against that original reference, and retain the measurements. It must not continually widen or move the reference to make later observations pass. It must separately verify the actual installed candidate, reservation and independent driver function. This API alone does not produce an accepted `SubmissionEnduranceProbeResult` or prove an uninterrupted 24-hour period.

The parser currently recognizes the observed English console format. It accepts fragmented responses and background logs, including logs appended directly to the prompt. Only complete, anchored response lines count; malformed, duplicate or incomplete replies fail. Reads are bounded in time and size. Cancellation, disconnection and uncertain writes never automatically resend the command. Diagnostics do not forward arbitrary console logs or credentials.

Validation includes parser and session regressions plus independent reads on a CP4-R. No new reboot was performed for these reads. A captured prompt/log interleaving timeout and the one-second timestamp variation led to explicit regression tests; earlier failed observations remain failed. Other console formats require their own observed fixtures before support is claimed.
