# Assess and select development computers

These commands and APIs require DevTools 1.17.0 or later. The inspection and selection commands are read-only. Separate reviewed [OpenSSH setup](WindowsSetup.md) is available; inspection does not install prerequisites, register runners, log on users or reserve resources.

## Inspect a Windows computer

Run the complete console on the computer you want to assess. Choose an existing local directory on the disk that will hold the work:

```powershell
.\CrestronHomeDevTools.Console.exe resources inspect-windows --work-directory C:\Work --output C:\Private\host-snapshot.json
```

The output file must be new. The report contains machine identity, observation time, CPU models and logical processors, total and currently available memory, free space on the chosen work disk, firmware/hypervisor virtualization indications, installed .NET SDK directory versions, and SSH/GitHub runner service state. It contains no credentials. Keep it private because it identifies your computer and paths. Run a fresh inspection when choosing a host; an old report does not establish current capacity.

The public C# entry point is `WindowsHostAssessment.ReadLocalAsync(workDirectory, cancellationToken)`. The console carries its own Windows inspection script; developers do not need to write PowerShell, Python or Linux commands. Inspection is bounded to 45 seconds and terminates its child process on timeout.

## Assess a combined workload budget

Pass a private requirements JSON file with `--requirements FILE`. For example, the following is an **illustrative operator budget**, not a vendor requirement or a performance guarantee:

```json
{
  "Name": "Build and Android UI work",
  "MinimumLogicalProcessors": 4,
  "MinimumAvailableMemoryMiB": 8192,
  "MinimumAvailableDiskMiB": 40000,
  "RequiredDotNetSdkMajor": 10,
  "RequiresVirtualization": true,
  "RequiresDesktop": true,
  "RequiresSshService": true,
  "RequiresGitHubRunnerService": true
}
```

Use the combined memory/disk budget for tasks intended to run together. Record representative build and emulator measurements before choosing those values for unattended use. Leave requirements false or omit them when not needed: a standalone monitor need not have an Android emulator, Visual Studio or a GitHub runner.

The report separates missing prerequisites from unverified operational checks. Exit 0 means the inspection succeeded and any declared prerequisites passed; exit 3 means declared prerequisites remain missing or unverified; exit 2 means inspection/configuration failed. Even a passing prerequisite report requires a representative workload rehearsal. A .NET SDK directory does not prove that every project dependency, .NET Framework targeting pack, Crestron SDK or Build Tools component is available. Test the actual solution build before recording build capability.

## Desktop and headless use

A computer may have no physical monitor and still have a logged-in desktop accessible through remote-control software. That arrangement must be tested with the actual UI applications. This assessment deliberately leaves `DesktopUsable` unknown: a logged-in account is not proof that the desktop is unlocked or accessible to the automation worker.

Service/session-zero execution does not establish desktop automation capability. Verify Configure Pro under the intended interactive account, after disconnecting the remote-control client, and after restarting Windows. Verify emulator acceleration and ADB in the intended account as well. Do not grant readiness merely because a runner service or emulator executable exists. Automatic logon and its credential/security choices are explicit setup decisions; this command does not enable it.

## Keep a named inventory

`DevToolsResourceInventory` holds one or many Windows computers and Crestron processors. Each resource has `Planned`, `Ready` or `Disabled` state, permitted roles, capability evidence references, optional private credential-entry names, and a flag for equipment shared with real use. Save/load it using the public C# API. It has no passwords. Names and evidence are operator-maintained; a recorded assertion is not an independent verification.

```powershell
.\CrestronHomeDevTools.Console.exe resources check --inventory C:\Private\resources.json
.\CrestronHomeDevTools.Console.exe resources select --inventory C:\Private\resources.json --kind WindowsComputer --role ConfigureProUi --capabilities configure-ui,desktop-recovery --name ui-worker
```

Capability names are your inventory's names for retained rehearsal evidence. Planned or disabled resources are never selected. Multiple matches require a name. Selection is read-only and does not contact the resource. The calling workflow must still acquire its shared resource reservation, apply real-use restrictions, and confirm that the recorded evidence remains applicable. Desktop automation jobs must not compete for the same interactive session.

The inventory CLI does not yet route existing workflow commands automatically. Selected-entry [cross-computer provisioning](PrivateInputs.md#transfer-one-entry-to-another-windows-computer), reviewed [OpenSSH setup](WindowsSetup.md) and [GitHub runner setup](WindowsRunnerSetup.md) have separate commands. Other tool installation and automatic desktop-session recovery setup remain pending; do not treat inspection as a completed machine installer. The setup guides state which installation paths still need real-machine validation.
