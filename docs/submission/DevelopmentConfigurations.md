# Development computers and test processors

One Windows development computer and one compatible Crestron Home processor are the baseline configuration for these tools. A second computer, a second processor and a particular private orchestration repository must not be prerequisites. Additional hardware provides isolation and availability. The complete submission workflow still requires end-to-end validation; this guide describes the existing components and the integration requirements for each layout.

Ordinary library/client tests and driver development do not require submission tooling. Offline tests and package/document preparation can run without a processor. Hardware and submission requirements still need their actual observations; unavailable equipment cannot be reported as a passing test.

## Choose a layout

| Available equipment | Where work runs | Scheduling consequence |
| --- | --- | --- |
| One PC, one processor | Builds, local tests, processor workflows, optional Android emulator and endurance scheduler share the PC. | Reserve the processor for one workflow at a time. During endurance, continue local work but postpone deployments and competing hardware tests. |
| One PC, several processors | The same PC targets explicitly configured processor identities. | A candidate can remain frozen on one processor while another is used for development. Shared physical devices and Android instances still need coordination. |
| Several PCs, one processor | Monitoring or CI may run on a separate computer. | Every cooperating client must honor the same processor reservation; moving monitoring to another PC does not make the processor available for competing work. |
| Several PCs, several processors | Monitoring, development and CI can be separated. | Bind each run to its intended processor, candidate, device and evidence directory. Extra hardware does not remove shared-device conflicts. |

Use the same public C# APIs, console commands and reviewed plans in every layout. Supply addresses, processor trust, credentials and paths through private configuration. No developer should need our private repository, machine names or network layout. The local commands can be used without a GitHub Actions runner; protected workflow templates are an optional orchestration layer.

There is no two-computer or two-processor limit in this layout. For three or more, repeat the per-run configuration: a unique task name, reservation ID, worker plan, run directory and scheduler-state directory, bound to the intended processor. Keep each active run assigned to one monitoring computer. Do not start a second collector for that same run on another PC as an improvised failover mechanism. Separate runs may share an immutable tool installation, but their mutable journals and task state must remain separate. Capacity is limited by actual CPU, storage, network, processor connection limits and probe deadlines, not by a fixed pair of machines.

One PC can schedule independent runs for multiple processors. Different PCs targeting the same processor still contend for the same reservation. Shared physical devices require coordination even when processor reservations differ; a second processor does not make simultaneous control tests on the same device independent.

## Concurrent CI from different computers

Several repositories or developer PCs may trigger work simultaneously. Coordinate actual resources, not just the submitting machine or repository:

| Resource | Current boundary and requirement |
| --- | --- |
| GitHub runner | One runner process executes one assigned job at a time. Multiple runner installations, scheduled monitors and desktop commands on the same PC can still overlap; runner scheduling is not a processor or device reservation. |
| Processor | DevTools and NUnit cooperating clients use the same processor-hosted workflow reservation. It covers cooperating clients on different PCs and repositories and is retained across endurance samples. Competing work waits within its configured budget or reports busy; it must not remove the owner to proceed. |
| Android app session | Workflows reserve their configured Android session lock. All clients controlling that session must use the same authoritative lock; independent local lock files on different PCs do not coordinate one remotely accessible emulator. |
| Physical hub or device | Processor reservations do not coordinate access to the same physical device through different processors, or a desktop live test that bypasses the processor. General shared-device reservation is a deferred enhancement, not a prerequisite for layouts without that overlap. If this topology is used, explicitly serialize those tests through one trusted coordinator. |
| Private evidence, signing and delivery state | Give independent runs separate directories. The supplied submission-stage concurrency group serializes its repository only; protect shared files and provider journals across any additional callers. Never start duplicate delivery to get around a pending job. |

Reserve the resources protected by the workflow before its first side effect, and hold the reservations through verified restoration and cleanup. Shared-processor coordination across PCs and repositories is the immediate requirement. A future multi-resource coordinator should acquire resources in a consistent global order, avoid deadlocks, preserve ownership through disconnects and restarts, and refuse to steal an uncertain reservation on a timeout. This lower-priority enhancement is not a claim that general shared-device orchestration is already shipped.

