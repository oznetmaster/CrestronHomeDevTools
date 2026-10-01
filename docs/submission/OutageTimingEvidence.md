# Outage recovery timing evidence

The reviewed plan defines the recovery clock and deadline. Network recovery uses
network restoration; power recovery uses program load. Keep those test steps
separate, including when the processor gets power through its network cable.

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
