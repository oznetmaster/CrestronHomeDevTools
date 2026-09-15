# Moving a driver between rooms

The source library and console support moving a loaded, childless driver instance between existing rooms. The operation preserves its instance ID, model, name and version, and verifies the new room and loaded state. It does not remove/reinstall the driver or change its configuration values.

Only drivers advertising reboot-free lifecycle support are accepted. Drivers with managed children, duplicate names in the destination, changed identity, or an unexpected current room are refused. This first implementation does not rearrange child devices or move V1 drivers requiring a reboot.

## Console and CLI

Use the same commands at the interactive `ch>` prompt or as CLI arguments:

```text
locations
devices
move --device 12345 --model "Example Driver" --version 1.0.0.1 --from-room 41001 --room 41002
```

Replace the example IDs with current inventory values. The CLI uses the selected processor profile, takes the shared processor reservation, submits the move once and waits for verification. A lost reply or unconfirmed result keeps the reservation for inspection; it does not repeat the command. Reconnect and inspect `devices` before deciding on recovery.

## Library

`ConfigurationClient.GetLocationsAsync()` returns room identities. `MoveDriverInstanceAsync(deviceId, expectedModel, expectedVersion, expectedLocationId, destinationLocationId, timeout, cancellationToken)` performs the guarded move and returns `DriverRoomMoveResult`. A request for the already-current room verifies the instance without submitting another command. Library consumers must hold a shared processor reservation around their complete operation; the console does this automatically.

The tested configuration command is `cp.deviceConfiguration:setLocation` with a **numeric** `locationId`. This differs from `commissionDevice`, whose installation request uses a string. Do not interchange the formats: a string sent to `setLocation` was treated as removal on the development processor. The typed helper and regression tests enforce the numeric format.

## Validation

A temporary Entity V2 processor-test instance was moved between two rooms on an MC4-R running Home 4.11.322, then moved back through a fresh authenticated connection. Both directions preserved instance ID and loaded version. The temporary instance and its CI archive were removed afterwards. Actual installed drivers were not moved. The interface remains unofficial and firmware-dependent; see the [protocol reference](ProtocolReference.md).
