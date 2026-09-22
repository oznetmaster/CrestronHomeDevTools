# Windows endurance worker

For new runs with DevTools 1.18.0 or later, install [PowerShell 7.6 or later for all users](../PowerShell.md) on the worker and any Windows computer performing remote observation. Existing active runs keep their recorded scripts and shell until completion.

DevTools 1.8.0 and later Windows console ZIPs include the scheduler scripts under `scripts/endurance`. Use a complete release archive; earlier 1.7.0 archives do not contain the scripts. This is optional driver-submission tooling; ordinary CI and library releases do not require an endurance worker.

The worker can run on the development PC. A separate Windows computer is optional when development-machine restarts should not interrupt monitoring. See [development configurations](DevelopmentConfigurations.md) for the baseline one-PC, one-processor setup, shared CI scheduling and interruption limits. The self-contained Windows console includes its .NET runtime; Visual Studio, an Android emulator and a separate .NET installation are not required unless the driver's independently reviewed probe needs them. The computer must remain powered, awake and able to reach the processor and any devices the probe reads.

For supporting diagnostic retention, see [remote processor logging](../RemoteSystemLogging.md). Syslog collection is separate from functional observations and does not replace them; verify delivery and the diagnostic streams covered before using it as evidence.

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

After extracting the complete console bundle into `cli`, copy its entire `scripts/endurance` directory to the separate `scripts` directory shown above. The archive does not create this sibling directory for you. Run this once, before protecting and pinning the inputs:

```powershell
$base = 'C:\Private\Endurance\candidate-a'
if (Test-Path -LiteralPath "$base\scripts") {
    throw 'The scheduler script directory already exists. Review it before proceeding.'
}
Copy-Item -LiteralPath "$base\cli\scripts\endurance" `
    -Destination "$base\scripts" -Recurse
