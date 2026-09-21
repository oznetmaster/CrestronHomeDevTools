# Set up a Windows GitHub Actions runner

This helper requires DevTools 1.17.0 or later. It installs a new Windows x64 runner as a service. It does not alter existing runners, grant repository permissions, configure GitHub environments, provision private credentials, or make a desktop session available.

## Prepare the computer and GitHub destination

Assess the intended combination of tasks using [Windows resources](WindowsResources.md). Choose a dedicated new runner directory, the repository or organization that may send it jobs, labels and the Windows service account. A service starts without an interactive sign-in; Configure Pro and other Windows desktop tests still need a separately verified logged-in desktop. Labels alone do not isolate credentials or authorize jobs.

In GitHub, open the destination's **Settings > Actions > Runners > New self-hosted runner**, then select Windows x64. Use the runner version and SHA-256 from GitHub's official download instructions. The setup helper constructs the official `actions/runner` release URL from that version and checks the archive before extracting or executing anything. Organization runner groups and access restrictions must be configured separately; this helper uses the default group.

Prepare a plan on the intended computer. Replace the version/hash placeholders with the official values; this command creates only a review file:

```powershell
.\CrestronHomeDevTools.Console.exe resources prepare-runner --url https://github.com/OWNER/REPOSITORY --name development-worker --version A.B.C --archive-sha256 OFFICIAL_SHA256 --directory C:\Runners\Development --account "NT AUTHORITY\NETWORK SERVICE" --labels development,processor-tests --output C:\Private\runner-plan.json
```

An organization URL is also supported. GitHub Enterprise Server URLs and ARM64 are not supported by this first helper. Choose a shorter runner name if the resulting Windows service name would exceed 80 characters. Review the printed target, account, labels, download URL, service name and plan digest. The GitHub configurator grants the chosen account service-logon and runner-folder access. Choose a separate identity and access boundaries for signing or delivery jobs when required; setup does not grant those private capabilities.

## Apply the reviewed plan

In an elevated terminal on the same computer:

```powershell
.\CrestronHomeDevTools.Console.exe resources apply-runner --plan C:\Private\runner-plan.json --sha256 REVIEWED_PLAN_DIGEST --state C:\Private\runner-setup-state --apply-reviewed true
```

The console prompts without echoing for the short-lived registration token from GitHub, and for a service-account password if the selected account needs one. Obtain a fresh token shortly before applying. Tokens are not command-line arguments or saved in the setup journal. For automation, supply protected JSON on redirected standard input with `RegistrationToken` and optional `ServicePassword`; do not place it in a repository or a shell command containing the secret. The child process uses GitHub's supported secret-input environment variables. There is no fixed GitHub token-length assumption.

Keep the state directory outside the new runner directory. Download/extraction is bounded to ten minutes and configuration to five. Existing directories or conflicting Windows services stop setup; `--replace` is never passed. The installation and GitHub's private diagnostics remain in the selected directory after an error so the result can be inspected. No automatic rollback, reboot or removal of a GitHub registration occurs.

- **Exit 0 / Completed:** the local runner configuration matches the selected repository/name and its service has the expected executable, account, automatic startup and running state. This does not prove GitHub considers it online or that jobs work.
- **Exit 3 / InspectionRequired:** inspect the retained journal, Windows service and GitHub runner list. A previous attempt may already have registered a runner. Running the same plan re-observes it, but never replays registration. If it is fully configured after a restart, the observation can complete the journal.
- **Exit 2:** input, access or setup failed. Retain the journal and inspect before recovering. Do not repeatedly generate fresh attempts to bypass an uncertain registration.

If manual recovery is necessary, use GitHub's removal instructions for that specific runner and review any local cleanup before preparing a fresh attempt. Never delete an unrelated runner directory or service. Existing runner updates remain managed by GitHub's normal updater.

Use `resources inspect-runner --plan C:\Private\runner-plan.json` for a read-only local check, without credentials, journal changes or a GitHub connection. Finally confirm the runner is **Idle/Online** at the selected GitHub destination and run a representative job. Check required SDKs, device connectivity, processor locking, private-store permissions and cleanup under the actual service account. Record those observed capabilities in the resource inventory only after they succeed. A completed installation is not workload validation.

## Public API and validation

C# callers use `WindowsRunnerSetupPlan`, `WindowsRunnerSetup.Digest` and `WindowsRunnerSetup.ApplyAsync`, passing `WindowsRunnerSetupSecrets` in memory. `IProgress<string>` reports stages. `IWindowsRunnerSetupOperations` separates setup orchestration from vendor operations for testing.

Current validation covers synthetic registration/service observations, collision refusal, interruption recovery without registration replay, archive hashing/path checks and secret handoff. A real new runner registration/service installation with this helper has **not yet been validated**. Previously installed runners do not establish that evidence. No developer needs Python or Linux commands to use this helper.

Sources: [GitHub runner setup](https://docs.github.com/en/actions/how-tos/manage-runners/self-hosted-runners/add-runners), [Windows service configuration](https://docs.github.com/en/actions/how-tos/manage-runners/self-hosted-runners/configure-the-application), [runner secret-input handling](https://github.com/actions/runner/blob/main/src/Runner.Listener/CommandSettings.cs), [Windows service installer](https://github.com/actions/runner/blob/main/src/Runner.Listener/Configuration/WindowsServiceControlManager.cs).