Only runs with independent resource sets may overlap. A read-only observer can still be affected by another run changing the device it observes. A concurrency group in GitHub is scoped to its repository and does not protect against a desktop command or another repository; see [workflow setup](WorkflowSetup.md) for the template's narrower scheduling role.

## A processor that also controls a home or office

Treat this as an explicitly shared operational environment when reviewing the workflow plan. This is deployment guidance, not a new automatic profile switch. Keep unrelated installed drivers, configuration and catalogue packages intact. Target cleanup only at test instances and artifacts whose ownership the run has established; never empty the catalogue to make room for a test.

Identify every physical device a live test will operate. Preserve and verify its original state, and obtain authorization for any disruption that the test requires. Prefer read-only observations where they satisfy the test's actual purpose. Restoration does not undo the temporary effect on occupants while heating, lighting or another service was changed.

Reboots, simulated outages and driver removal/update require an explicit maintenance decision. A V1 driver update may require a processor reboot affecting the whole installation; a V2 update can still interrupt that driver's functions. Neither should happen just because an unrelated background job found an available runner. Postpone disruptive coverage when no suitable maintenance window is available and report the missing coverage honestly.

Normal household use continues while a CI reservation exists: the lease does not block occupants, schedules or unrelated control applications. Avoid competing development changes during candidate endurance. The approved observation policy should distinguish normal use from changes that compromise the candidate, its observed device or evidence continuity. Do not silently ignore interference, and do not invalidate unrelated normal operation without a policy reason. Automated restoration also needs conflict handling if an occupant changes the same settings during a test; serializing CI alone cannot solve that problem.

A dedicated processor is convenient for disruptive tests, but it is optional. A shared installation may require more deliberate scheduling and may be unable to complete particular submission tests without a planned interruption.

## A test processor connected to a device in real use

Processor usage and physical-device usage are independent dimensions of a test profile. A dedicated development processor can connect to a hub that still controls the occupied home or office. Reserving that processor does not make the physical device a laboratory fixture or prevent its schedules, occupants or other controllers from changing it.

| Processor use | Physical-device use | Consequence |
| --- | --- | --- |
| Dedicated to testing | Dedicated to testing | CI can use the reviewed test equipment within its declared operating limits. |
| Dedicated to testing | Serving the home or office | Device-control tests still affect real users. Bound the affected devices and settings, coordinate disruptive actions and check restoration against intervening changes. |
| Serving the home or office | Dedicated to testing | Processor lifecycle operations may interrupt unrelated real services even though the test device is isolated. |
| Serving the home or office | Serving the home or office | Apply both sets of restrictions. A CI reservation does not suspend normal operation. |

Record both dimensions in the reviewed environment/test plan, together with the permitted target devices, operations and maintenance windows. This profile does not require a second physical device. Read-only endurance should observe normal operation without overriding schedules or occupants. Control tests should change only the approved target and fields, verify current state before acting, and retain what they actually changed.

Classify the smallest independently controlled target, including individual outlets, hub children, sensors and channels. A parent device can contain both dedicated test children and children in normal use. Dedicated test targets do not inherit household-use restrictions merely because other targets on the same processor or parent are operational. Identify them by stable device/child identity rather than an address or label alone.

Apply operational restrictions according to the action's actual scope. Switching one dedicated test outlet can be unrestricted while another outlet on the strip remains operational. Resetting, powering off, unpairing or reconfiguring their shared parent may affect all children, so review that broader action against the operational children as well. A child-level test designation does not authorize disrupting the shared parent. Reading an operational sensor is also different from resetting the hub that carries its readings.

An optional naming convention can make that classification easier. For example, a developer may designate devices whose names contain `Demo` as dedicated test equipment. The developer chooses the pattern and matching rules; it is not a universal convention and existing devices need not be renamed. A hub or strip designated as test equipment supplies that classification to all its children by default, making them available for testing without renaming each child. Explicit device/child classifications take precedence over a name rule or inherited designation, so an explicitly operational child remains operational. A target with neither its own classification nor an inherited one remains unclassified; lack of a name match alone does not prove either usage.

Use the rule to discover and review candidate targets, then retain their stable parent/device/child identities in the test plan. Do not silently substitute a new device merely because it later acquires the same name. A parent-wide action still accounts for affected operational children. This is an optional integration convention for consumer configuration; the shared workflow templates do not currently implement automatic device-name classification.

