# Resumable endurance collection

`SubmissionEndurance`, `SubmissionEnduranceMonitor`, `SubmissionEnduranceProcessProbe` and the endurance CLI commands require DevTools 1.6.0 or later. Their journals, collection, reservation orchestration and child-process protocol have regression tests and real-processor validation. Consumer-owned probes have also exercised short real-function observations, standard evidence export and deliberate driver-restart rejection. These checks do not constitute a final candidate endurance period. The consuming project remains responsible for its probe, scheduler, interruption/restart validation, approved policy and complete candidate-specific duration.

## What the collector does

Call `SubmissionEndurance.CollectAsync` periodically with the same private run directory and pinned `SubmissionEndurancePlan`. Each call makes at most one read-only functional observation. It returns immediately without calling the producer when the next sample is not yet due. Completed and failed runs are terminal.

The plan binds the package, source commit, approved policy, official template, processor identity, installed instance, shared reservation and trusted producer. Its endurance requirement supplies the minimum duration and approved maximum sample gap. The chosen interval must leave time for the probe within that gap. The collector does not invent or approve those values. A short duration is useful for synthetic tests; actual submission policy must carry the official duration.

Each producer result supplies the identities it actually verified, the processor's current boot identity, a functional outcome and retained evidence bytes. The collector records those fields and the original bytes in a private sample file, hashes it and atomically replaces a flushed checkpoint. The first successful sample starts the observation interval. The last successful sample must cover its end. It never backdates samples to the intended schedule.

`SubmissionEndurance.Export` returns a standard `SubmissionObservation` only after the full interval has passed and the existing evidence validator accepts the retained samples. It rechecks evidence hashes on export. This output can enter the normal evidence and bundle pipeline; it does not bypass that pipeline, authenticate the producer or establish complete coverage of the official test plan.

## Validate a received plan

`SubmissionEndurance.ValidatePlan(plan)` requires 1.7.0 or later. It performs the collector's structural validation without creating a journal or contacting a processor. An external producer can use it before comparing the entire request with its independently reviewed binding. It checks complete identity pins, the read-only endurance contract, positive duration and cadence, and room for the probe within the sample gap. It does not approve a policy, authenticate a caller, establish an official duration, or verify the actual package and device.

## Producer and reservation requirements

The consuming driver supplies its functional probe. A TCP connection, driver readiness flag or successful HTTP status alone is insufficient. The probe must verify the installed candidate and perform the approved, driver-specific functional comparison with an independent observation. Retain enough information to review that comparison. Keep credentials out of probe results and exception messages intended for evidence.

A processor boot identity alone cannot detect a driver process restarting within the same boot. The probe must also bind and verify a suitable driver-lifetime identity and evidence that its observed state is fresh. The same package version and a ready flag do not establish continuity. When using an inferred boot window, retain the original reviewed baseline and bounded clock tolerance; never move the baseline to accept later observations. See [processor uptime](../ProcessorUptime.md) for the API and clock limitations.

The trusted orchestration layer must acquire and maintain the shared processor reservation for the **whole endurance run**, including between scheduler invocations. Every sample must verify that ownership is still valid. The plan's reservation string is a binding, not a lease implementation. The collector's local exclusive file lock prevents overlapping invocations against one journal; it cannot stop another computer or a different journal from accessing the processor. Shared physical devices also need coordination across processors.

### Scheduled observations with a persistent processor reservation

`SubmissionEnduranceMonitor` wraps the collector with the existing shared processor lease used by cooperating development tools. Supply a unique `Guid.NewGuid().ToString("N")` as the plan's reservation ID, the processor address and pinned SSH fingerprint, and credentials loaded privately by the worker.

Call `StartAsync` once when deliberately starting a new run. It records acquisition intent before contacting the processor, then retains the remote reservation after closing its connection. Starting an existing run again is rejected. A new worker process must call `CollectAsync` with the same plan, endpoint and private directory; it must not acquire a replacement reservation.

