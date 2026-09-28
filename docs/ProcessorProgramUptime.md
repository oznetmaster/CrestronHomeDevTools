# Home program uptime

Status: source addition for the next batched release. Read-only validation has been performed on an MC4-R running Home 4.012.0194; other firmware and processor models need verification.

`ProcessorProgramUptime.ReadAsync` reads the default program's `PROGUPTIME` over authenticated, host-key-pinned SSH. It reads `PROGCOMMENTS` before and after the duration and requires the exact supplied `ProcessorProgramIdentity` (boot directory, application name and program file). It does not restart a program or change slots. Although this firmware's console help advertises a program suffix, the observed suffixed commands were rejected; the reader uses only the default program and fails if its identity differs.

For the tested Home program, the identity was `/simpl/app00`, `Crestron.Seawolf`, `Crestron.Seawolf.dll`. Confirm the expected identity for the actual test target rather than assuming a slot or silently accepting another program.

The snapshot retains the raw uptime response, request and observation times, elapsed duration, and the processor's local start text. `EarliestStartUtc` and `LatestStartUtc` account for request latency and one millisecond of console quantization. The local calendar string is diagnostic only: it is not converted to UTC. This firmware prints an integer millisecond component without padding: `.47` is 47 ms, not 470 ms. Consecutive live replies across a second boundary established this behavior.

This is a program-start clock. It does not establish when an individual driver was loaded, when the Home program finished initialization, or when any function became usable. Measure those functions separately. Unsupported, missing, contradictory or ambiguous replies fail instead of substituting system uptime, an open port, an API response, or an operator acknowledgement.

For recovery checks, retain a baseline from before the interruption. Require a new processor boot and new expected program start; preserve that recovery window and check later observations against it. Do not slide the reference window forward after each query. A restart, changed program identity or inconsistent clock must invalidate the continuity claim.

Offline tests cover identity changes, fragmented responses, stale greeting output, incomplete/duplicate replies, malformed durations and the captured unpadded-millisecond sequence. Two successive live reads produced overlapping program-start windows; a separate system uptime read placed the boot before the program start. These read-only checks are not evidence that a physical outage or an end-to-end workflow has passed.