Restoration must account for legitimate changes made after the initial snapshot. Check whether the fields to be restored still match the state produced by the test before writing them back. A detected conflict needs an explicit recovery decision and retained evidence; neither blindly restoring the old snapshot nor reporting successful restoration without verification is acceptable. Devices without comparable state or conditional updates may need a coordinated maintenance window rather than an automatic restoration promise. This is an acceptance requirement for the relevant fixtures, not a claim that every existing fixture already implements conflict handling.

This operational-device profile is distinct from two independent CI runs contending for the same device. Cross-processor CI resource coordination remains a deferred enhancement; protecting normal household or office operation is part of the current live-test requirements.

## One computer and one processor

1. Build and run offline tests locally. Prepare and pin the Release candidate before submission testing.
2. Run the processor and applicable Android tests under the shared resource reservation, restoring physical device settings after control tests. The emulator can run on the development PC; a read-only endurance probe does not need it unless that probe explicitly observes the app.
3. Start the reviewed endurance run and use the [Windows scheduler](WindowsEnduranceWorker.md) on that same PC. Keep the PC powered, awake and connected, with sufficient resources for the probe to finish within its approved observation interval. The scheduled worker does not require a logged-in desktop.
4. Leave the processor and candidate unchanged for the required period. Local editing, builds and offline tests can continue. Wait for the reservation to be released before running competing deployments, installs, removals, updates, reboots or hardware tests. A processor reservation is cooperative: Configure/Setup and unrelated scripts must also be kept from changing the environment.
5. Retain the completed evidence, run the required post-period functional checks and continue the review/signing/delivery stages. Elapsed time alone does not establish submission readiness.

A CI job that cannot acquire the processor must follow its configured bounded wait or report that hardware is unavailable. It must not delete an existing reservation, silently select another processor or mark unexecuted tests as passed. Hardware availability remains separate from ordinary build and publication policy; submission evidence remains mandatory for submission.

## Sleep, updates and interruptions

The driver continues running on the processor when the PC is unavailable, but the PC cannot collect observations while asleep, powered off or restarting. Automatic task startup does not fill that gap. Schedule Windows updates outside the observation period where possible, and retain all interruption records.

The existing collector can resume between completed samples only when the original identities, reservation and approved maximum gap still hold. An unfinished probe requires inspection. A failed or unprovable interval remains failed; retain it and start a new reviewed run when required. Do not loosen the observation policy retrospectively. See [restart and failure handling](EnduranceCollection.md#restart-and-failure-handling).

A separate monitoring PC reduces interruptions caused by development-PC restarts. It is optional. With only one PC, local task results and attention files are available when that machine is running; immediate notification of its own power or network loss requires an independent observer and is not guaranteed by the worker itself.

## Multiple instances and physical equipment

Multiple driver instances do not necessarily require multiple processors. Where the driver, processor and applicable test requirement permit it, two independently installed instances on one processor can provide that topology. Record what was actually exercised, including shared physical devices. Two managed children, sequential reinstalls and two independent gateway instances are different scenarios; none should silently substitute for another.

If a particular test genuinely requires additional equipment that the developer does not have, report that requirement as uncovered or document permitted non-applicability with its reason. The tools must not impose extra hardware merely because the reference environment has it, or claim that every official requirement can be met with every hardware layout.

## CI credentials on a shared PC

The normal developer workflow is Windows-based: Visual Studio/.NET, C# tests and documented configuration/console commands. It must not require Linux knowledge or an interactive Linux shell on the processor. The tools handle SSH/SFTP and processor operations internally. Supplying processor credentials is separate from learning Linux administration; any low-level diagnostic instructions are optional troubleshooting. Internal hosted jobs may use Linux or Python without requiring the consuming developer to program or administer either.

Keeping development and monitoring on the same computer does not require exposing submission credentials to source-build jobs. Protect private inputs and evidence with separate account permissions where needed, and restrict privileged jobs to reviewed code and artifacts. Never execute untrusted pull-request code under an account with access to processor credentials, signatures or delivery credentials. A second physical computer is one isolation option; it is not the only one. Account isolation does not protect against local administrators.

Before relying on unattended operation, validate startup, reservation contention, interruption handling and credential access in the chosen layout. Successful tests in a multi-computer environment do not by themselves validate the complete single-computer submission workflow.