```

Use the copied scripts for configuration creation and task registration below. Running configuration creation directly from `cli\scripts\endurance` selects a tick script inside `cli` and is rejected with `Keep inputs and configuration outside CLI, run and scheduler state directories.` Keep the original bundle intact; configuration creation pins the complete CLI bundle and the separate tick script. Do not replace either copy during an active run.

If you transfer this layout to another Windows PC, also create the empty `run` and `scheduler-state` directories there. A transfer that uploads only files can omit both directories. For SFTP, verify the destination as the server presents it: a Windows server that lists `C:` under `/` exposes paths such as `/C:/Private/Endurance/candidate-a`. Do not try to create the drive component as a directory. If a transfer fails, inspect the destination and compare any uploaded files with the intended bundle before resuming; do not overwrite an existing run.

Keep a probe's inventory or review metadata outside its published directory unless it is itself part of the pinned inventory. If the probe uses a fixed baseline file, acquire it during authorized preparation, include it in the final probe inventory, and then make the probe directory read-only to the worker account. Run the service-account preflight against that final bundle; do not grant the worker permanent write access to its executable directory to capture a baseline.

The scheduler runs as Windows **LocalService**, without an interactive desktop or saved Windows login password. Before registration, an administrator must protect these directories: administrators and the owner may administer them; LocalService needs read/execute access to the CLI, scripts, probe and input files, and modify access only to `run` and `scheduler-state`. Remove access for unrelated users. Do not grant the worker permission to rewrite its scripts, candidate, criteria or file manifests.

DevTools 1.17.2 adds an optional preparation helper for this step. Run it on the monitoring PC, after staging the files and baseline but **before** starting a run. First preview the dedicated directory, then explicitly apply the reviewed change from an administrator PowerShell session:

```powershell
& "$base\scripts\Set-EnduranceDirectoryPermissions.ps1" -RootDirectory $base
& "$base\scripts\Set-EnduranceDirectoryPermissions.ps1" -RootDirectory $base -Apply
```

The helper preserves full access for the root directory's owner, Administrators and SYSTEM. It replaces other access rules with LocalService read/execute on inputs and modify access only on the two output directories. Existing files receive file-level rules in the same operation that removes inherited access; they are not left with empty permissions. New files inherit their directory's intended access. The result reports whether it applied and verified the rules. Preview performs no changes.

Use only a dedicated new monitoring directory: both output directories must already exist and be empty. The helper refuses used runs, escaped or overlapping output paths, system/profile roots and reparse points. It neither copies credentials nor changes permissions outside the chosen root, registers a task, or contacts a processor. If connection settings are deliberately stored outside this root, separately authorize and verify their service access. For a separate observer directory, set `-RunDirectory journal -StateDirectory state` to match its empty output folders. Do not apply the helper to a running collector or a directory shared with unrelated applications.

An administrator may instead prepare equivalent permissions using normal Windows tools. Always verify a real invocation under LocalService before relying on collection or alerts; inspecting access rules is not a substitute for testing access to the actual processor and saved inputs. Configuration and registration scripts do not change permissions automatically.

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

## Configure the PC for automatic restart

Configure each monitoring PC, including a development PC that also monitors, before starting the observation period:

- Keep the Windows Task Scheduler service running and available at startup. Leave each registered endurance task enabled. The supplied registration script uses a startup trigger, a one-minute repeating trigger and `StartWhenAvailable`; it runs as LocalService without an interactive login. It allows battery operation and does not stop a running task merely because AC power is lost.
- In Windows power settings, disable automatic sleep and hibernation while plugged in for the monitoring period. The display can turn off and the desktop can be locked. Check the applicable battery and lid-close settings for a laptop, and keep reliable power connected. The task permits battery execution but cannot prevent the computer from sleeping or losing power. Registration does not change the machine's power plan or firmware wake settings.
- Keep the reviewed inputs, credentials and mutable run state on persistent local storage accessible to LocalService after startup. A saved desktop login, mapped drive or interactive credential prompt must not be needed. Ensure network access and any probe dependencies become available in time for the approved observation gap.
- Schedule Windows updates outside the observation period where practical; do not disable security updates indefinitely. A startup trigger restarts monitoring, but it cannot preserve missing observations or guarantee continuity through every reboot.
- Rehearse an actual PC restart using a separate short validation run before relying on the configuration for a submission candidate. Verify that the task runs without login, advances the existing run's observations and retains its identity and ownership. Also verify that an interrupted probe or excessive gap produces an attention/failure record rather than a new silently started period.

Repeat task registration with unique task names and separate worker/run/state files for any number of independent runs. There is no two-task limit. Size the workload so all probes can meet their deadlines, and respect processor and shared-device reservations. Do not assign the same run to multiple monitoring computers.

Automatic recovery means the scheduler starts the existing monitor again and lets it validate the saved run. A consistent run interrupted between completed attempts may continue automatically within its approved gap. An unfinished or inconsistent attempt restarts into an attention state for inspection; it cannot truthfully be resumed as successful evidence merely because Windows has restarted. See the interruption rules below.

## Results, interruptions and alerts

Each invocation has a retained intent, child process identities, private stdout/stderr and a completed result under `scheduler-state/attempts`. `scheduler-state/status.json` is atomically replaced after a completed attempt. These are operational records, not submission evidence; only the collector's revalidated export can enter the evidence pipeline.

For routine monitoring while the task is enabled, read the completed `scheduler-state/status.json` and `attention.json` snapshots and their timestamps. Do not poll `endurance-status` concurrently: although it does not contact the processor, it acquires the monitor journal exclusively and can collide with the scheduled collector. A saved snapshot can be stale or describe the preceding completed attempt; combine it with Task Scheduler state and the permitted observation gap. Never treat a snapshot alone as proof that a process is alive.

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

### Passive health assessment

DevTools 1.12.0 and later include `Get-EnduranceHealthSnapshot.ps1` and `endurance-health`. They provide an operational health assessment for an independent observer. They do not send notifications, schedule the observer, restart a task, change a reservation, or establish submission evidence. Use the separate [observation and notification commands](EnduranceNotifications.md) when operational alerts are needed.

Run the snapshot script on the monitoring PC through your authenticated monitoring connection:

```powershell
./scripts/endurance/Get-EnduranceHealthSnapshot.ps1 -TaskName 'Crestron-Endurance-CandidateA' -StateDirectory 'C:\Private\CandidateA\scheduler-state'
```

Retain its JSON output privately on the observer and assess it with the trusted worker plan:

```powershell
./CrestronHomeDevTools.Console.exe endurance-health --worker C:\Private\CandidateA\worker.json --snapshot C:\Private\CandidateA\health.json --max-status-age-seconds 300 --clock-tolerance-seconds 2
```

The observer needs the worker plan, but no processor credentials or producer installation. It checks the current task state, completed scheduler receipt, latched attention, collection identity, sample chronology, maximum gap, boot identity and terminal cleanup. The script retries inconsistent task-status reads at most three times because Windows exposes task state and result through separate queries. Persistent inconsistency remains a fault. No collector journal is opened.

Exit 0 reports `Collecting` or `Completed`; exit 3 reports attention or an unreadable observation; exit 2 reports malformed input or options. Treat **any nonzero exit, failed remote observation, timeout or absent fresh snapshot as an alert condition**. Do not replace an old observation's timestamp with the time it was copied. `Completed` is an operational signal only: still export and validate the retained evidence. Stop monitoring a completed run only after reviewing and retaining its terminal result; its last snapshot will otherwise eventually become stale.

The C# equivalent is `SubmissionEnduranceHealth.Evaluate`. Integrators provide a `SubmissionEnduranceHealthSnapshot` from their own trusted observer. A remote connection failure can be represented with `WorkerReachable = false`; never reuse a previous reachable result after a failed connection. On a one-PC setup, an external heartbeat receiver is needed to detect loss of that PC. A second PC is optional. Notification routing and delivery verification remain the monitoring integration's responsibility.

Current source also provides an optional [SMTP notification API and CLI](EnduranceNotifications.md) with persistent duplicate suppression and explicit handling of uncertain delivery. It needs an authorized destination and independent observer; installing it alone does not establish delivered alerts.

For a built-in local or remote Windows observation path, use `endurance-observe` as described in that guide. Its snapshot reader is embedded in the library; no separate private observation program or transferred script is needed. Remote Windows SSH setup and trusted credentials remain explicit prerequisites.

## Retain a completed run

DevTools 1.11.0 and later include `scripts/endurance/Export-EnduranceScheduledRun.ps1` in the complete console archive. It can consume an existing pinned scheduler configuration without changing the scheduled script, task, CLI or worker plan. Supply the original tick script explicitly if its bytes differ from the script beside the exporter. Do not replace pinned files during collection.

```powershell
& 'C:\Tools\Export-EnduranceScheduledRun.ps1' `
    -Configuration 'C:\Private\Endurance\candidate-a\schedule.json' `
    -ConfigurationSha256 'INDEPENDENTLY_RETAINED_SCHEDULE_SHA256' `
    -TickScript 'C:\Private\Endurance\candidate-a\scripts\Invoke-EnduranceScheduledTick.ps1' `
    -OutputDirectory 'C:\Private\Evidence\candidate-a-completed'
