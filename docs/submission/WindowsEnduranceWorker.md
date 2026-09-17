# Windows endurance worker

The scheduler scripts in this source revision work with the published DevTools 1.7.0 Windows console. They are not included in the 1.7.0 release archive. A subsequent console release will include them under `scripts/endurance`. This is optional driver-submission tooling; ordinary CI and library releases do not require an endurance worker.

Use a separate Windows computer when development-machine restarts should not interrupt monitoring. The self-contained Windows console includes its .NET runtime; Visual Studio, an Android emulator and a separate .NET installation are not required unless the driver's independently reviewed probe needs them. The computer must remain powered, awake and able to reach the processor and any devices the probe reads.

## Prepare a reviewed run

First prepare the immutable candidate, trusted read-only probe, complete file pins, approved observation policy and private `SubmissionEnduranceWorkerPlan` described in [endurance collection](EnduranceCollection.md). A scheduled task cannot decide which candidate or policy should be accepted. Keep all acceptance criteria in those pinned inputs, and mutable credentials in a separate protected settings file.

An example private directory layout is:

```text
C:\Private\Endurance\candidate-a\
    cli\                  Complete published Windows console bundle
    scripts\              Reviewed scheduler scripts
    probe\                Complete published and pinned driver-specific probe
    worker.json           Reviewed worker plan
    connection.json       Private CLI connection settings
    probe-settings.json   Private probe credentials, if required
    schedule.json         Generated scheduler configuration
    run\                  Collector journal, ownership and observations
    scheduler-state\      Wrapper attempts and local attention signal
```

The scheduler runs as Windows **LocalService**, without an interactive desktop or saved Windows login password. Before registration, an administrator must protect these directories: administrators and the owner may administer them; LocalService needs read/execute access to the CLI, scripts, probe and input files, and modify access only to `run` and `scheduler-state`. Remove access for unrelated users. Do not grant the worker permission to rewrite its scripts, candidate, criteria or file manifests. These scripts do not change ACLs or copy credentials automatically.

LocalService uses the explicit private connection file; it cannot decrypt another user's saved desktop profile. Ensure that granting this account access to the processor and probe credentials is authorized. Never put passwords in task arguments, source control or public artifacts. The probe must also obey these privacy requirements. The wrapper removes inherited `CRESTRON_HOME_*` environment overrides before invoking the CLI so they cannot silently replace the reviewed connection settings.

Deliberately start the reviewed run once using the published CLI's `endurance-start` command and the same worker, run directory and connection settings. Confirm acquisition and ownership. Starting a run is separate from scheduling: **none of these scripts invokes `endurance-start`**.

## Pin and register the task

Use absolute local paths. Keep the CLI, run and scheduler-state directories separate; keep input files outside all three. Run these examples in an administrator PowerShell session after the inputs and permissions have been reviewed:

```powershell
$base = 'C:\Private\Endurance\candidate-a'
& "$base\scripts\New-EnduranceScheduleConfiguration.ps1" `
    -CliDirectory "$base\cli" `
    -WorkerFile "$base\worker.json" `
    -RunDirectory "$base\run" `
    -SettingsFile "$base\connection.json" `
    -StateDirectory "$base\scheduler-state" `
    -Output "$base\schedule.json"

& "$base\scripts\Register-EnduranceScheduledTask.ps1" `
    -TaskName 'Crestron-Endurance-CandidateA' `
    -Configuration "$base\schedule.json" `
    -TickScript "$base\scripts\Invoke-EnduranceScheduledTick.ps1"
```

Configuration creation records hashes of the worker plan, tick script and every published CLI file. It does not approve those files and never overwrites an existing configuration. The collector separately validates the complete probe bundle and its candidate binding. Protect the configuration itself with the administrator-owned ACL; file hashes do not protect against a malicious administrator.

Registration refuses to overwrite an existing task. The task has a startup trigger and repeats every minute, ignores overlapping starts, runs without a logged-in user, and has no task-level forced time limit. The probe's own deadline handles normal cancellation. A minute-long scheduler interval must be suitable for the approved sample interval and maximum gap, allowing for the actual probe duration and startup delays. Registering a task does not establish that timing requirement.

## Results, interruptions and alerts

Each invocation has a retained intent, child process identities, private stdout/stderr and a completed result under `scheduler-state/attempts`. `scheduler-state/status.json` is atomically replaced after a completed attempt. These are operational records, not submission evidence; only the collector's revalidated export can enter the evidence pipeline.

| Exit code | Meaning |
| --- | --- |
| 0 | A known collecting or passed state was observed; inspect `State` to distinguish them. |
| 1 | A failed observation and confirmed reservation release were observed. |
| 3 | Operator attention is required; no new period is automatically started. |
| 4 | Another wrapper holds this scheduler directory's lock; this invocation did no work. |

The wrapper inspects the collector before doing any work. Pending probes, interrupted runs, unknown ownership, an unstarted run, changed pinned files or malformed status prevent a tick. A known terminal result whose reservation is still held may finish normal cleanup through the existing CLI. Uncertain acquisition or release is never replayed. Passed/released runs receive no further probe executions; disable their task when archiving the result.

A wrapper that terminates without its completed result leaves an unfinished attempt. The next invocation records `AttentionRequired` and does not launch the collector. `attention.json` latches failed or uncertain outcomes so subsequent triggers cannot quietly recover them. An operator must inspect process state, the monitor journal and remote ownership before any reconciliation. Do not delete an attention file, unfinished attempt or remote reservation merely to obtain a green run. Retain the failed evidence and use a newly approved run when continuity cannot be proved.

The wrapper waits for its collector child to exit. If the parent is killed, its child might remain alive; a subsequent wrapper refuses to launch another collector. Investigate the recorded process identity before terminating anything. A Windows restart between *completed* attempts may resume the same journal, but the collector must still verify the original identities, reservation and maximum gap. A restart during an unfinished attempt requires inspection. No timer is backdated or restarted automatically.

`attention.json` is a durable **local alert signal**, not an email or delivered notification. Configure an independent monitoring service to alert on this file, nonzero task results, a stopped/disabled task, missing results or stale collecting observations. That service should also notice when the monitoring computer is offline. A worker cannot reliably report its own loss of power. Test actual notification delivery to an approved destination before relying on unattended endurance monitoring.

## Validation and remaining deployment checks

The wrapper has synthetic Windows PowerShell 5.1 checks covering normal collection, completion, failure, incomplete/unknown ownership, malformed output, inherited connection overrides, changed files, overlapping invocations and process-tree termination. These tests do not contact a processor or count toward an endurance requirement.

Before a real run, validate the registered task under its actual service account, the protected credential files, independent alerts, an operating-system restart between observations, interruption during an observation, permitted sample gaps and shared physical-device coordination. A processor reservation coordinates cooperating operations on that processor; it does not reserve a physical device shared with another processor. Complete the full approved duration against the exact final candidate after those deployment checks pass.

To retire a reviewed terminal task, use `Unregister-ScheduledTask -TaskName 'Crestron-Endurance-CandidateA' -Confirm`. Unregistering does not release a processor reservation or delete retained evidence. Check known cleanup separately before archiving private files.