# CrestronHomeDevTools 1.4.0

Add opt-in removal of a V1 driver instance when other installed instances share its reload scope.

- `DriverRebootHandler.AdditionalRemovalRebootDeviceIds` explicitly lists the existing instances to preserve and requires `RebootAfterRemoval`.
- The processor's affected scope must match exactly. Shared instances must have the expected model/version and be Loaded; only the selected instance receives the removal command.
- After the authorized Home configuration reboot, verify disappearance of the selected instance and preservation of the other instances' identities, rooms, versions, configured state and reported configuration items.
- Default removal remains restricted to a single instance. Unknown or expanded scope, lost responses and uncertain recovery do not trigger retries or automatic broader removal.

Apple TV V1 initial installation and removal were verified on a development MC4-R. Startup verification was resumed after a timeout without repeating installation or reboot; subsequent removal and its configuration reboot preserved all original devices, removed the temporary instance and released the reservation. This does not establish pairing or playback behavior. No CP4-R validation was repeated for this change.

See [V1 installation and removal](docs/V1DriverRemoval.md), [compatibility](docs/Compatibility.md), [CHANGELOG.md](CHANGELOG.md) and [third-party notices](THIRD-PARTY-NOTICES.md). The standalone CLI's ordinary remove command remains reboot-free; advanced orchestration uses the library or NUnit workflow.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.