Each due observation reconnects and verifies ownership before invoking the supplied probe, and independently reconnects to verify ownership again afterward. Losing ownership prevents an otherwise successful result from being credited. A local exclusive monitor lock also prevents overlapping scheduler invocations. An early scheduler tick does not call the probe or reconnect unnecessarily. The producer still must independently verify the candidate, installation, boot identity and functional result; the reservation check does not do those jobs.

`FinishAsync` releases the reservation only after the collector records `Passed` or `Failed`. It does not turn a failure into a pass. A pending or interrupted probe retains ownership for inspection. Acquisition and release each have durable intent: an uncertain outcome is never automatically replayed. Do not delete the monitor journal or remote lock to make the next scheduled invocation succeed.

The monitor stores its ownership record in `monitor.json`. Collector checkpoints and retained samples are kept separately under `SubmissionEnduranceMonitor.GetEvidenceDirectory(privateRunDirectory)`. Use that evidence directory with `SubmissionEndurance.ReadCheckpoint` and `Export`. The separate locations preserve the collector's rejection of orphaned evidence instead of weakening that check to accommodate monitor files. Credentials are not written into either journal.

The API itself does not install a Windows service or scheduled task. Source scheduler scripts are described in the [Windows worker guide](WindowsEnduranceWorker.md); they are not included in the 1.7.0 release archive. The CLI described below supports separate scheduler invocations. Its private files and credentials must survive a worker restart. Protect shared physical devices separately when other processors can access them.

The producer must be read-only and obey cancellation. Tests that change physical state, simulate outages or require restoration belong in separately reserved fixtures with their own recovery evidence. The API rejects policies requiring a response measurement or state restoration because this collector does not implement those actions. An approved submission plan may need both periodic read-only observations and those separate functional/control tests.

## Restart and failure handling

- A Windows restart between completed samples may resume the same journal only while the approved gap is still satisfied and the environment/reservation is unchanged. A service must load the same pinned plan and directory after restart.
- A crash after probe intent was saved leaves `ProbePending`. The next invocation marks the run `Interrupted` without replaying the probe. It does not assume that an unfinished observation passed.
- A gap, changed identity or boot identity, failed functional check, reversed clock or probe error stops the run. Later successful responses cannot overwrite that failure. Review the retained failure before starting a new run in a new directory.
- Probe cancellation is cooperative. The collector holds its lock until the producer actually exits, even after the timeout expires. It does not abandon an in-flight probe and start another one. A hung producer needs operator/process supervision; killing it leaves the durable pending state for the next invocation to detect.
- Missing or changed retained files, a different plan, an invalid checkpoint or orphaned evidence prevent silent continuation. Do not delete checkpoints to make a failed run appear new.

The journal is private operational evidence, not a cryptographically authenticated log. Protect it and the plan with the worker account's filesystem permissions, pin the producer in trusted CI, and archive completed evidence with independently retained digests before signing. No credentials, local paths or raw device evidence should be uploaded as public release assets.

## Deployment still required

The remaining integration is a driver-specific trusted producer, deployment of a supervised scheduled worker with automatic startup and alerts, private credential provisioning, and validation across an actual operating-system restart. The monitoring computer can be separate from the development computer; it needs compatible .NET, processor/device connectivity and private storage. Android UI tests need an emulator only when the approved producer actually uses the app.

## Scheduler command contract

Prepare a private `SubmissionEnduranceWorkerPlan` JSON document with `plan`, `processor` and `probe` sections. `plan` is the approved `SubmissionEndurancePlan`; `processor` binds the address and SSH fingerprint. `probe` specifies an absolute published program directory, a relative executable filename, a complete list of `SubmissionEvidenceFile` SHA-256 pins, and an optional absolute `settingsFile` path. Compute `plan.producerId` with `SubmissionEnduranceProcessProbe.GetProducerId(probe)` after reviewing the manifest. Do not generate or approve new pins automatically during a scheduled tick. Keep all criteria governing acceptance in the approved plan/producer, not in mutable credential settings.

