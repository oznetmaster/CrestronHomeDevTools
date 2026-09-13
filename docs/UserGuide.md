# Console and CLI guide

## Contents

- [Starting the console](#starting-the-console)
- [Profiles and credentials](#profiles-and-credentials)
- [Commands](#commands)
- [Deploy and activate](#deploy-and-activate)
- [Reviewed updates and removal](#reviewed-updates-and-removal)
- [Outputs and exit codes](#outputs-and-exit-codes)
- [Troubleshooting](#troubleshooting)

## Starting the console

Run the built `CrestronHomeDevTools.Console` executable without arguments for interactive setup and a `ch>` prompt. From source, use `dotnet run --project CrestronHomeDevTools.Console`. `help` describes each command; `exit` closes the prompt. Quote names and file paths containing spaces.

Supplying arguments executes one command and exits. For example:

```powershell
dotnet run --project CrestronHomeDevTools.Console -- discover
dotnet run --project CrestronHomeDevTools.Console -- devices --profile development
```

A framework-dependent published console requires .NET 10. A self-contained release includes its runtime; use the complete matching release directory, not an executable copied alone.

## Profiles and credentials

`configure --profile development` discovers processors, prompts for selection and credentials, verifies the certificate and SSH host key, then saves an encrypted profile. The default profile is named `default`. Credentials are per profile/processor, not assumed to be shared across the network. Saved system names are resolved to current addresses on subsequent use. `--processor` accepts an explicit IP address or exact discovered system name; duplicate names are rejected.

Windows DPAPI protects the entire saved profile, including its credentials, host and fingerprints, for the current Windows account. Profiles live under `%LOCALAPPDATA%/CrestronHomeDevTools/Profiles`. Run configure again to edit a profile; an empty password entry retains its saved password. To forget a profile, remove that profile file. For a verified certificate change, remove the old profile and configure it again. Copying encrypted profiles to another machine/account is not a credential-transfer method.

CLI commands do not prompt for missing trust or silently accept a new key. Supply noninteractive credentials from an appropriate private store:

| Environment variable | Meaning |
|---|---|
| `CRESTRON_HOME_HOST` | Processor address when not selected explicitly. |
| `CRESTRON_HOME_USER` | Processor username. |
| `CRESTRON_HOME_PASSWORD` | Processor password. |
| `CRESTRON_HOME_CERT_SHA256` | Verified HTTPS/WebSocket certificate SHA-256 fingerprint. |
| `CRESTRON_HOME_SSH_FINGERPRINT` | Verified SSH.NET SHA-256 host-key fingerprint for SFTP. |

`--settings` accepts a private JSON file with `host`, `userName`, `password`, `certificateSha256`, `sshFingerprint`, `httpsPort` and `webSocketPort`. The default ports are 443 and 49000. Environment values override file values. Select a matching profile instead of changing its host while retaining another processor's credentials. There is no password argument, and the HTTPS certificate fingerprint and SSH host-key fingerprint are different values.

## Commands

| Command | Action |
|---|---|
| `discover` | Discover local Home processors without credentials. |
| `configure [--profile NAME]` | Save a selected processor and encrypted credentials. |
| `drivers [--search TEXT]` | List catalogue packages and catalogue IDs. Narrow the search if results are limited. |
| `devices` | List installed devices, instance IDs and room IDs. Shows IDs, names, models and room IDs; omits full device properties. |
| `eligibility --driver ID` | Inspect versions, eligible installed instances and reboot/swap support. |
| `plan-update --driver ID --output FILE` | Save the proposed update scope for review. No processor change. |
| `deploy --package FILE` | Upload/import a package and confirm catalogue availability. No instance update. |
| `activate --driver ID --name NAME --room ID [--device ID]` | Install if absent, upgrade if older, or verify a matching current instance. |
| `update --plan FILE` | Recheck and apply the reviewed update to all eligible instances in the plan. |
| `refresh` | Refresh the catalogue from staged import files. |
| `reload-scope --device ID` | Inspect the affected device scope for a reload. |
| `reboot [--processor TARGET --confirm-reboot TARGET]` | Reboot the whole processor with interactive or explicit target-matched unattended authorization. |
| `reload --device ID` | Request a supported reboot-free targeted reload. |
| `remove --device ID --model NAME --version VERSION` | Remove only the matching instance after identity and dependency checks. |
| `help` / `exit` | Show usage / leave interactive mode. |

Common options are `--processor`, `--profile`, `--settings` and `--timeout`. The operation timeout defaults to 120 seconds. Catalogue IDs belong with `--driver`; installed instance IDs belong with `--device`. Room/location IDs are neither of those.

## Deploy and activate

```text
deploy --package "C:/CI/Artifacts/Example.Driver.pkg" --profile development
drivers --search "Example" --profile development
activate --driver CATALOGUE_ID --name "Example Driver" --room ROOM_ID --profile development
```

Replace placeholders with reviewed inventory values. `deploy` inspects the root manifest and assembly, calculates the package SHA-256, uploads a temporary file by SFTP, renames it into `/user/ThirdPartyDrivers/Import`, requests import and waits for the expected catalogue identity/version. It refuses an existing staged filename instead of overwriting it.

Catalogue import is asynchronous. `activate` waits for the required catalogue/eligibility/Loaded state; an upload completing is not proof an installed instance has changed. Instance names are limited to 32 characters. With `--device`, the model/name/room must still identify the intended instance. Without it, ambiguous matches are rejected. Automatic activation refuses a downgrade or an update affecting other instances. Missing targets can be installed initially or after a prior removal.

Changed package contents require a new version. Identical version strings cannot prove identical code. Numeric comparisons preserve the fourth Debug component while tolerating zero-padding: `2.0.000.0005` and `2.0.0.5` match; `2.0.0.6` is different.

## Reviewed updates and removal

Use `plan-update` when applying the processor's update operation to a reviewed group of eligible instances. The plan records versions and IDs; `update` rechecks those values before submitting. A changed scope, missing eligibility or reboot requirement closes the operation. Keep the plan private.

Removal checks the exact ID, model, version and affected dependencies, then waits for the instance to disappear. The catalogue package remains available for a later install. Save test results and confirm no test is running before removing a processor test host. DevTools does not know NUnit execution state; the higher-level workflow owns that decision. Do not infer completed removal merely because a tile vanished or its room assignment was cleared.

## Outputs and exit codes

One-command inspection/operation results use JSON on stdout; diagnostics use stderr. Interactive setup/help are human-readable. The CLI waits on the same authenticated session for submitted operations.

| Exit | Meaning |
|---:|---|
| 0 | Success, with the command's required confirmation. |
| 1 | Processor or operation error. |
| 2 | Invalid arguments, configuration or settings. |
| 3 | Timeout or unconfirmed outcome. |
| 130 | Cancellation. |

An event saying `Ended` may not establish success. Deployment independently verifies catalogue availability; update/activation verify the installed version and Loaded state. Refresh/reload can return an unconfirmed outcome if there is no adequate success evidence. Cancellation and connection loss do not undo a change already accepted by the processor. Inspect current state before a new attempt; submitted changes are never automatically replayed.

## Troubleshooting

| Symptom | Check |
|---|---|
| Processor absent from discovery | Local UDP discovery, subnet/firewall reachability and the anonymous Home V2 HTTPS endpoint; try an explicit address. |
| Authentication/key validation fails | Correct per-processor profile, credentials and independently verified HTTPS/SSH pins. Do not disable validation. |
| Uploaded package cannot yet be updated | Allow catalogue import and eligibility to finish; check the exact four-part version. |
| Existing staged filename | Inspect the prior import outcome before retrying; do not overwrite blindly. |
| Update rejected | Recreate/review the plan if versions or affected devices changed. A required reboot is unsupported here. |
| Removed tile but instance remains | Inspect inventory and live logs; room unassignment alone is not completed removal. |
| Logs seem old | Saved processor logs can lag; use a live SSH console log stream during diagnosis. DevTools itself does not implement a log-tail command. |

Return to [README](../README.md), [API guide](LibraryGuide.md) or [compatibility details](Compatibility.md).

## Confirmed processor reboot

At the interactive `ch>` prompt, enter `reboot`. The console connects using the selected processor's credentials and verified SSH host key, then displays its name and resolved address. To proceed, type exactly `REBOOT <displayed-address>`; any other answer or end of input cancels without sending a reboot command. All Home programs and drivers on that processor will be interrupted.

For unattended use, including redirected input, supply both `--processor TARGET` and `--confirm-reboot TARGET`. Values must match (case-insensitive), and missing/mismatched confirmation is rejected before discovery or authentication. A generic `--yes` is not accepted. The selected processor must still match its verified SSH host key. Supplying `--confirm-reboot` explicitly also suppresses the interactive prompt.

The standalone command acknowledges submission; it does not wait for the processor to finish starting. The NUnit development workflow adds reboot recovery and version verification through its own `allowProcessorReboot` policy. Ordinary DevTools `activate`, `update`, `remove` and `reload` commands retain their default reboot-free policy; use the workflow for the full V1 cycle.

The command reports Cancelled, Accepted or Unconfirmed. Accepted means the processor acknowledged the reboot request, not that it has finished starting. If the connection closes, a timeout occurs or cancellation arrives after submission, the outcome can be Unconfirmed (exit code 3). No automatic retry is made. Check processor availability before deciding what to do next. Cancellation before submission sends nothing.
