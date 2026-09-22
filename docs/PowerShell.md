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
