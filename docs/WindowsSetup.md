# Reviewed Windows prerequisite setup

This feature requires DevTools 1.17.0 or later. This page covers Windows OpenSSH Server. A separate [reviewed GitHub runner setup](WindowsRunnerSetup.md) also requires 1.17.0. Build-tool and emulator installation remain separate steps; these commands do not prepare an entire workstation.

## Prepare and review

Use [resource assessment](WindowsResources.md) to decide what the computer needs. Run this on the intended computer; it writes a new plan and prints its digest and proposed changes without changing Windows:

```powershell
.\CrestronHomeDevTools.Console.exe resources prepare-ssh --remote-address LocalSubnet --output C:\Private\ssh-setup.json
```

Choose a single source IP instead of `LocalSubnet` if appropriate. The plan is tied to this computer's name. Review that the selected source includes the computer from which you intend to manage it, especially if already connected over SSH.

The plan installs Windows OpenSSH Server if missing, starts `sshd` and sets automatic startup. It configures the DevTools SSH firewall rule and scopes Windows' standard `OpenSSH-Server-In-TCP` rule when that rule exists. Other existing rules are preserved and can allow additional access. Third-party firewalls still need separate configuration.

## Apply and resume

In an administrator terminal on the same computer, use the digest printed for the plan you reviewed:

```powershell
.\CrestronHomeDevTools.Console.exe resources apply-ssh --plan C:\Private\ssh-setup.json --sha256 REVIEWED_DIGEST --state C:\Private\ssh-setup-state --apply-reviewed true
```

Progress and state are recorded before each change. Windows feature installation can take several minutes and is bounded to 30 minutes. Cancellation or timeout is not proof that Windows servicing rolled back.

- **Exit 0 / Completed:** capability, automatic running service and firewall settings were observed locally. Test SSH from the other computer before recording remote-access capability.
- **Exit 3010 / RestartRequired:** restart Windows at a suitable time, then run the same apply command with the same plan and state directory. It observes the new boot and continues remaining steps. The tool never reboots automatically.
- **Exit 3 / InspectionRequired:** inspect the retained state and Windows servicing. An uncertain installation is not automatically replayed. After resolving the cause, explicitly add `--resume-after-inspection true` to the same command.
- **Exit 2:** configuration, access or execution failed. Inspect the retained state before retrying; changes may already have occurred.

A machine-wide exclusion lock and journal lock prevent cooperating setup instances from overlapping. Repeated execution rechecks Windows instead of trusting an old Completed record. Keep the same state directory for one plan. Setup does not change passwords, disable host verification, configure automatic desktop logon or register GitHub runners.

The C# APIs are `WindowsSshSetupPlan`, `WindowsSshSetup.Digest` and `WindowsSshSetup.ApplyAsync`. Supply `IProgress<string>` for progress. The library bundles its script; developers need not edit PowerShell or know Linux commands.

Validation currently covers simulated Windows operations: restart/resume, interrupted installation, changed-plan refusal, current-state checks and failed installation observation. Script syntax is checked. A clean-machine installation and real reboot have **not yet been validated with this new helper**. Existing separately configured SSH access is not evidence that this installer succeeded.

## Other workstation prerequisites

Install only what the intended tasks need. Builds need the .NET SDK and project-required Build Tools, targeting packs and vendor SDKs; the full Visual Studio IDE is optional. GitHub runners require authorization for the chosen repository/organization, a short-lived registration token and a deliberate service identity. Android tests require emulator acceleration and ADB verification. Windows UI automation requires an appropriate logged-in desktop and testing after remote-control disconnect and restart. An automatically running service does not establish interactive desktop capability.

Sources: [Microsoft OpenSSH setup](https://learn.microsoft.com/en-us/windows-server/administration/openssh/openssh_install_firstuse), [Windows feature installation troubleshooting](https://learn.microsoft.com/en-us/troubleshoot/windows-server/system-management-components/cant-install-openssh-features), [GitHub runner setup](https://docs.github.com/en/actions/how-tos/manage-runners/self-hosted-runners/add-runners), [GitHub runner service configuration](https://docs.github.com/en/actions/how-tos/manage-runners/self-hosted-runners/configure-the-application).
