# CrestronHomeDevTools 1.17.4

Protected JSON input now works when Windows PowerShell 5.1 supplies a UTF-8 byte-order marker. Previously, the same saved-credential import or remote endurance observation could succeed from PowerShell 7 and fail before making a connection from Windows PowerShell 5.1. Redirected input is now decoded as UTF-8, preserving non-ASCII credential values, and the private JSON readers accept one leading marker.

The correction covers credential import, runner setup, endurance observation/watch/notification and both submission delivery commands. Existing input bounds, private-buffer cleanup, endpoint checks, evidence checks and exact signing/delivery approvals are unchanged. It does not change driver behavior or require restarting an active endurance run.

Validation includes the actual credential-import executable with marked and unmarked UTF-8, non-ASCII synthetic credentials, encrypted-store verification, and command tests for observation, notification and delivery. The original Windows PowerShell 5.1 reproduction now succeeds using dummy data. No real credentials or external providers were used by these regression tests.

See [reusable private inputs](docs/PrivateInputs.md). Use the updated console for new setup or observation commands; leave active collectors on their pinned tools.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.
