# CrestronHomeDevTools 1.3.0

Add room inventory and guarded movement of a loaded driver between existing rooms.

- `locations` lists configured room IDs and names.
- `move --device ID --model NAME --version VERSION --from-room ID --room ID` moves a matching childless driver and verifies its unchanged identity and loaded version.
- The library exposes `GetLocationsAsync`, `MoveDriverInstanceAsync`, `ProcessorLocation` and `DriverRoomMoveResult`.
- Reboot-required drivers, managed children, ambiguous destinations and changed identity are refused. The CLI holds the shared processor reservation and retains uncertain outcomes for inspection.
- CI and release validation compare executed test identities with discovery instead of maintaining a fixed test count.
- Enforce the numeric room-ID format required by the move command. Installation uses a different string-valued format; interchanging them can remove an instance.

Hardware validation moved a temporary Entity V2 test instance between two rooms and back on an MC4-R, including a fresh authenticated connection. Instance ID and loaded version were preserved. The temporary instance and CI archive were removed afterwards. No actual installed driver was moved.

See [room moves](docs/RoomMoves.md), [protocol reference](docs/ProtocolReference.md), [CHANGELOG.md](CHANGELOG.md) and [third-party notices](THIRD-PARTY-NOTICES.md). The library requires .NET 10 and a V2 Crestron Home processor. This is unrelated to driver V1/V2 naming. No Crestron SDK is required.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.