The producer directory must contain only its pinned published files. Added, removed, changed, duplicate or linked files are rejected. Keep private settings, evidence and logs outside that directory. The producer identity covers the executable, file manifest and settings path; moving the unchanged published directory does not change its identity. On Windows the checked files remain open without write/delete sharing while the child runs. Filesystem permissions must also protect the directory and trusted runtime; hashes are not a security sandbox against a malicious local administrator.

The executable receives a single `SubmissionEnduranceProbeRequest` JSON object through standard input, then EOF. It must emit one `SubmissionEnduranceProbeResult` JSON object to standard output and exit zero. Standard output is bounded to 8 MiB and discarded stderr to 64 KiB. The result's evidence is subject to the collector's separate 4 MiB limit. The wrapper passes no shell command and opens no visible window. The process inherits its worker account/environment; it loads its own private credentials using the provided settings path or the account's configured store. Credentials must never appear in stdout evidence or diagnostic messages.

This is deliberately execution of trusted code, not remote uploaded code. The producer must be read-only, independently verify the target/candidate/boot/function, and must not spawn background processes or leave children running. Echoing identities from stdin does not verify them. The test-only probe project in this repository produces synthetic observations and must never be selected for real acceptance.

The collector supplies the probe deadline. Timeout/cancellation stops the child process tree and waits for the child to exit before returning. If Windows refuses termination, ownership remains held while waiting for external supervision; an operator must investigate. Killing the parent worker leaves a pending journal and cannot produce a pass on restart. Child errors and malformed output are reported without forwarding their potentially sensitive text.

Use the same absolute paths on every invocation:

```text
CrestronHomeDevTools.Console.exe endurance-start --worker C:\Private\Endurance\worker.json --run C:\Private\Endurance\run --profile submission
CrestronHomeDevTools.Console.exe endurance-tick --worker C:\Private\Endurance\worker.json --run C:\Private\Endurance\run --profile submission
CrestronHomeDevTools.Console.exe endurance-status --worker C:\Private\Endurance\worker.json --run C:\Private\Endurance\run
CrestronHomeDevTools.Console.exe endurance-export --worker C:\Private\Endurance\worker.json --run C:\Private\Endurance\run
```

`start` verifies the producer and deliberately acquires the reservation once. Schedule **only `tick`**, never `start`. `tick` collects at most one due sample, and releases after a known `Passed` or `Failed` result. An interrupted run retains its reservation. Unknown acquisition/release outcomes are not retried. A completed tick returns the terminal checkpoint again without another producer execution. If collection completed but the worker exited before cleanup began, the next tick can finish that known terminal cleanup. `endurance-finish` is the equivalent explicit cleanup command and refuses pending/interrupted runs.

`tick` exits 0 for collecting/passed, 1 for failed, and 3 for an interrupted run or unconfirmed cleanup. The JSON `ReservationReleased` field is separate from the functional result: released does not mean passed. `status` is offline and includes both `ReservationState` and `Checkpoint`; passed observations can still have a held or uncertain reservation. CLI `export` requires passing, revalidated evidence and a recorded release. A release failure can prevent the tick's normal JSON response, so inspect `status` before intervening. Offline status reports an interrupted probe or uncertain ownership transition with exit 3.

For Windows Task Scheduler, use the tested published CLI as the executable and the `endurance-tick` arguments above. Configure a startup trigger and a repeating trigger shorter than the approved sample interval, **Do not start a new instance**, and **Run whether the user is logged on or not**. Use a dedicated account with private settings access and network connectivity. A saved encrypted profile must be created by that account on that computer; copying another user's profile is insufficient. Do not pass passwords in the task's arguments. Leave task-level forced time limits disabled for the collector; its probe deadline handles ordinary hangs, and any forced termination requires journal inspection.

Retain private task output and monitor nonzero exit codes and stale sample times through the operator's monitoring system. Task Scheduler alone does not send alerts. Before a real run, verify a scheduled invocation under the actual account, a Windows restart between samples, alert delivery, preservation of the common reservation, and failure when the permitted sample gap is exceeded. These deployment/restart checks have not yet been completed by this source implementation.

See the [submission implementation status](../CrestronSubmission.md) and [evidence CLI](EvidenceCli.md) for the surrounding gates.