```

Use a new destination under an existing private parent with suitable access permissions. The script does not set ACLs. It acquires the existing scheduler lock, checks the configuration, original tick script, worker and complete CLI inventory, then invokes only offline `endurance-status` and `endurance-export`. It requires a passed collection and confirmed reservation release. Lock contention returns 4 without launching the CLI; other refusals return 3. It never starts, resumes or finishes a collection, contacts a processor, or disables/deletes a task.

The snapshot retains the worker/configuration, original tick script, run files and scheduler history, including previously reconciled incidents. Lock files are excluded from source copying. It does not copy the external connection or probe settings files, probe binaries or CLI bundle. The copied worker still records its original paths and identities; keep the matching tool/probe inventories separately. Raw probe evidence and diagnostic output can still contain sensitive information, so keep the whole snapshot private.

Before writing `complete.json`, the script independently reads and exports the copied journal through the pinned CLI, requires an identical exported observation, checks unchanged source inputs/evidence, and inventories the retained files. A failure keeps partial files and private diagnostics, with no completed receipt. Inspect a failed or interrupted invocation before retrying; do not overwrite its directory or terminate it while its child process is running. Protect the completed receipt and its digest separately when transferring the snapshot. Hashes establish content identity, not producer authentication.

This is a completed **collection snapshot**, not a validated full submission bundle. It preserves the original collection policy and scope. Final functional tests, any reviewed derivation into the full submission policy, the official form, signing and submission approval remain separate. Retire the exact scheduled task separately after reviewing the terminal result and successful retention.

## Validation and remaining deployment checks

The snapshot script has 16 synthetic scenarios, originally validated under Windows PowerShell 5.1 and revalidated under PowerShell 7.6, covering successful retention, incomplete/failed/unreleased collections, malformed replies, export failures, changed source/copy evidence, different exports, altered pins, extra CLI files, attention records, lock contention and an unsafe nested destination. The passing case also checks preserved incident history, the file inventory and refusal to overwrite an existing snapshot. A separate two-second synthetic journal passed export and copied-journal revalidation with the released 1.7.0 CLI. That fixture fabricated monitor ownership and used synthetic function samples; it did not acquire a real reservation, contact hardware or establish candidate endurance acceptance.

The wrapper originally had Windows PowerShell 5.1 checks; the current source runs these under PowerShell 7.6, covering normal collection, completion, failure, incomplete/unknown ownership, malformed output, inherited connection overrides, changed files, overlapping invocations and process-tree termination. Additional cases exercise empty, unavailable, null and structurally invalid status responses before and after a tick. These now retain a specific before/after status failure reason and the original output without a secondary property-access exception, and remain latched without replay. This response-handling change is included in 1.11.0; do not replace pinned tooling during an active run. These tests do not contact a processor or count toward an endurance requirement.

A separate Windows monitoring computer has also run a credential-free synthetic rehearsal under LocalService. Two completed invocations continued the same journal without calling `endurance-start`. In the interruption case, the wrapper alone was terminated after its child started; the child survived. A second invocation returned `AttentionRequired`, retained the unfinished attempt and made no additional collector call. The identified synthetic child was then stopped and both manual-only rehearsal tasks were removed. This verifies service-account process recovery and no replay, not a Windows reboot, delivered alert or real-device acceptance. The active endurance task was not modified by that rehearsal.
Before a real run, validate the registered task under its actual service account, the protected credential files, independent alerts, an operating-system restart between observations, interruption during an observation, permitted sample gaps and shared physical-device coordination. A processor reservation coordinates cooperating operations on that processor; it does not reserve a physical device shared with another processor. Complete the full approved duration against the exact final candidate after those deployment checks pass.

To retire a reviewed terminal task, use `Unregister-ScheduledTask -TaskName 'Crestron-Endurance-CandidateA' -Confirm`. Unregistering does not release a processor reservation or delete retained evidence. Check known cleanup separately before archiving private files.
