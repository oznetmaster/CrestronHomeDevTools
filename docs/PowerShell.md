# PowerShell for Windows automation

DevTools **1.18.0 and later** require **PowerShell 7.6 or later** for Windows resource assessment, prerequisite setup, remote Windows operations and the supplied endurance scheduling scripts. Install it on each Windows computer that performs those operations. The .NET library's processor configuration APIs do not independently require PowerShell.

Windows PowerShell 5.1, included with Windows, is a different application. It does not satisfy this prerequisite. These automation paths use `pwsh.exe` and check the version before running their operation; there is no fallback to 5.1.

## Install for all users

Use Microsoft's machine-wide MSI installation, including on computers that run scheduled tasks or GitHub runner services. In an administrator terminal:

```powershell
winget install --id Microsoft.PowerShell --source winget --installer-type wix
```

Alternatively, download the appropriate MSI from [Microsoft's PowerShell installation instructions](https://learn.microsoft.com/en-us/powershell/scripting/install/install-powershell-on-windows). Keep the standard installation location, `C:\Program Files\PowerShell\7\pwsh.exe`. A per-user Microsoft Store/MSIX installation does not provide the machine-wide executable used by these tools.

Verify the installed version:

```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoProfile -Command '$PSVersionTable.PSVersion.ToString()'
```

For remote Windows operations, `pwsh.exe` must also be available on the remote account's PATH. Existing services may retain an older environment until restarted. Arrange any necessary service restart when no job is running.

## Existing endurance runs

Keep an active endurance run on its recorded tooling and scheduled-task configuration until it finishes. This new prerequisite does not invalidate evidence collected with an earlier supported release. Apply the new setup to subsequent runs; do not replace scripts or change the task's shell during an active run.

## Processor connections and SSH callbacks

PowerShell is an orchestration shell, not a substitute for the documented compiled processor client. Prefer the supplied console or compiled public API runner for processor authentication, configuration and reservation recovery. Keep the exact validated executable, host, account, trust settings and dependency hashes in the run record. A PowerShell version check alone does not validate a new in-process client invocation.

Do not attach a PowerShell script block to an SSH.NET asynchronous `HostKeyReceived` callback. The callback can execute on a thread without a PowerShell runspace and fail before the lease or device is read. Use the maintained compiled callback or public client, comparing the expected SSH fingerprint exactly. Never suppress verification to work around the callback failure. An existing validated PowerShell script that invokes a compiled callback is distinct from a script-block callback; retain its established route rather than rewriting it during a submission.

An HTTP certificate failure is a separate problem. Record the full exception chain and distinguish WebSocket authentication from the subsequent HTTPS session-validation request. Inspect the effective HTTPS port reported by authentication, not just the initial options. Compare the actual target, ports, observed certificate fingerprints, approved stored pins, runtime, library hashes and execution host with the last successful public invocation. Keep secrets and authentication responses private. A compiled console succeeding while an in-process invocation fails does not alone prove a PowerShell defect, bad credentials or a changed certificate. Do not replace pins, relax TLS checks or repeatedly retry a mutation on that inference.

When a known-good compiled route restores connectivity, label that as an operational correction until the deeper cause is established. Preserve the failed attempt and keep permanent diagnostic and support follow-through open. Validate read-only connectivity through the chosen route before the corrected mutation; retain the exact reviewed mutation's ownership and cleanup guards.
