# CrestronHomeDevTools 1.17.2

Preparing a new Windows endurance monitor no longer requires writing a custom folder-permission script. The console ZIP includes the optional `Set-EnduranceDirectoryPermissions.ps1` helper: preview the dedicated directory, then use `-Apply` to make the reviewed change.

The helper preserves full access for the directory owner, Administrators and SYSTEM; LocalService receives read/execute on inputs and modify access only to the empty run and scheduler-state directories. It handles pre-existing files as well as future inherited permissions, removes unrelated access, and verifies the resulting rules. Preview makes no changes. Used runs, escaped or overlapping output paths, system/profile roots and reparse points are refused before mutation.

Six permission checks pass on Windows PowerShell 5.1 and PowerShell 7, including inherited broad-user removal, unchanged file contents, new-file access and refusal behavior. These checks do not impersonate LocalService or contact a processor; validate the real scheduled invocation in the consuming environment.

See [Windows endurance worker setup](docs/submission/WindowsEnduranceWorker.md). The helper does not copy credentials, install services, contact a processor or change an active collector. Keep existing runs on their pinned tools. No driver runtime behavior changes.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.
