# CrestronHomeDevTools 1.17.0

DevTools can now collect private inputs once and reuse named encrypted entries across processor commands, monitoring, signing and delivery. Windows setup and resource assessment help prepare one or more development and monitoring computers.

- Add Windows DPAPI private stores for processor, Windows, SMTP and uploader logins and signature images. Purpose, endpoint and trust checks bind each use. Provision selected entries to restricted service stores or another Windows computer over pinned SSH; do not copy user-encrypted files between accounts or machines.
- Add `credentials verify` for an access check under the actual service identity. Stored signatures feed both signing commands through a pipe without decrypted temporary images; exact-form signing approval remains required. Existing processor profiles and protected stdin integrations remain supported.
- Add scheduled endurance observation with named credential bindings, persistent notification journals, startup/resume support, input pins and overlap protection. The observer does not modify or restart its collector. Authorize the SMTP destination and test delivery before relying on alerts.
- Add named resource inventories and read-only Windows workload assessment. Planned resources cannot be selected. Recorded roles, installed software and a logged-in desktop are distinguished from verified capabilities and representative workload results.
- Add reviewed OpenSSH prerequisite setup with restart handling, and new GitHub runner/service setup with pinned archives, private registration-token input, collision checks and inspection after uncertain outcomes. Existing runners are not replaced, and registration is not automatically replayed.

See [private inputs](docs/PrivateInputs.md), [Windows resources](docs/WindowsResources.md), [OpenSSH setup](docs/WindowsSetup.md), [GitHub runner setup](docs/WindowsRunnerSetup.md) and [endurance notifications](docs/submission/EnduranceNotifications.md).

Validation includes the discovered .NET suite, isolated console signing/delivery acceptance checks, synthetic scheduler processes, a real two-computer synthetic credential transfer and a LocalService access-isolation rehearsal. The new installers have not yet completed a real clean-machine OpenSSH installation/reboot or new GitHub runner registration/service installation. Those limits are explicit in their guides. A completed local service setup does not establish GitHub online status, desktop access, workload readiness or Crestron acceptance.

This release does not change a driver or automatically migrate credentials, install services, change desktop logon, sign documents or send messages. Keep a running collector on its pinned tooling; install a new observer bundle separately.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.
