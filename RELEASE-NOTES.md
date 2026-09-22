# CrestronHomeDevTools 1.18.0

Windows automation now requires PowerShell 7.6 or later. Resource assessment, OpenSSH and runner setup, remote Windows credential transfer and endurance observation use PowerShell 7 and reject older engines before performing their operation. New endurance scheduled tasks use the PowerShell 7 executable that registers them.

Install PowerShell for all users on each Windows automation host before upgrading. The standard MSI installation places it in `C:\Program Files\PowerShell\7\pwsh.exe`; remote Windows accounts also need `pwsh.exe` on their command path. See [installation and migration](docs/PowerShell.md). Windows PowerShell 5.1 is no longer a supported automation engine for this release. Processor configuration APIs themselves do not require PowerShell.

Local endurance observation now passes its readable script directly to PowerShell instead of using the compressed remote-command wrapper. Its integration test requires an actual task-status result, so a blocked process cannot satisfy a missing-task check.

Leave active endurance runs on their recorded tooling and task configuration until they finish. Installing the prerequisite does not migrate a running task, and this release does not require repeating earlier validated endurance evidence.

Validation covers version refusal before execution, Windows setup/observation/credential-transfer regressions and the scheduler, notification, export, health and directory-permission scenarios on PowerShell 7.6. These checks use synthetic inputs and do not operate processors or send email.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.
