# Outage recovery timing evidence

The reviewed plan defines the recovery clock and deadline. Network recovery uses
network restoration; power recovery uses program load. Keep those test steps
separate, including when the processor gets power through its network cable.

## Manual network restoration

The operator's reconnect acknowledgement is an audit record, not the instant the
cable was connected. Start the recovery observations as soon as independently
observed connectivity permits them, and join all observation tasks before cleanup.
Do not defer those observations until the operator returns to click Done.

For a network-only test, `ISubmissionManualRestorationBounds` can retain fresh
connectivity as an upper bound on restoration, with the unchanged processor and
program epochs verified independently. This prevents a late Done response from
placing already collected recovery evidence before restoration. It does not move
the earliest bound forward: the reconnect request remains the conservative lower
bound unless a separate physical event observation establishes a tighter one.
An open port cannot establish the corresponding power-restoration event.

Before requesting another physical test after `recovery-deadline-unproven`, check
both the instrumentation and the full observation cost. For example, a 42-second
check plus 24 seconds between the reconnect prompt and actual restoration cannot
prove a 60-second deadline using the prompt as its earliest bound. A successful
connected diagnostic alone does not resolve that uncertainty. Preserve the
partial result; do not infer a driver failure, change the limit, or repeat the
operator action merely to try for a faster response.

If a frozen attempt is no longer usable while its readiness request is still
unanswered, the controlling workflow can call
`SubmissionOperatorStep.WithdrawReadiness(handle, reason)`. This retains
`workflow-withdrawal.json` separately from operator responses and cancels that
checkpoint so its worker can exit normally. It refuses physical-action prompts
and existing operator responses. A repeated withdrawal with the same reason is
idempotent. It does not authorize a new attempt, replace its evidence, or claim
that cleanup succeeded; verify the worker's retained restoration and cleanup
results before proceeding.

## Program start versus load completion

Program uptime establishes when the program started. It does not by itself
establish when initialization completed. An observer that has only a program-start
capture must set `ProgramLoadIsLowerBound` on its hardware binding. The manual
adapter forwards that declaration from its observer. The recorder preserves it
in a schema-2 measurement record; the plan's deadline and clock remain unchanged.
Existing bounded load-completion captures continue using schema 1.

The assessor uses the earliest possible program start as a conservative recovery
origin. All required functions must still pass and the original state must be
restored. A function result inside that stricter deadline proves timely recovery.
When recovery exceeds that bound, the assessor reports `Partial` with
`recovery-deadline-unproven` and `program-load-completion-not-observed`. It does
not report a timing failure: there is no observed latest load-completion time.
The minimum recovery duration is therefore absent. Genuine functional failures
and failed restoration still produce failures.

For example, with a start bounded by 10:00:00–10:00:02 and a 60-second limit,
all functions verified by 10:01:00 meet the conservative bound. Completion at
10:01:20 cannot establish whether recovery took too long after the program
finished loading. It needs additional load-completion evidence; it is not a pass.

The importer retains the original record and evidence digests. It rejects a
lower-bound marker in schema 1, without a start capture, or for a network clock.
Partial measurements cannot satisfy the evidence gate or start endurance.

Do not replace program-load time with the first successful driver query, restart
the deadline when retrying, or reinterpret retained failed attempts after the
fact. An installation that routinely exceeds the conservative bound needs an
independent, justified load-completion observation before repeating its physical
power test. Document that prerequisite before requesting operator action.

## Home diagnostic load event

`CrestronHomeLoadLog` reads the global `System\Runner: * Loaded System` event
from dated logs under `/rm/SeawolfDiagnostic` using pinned SFTP. It binds that
event to the Home program identity and start epoch, checks the logged duration,
and retains the raw logs with bounded UTC timestamps. The caller must verify the
program epoch before and after reading. Two dated files support loading across
midnight. Missing, ambiguous or inconsistent events fail explicitly; API
availability never substitutes for this event.

Before requesting an outage, validate logging support against the currently
running Home program. Repeat the observation against the new epoch after power
restoration. A successful preflight proves instrumentation availability only;
the physical interruption, functional recovery and restoration still need their
own evidence. Current validation covers the observed Home 4.012.0194 log format;
other firmware must pass the same preflight.

## Independently bound startup instances (schema 4)

A power plan may explicitly opt into `DriverInstances`: a pinned instance ID,
parent ID and required functions for each measured platform, child or sub-child.
Every required function belongs to exactly one instance; parents must be present
and the hierarchy must be acyclic. Legacy plans keep their existing semantics.
This implements a reviewed interpretation of initialization timing. Crestron's
published extension test plan says "once the program loads" and does not specify
separate platform/child timers or an exact initialization callback.

The hardware binding must advertise exactly those `StartupInstanceIds` before
any physical operation. Schema 4 records retain each instance's initialization
capture and whether it only supplies a lower bound on receipt. The assessor
checks each function against its own instance's clock. It never substitutes the
latest child's clock for the parent, resets a timer on reconfiguration, or drops
a missing child or function. Parent responsibilities such as creating children
must be included in the parent's functional assertions when applicable.

`CrestronDriverInitializationLog` observes Home load completion, exact instance
creation and Home's saved-configuration dispatch. Bind the required item names;
never store credential values in those bindings. Dispatch can precede receipt
inside the driver, so the observation is explicitly conservative. Ready,
Running, callback completion and command success cannot supply its origin.
`ReadManyAsync` reads all bound instances from one log snapshot. Verify the epoch
before and after capture and preflight logging support before prompting.

Schema 4 also retains function observation windows: a late successful check is
not proof that the driver actually recovered late. Missing evidence is partial;
explicit functional failures and failed restoration remain failures. The report
contains every assessed function interval. The importer uses the largest actual
interval and its matching start/end evidence for the overall response summary,
not the latest absolute timestamp from a different instance. Network-only timing
is unchanged. Old records cannot acquire independent clocks retroactively.

## Validate the launcher's dependencies

Validate retained results with the workflow assemblies from the actual automation
output directory before requesting physical actions. A fixture test host can load
newer assemblies and hide an older dependency in the launcher. Record the loaded
assembly paths and hashes alongside the launcher validation; do not strip newer
result fields or relax evidence parsing to make an older reader accept them.

An explicitly reviewed tooling repair may resume an attempt that retained only
its binding and original evidence inventory and never created an invocation.
Retain the original binding and append the exact replacement tool hashes and
reason. Candidate, fixture, timing and request bindings remain unchanged. Once
any invocation directory exists, this repair path is unavailable: inspect its
actual result and restoration instead. A launcher repair is not a test pass.

## Request timeout versus cancellation

Startup reads may return HTTP 500/502/503/504 while Home initializes. The recovery
fixture retries those responses and HttpClient request timeouts (cancellation
with an inner TimeoutException) only for read-only inventory/configuration.
Each retry stays inside the original observation budget. Commands are never
retried by this policy. Authentication, certificate and unclassified failures
remain terminal; caller cancellation stops promptly.

An expired internal observation budget is a timeout, not an operator cancellation.
Concurrent observers join before cleanup, retaining the failing branch. Unknown
transport cancellation without a caller request is a harness error. None of
these cases proves a driver recovery-time failure or permits replaying an outage.
Retain original results and use a fresh, explicitly bound continuation after repair.
