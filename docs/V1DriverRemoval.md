# V1 installation and removal with shared driver code

A V2 Crestron Home processor can run V1 drivers. Driver generation and processor generation are separate. These instructions concern V1 driver lifecycle operations on a supported V2 processor.

Initial installation and removal can require a Home configuration reboot. Set `RebootAfterInstall` and `RebootAfterRemoval` on a `DriverRebootHandler` only for a reviewed driver/firmware combination. Use `ConfigurationClient.RequestProcessorRebootAsync`, followed by fresh authenticated recovery and version/identity verification. A plain SSH reboot does not provide the same configuration-save sequence.

Home can report several installed instances of the same V1 driver in `GetReloadAffectedDevicesAsync`. From DevTools 1.4.0, a removal handler can explicitly list the other existing instances in `AdditionalRemovalRebootDeviceIds`. The default is empty and still refuses shared scope.

```csharp
var handler = new DriverRebootHandler(saveAuthorizedIntent, rebootAndReconnect)
{
    RebootAfterRemoval = true,
    AdditionalRemovalRebootDeviceIds = [existingInstanceId]
};
await client.RemoveDriverInstanceAsync(
    temporaryInstanceId, expectedModel, expectedVersion,
    TimeSpan.FromMinutes(15), cancellationToken, handler);
```

The two callbacks are application-specific: the first validates processor-specific authorization and records durable intent; the second requests one configuration reboot and returns a fresh authenticated connection. Hold the shared processor reservation for the whole operation and verification. An acknowledged reboot is not completed recovery.

The additional IDs are instances to **preserve**, not remove. The reported scope must exactly equal the selected removal ID plus the explicit list. Additional instances must have the same model and version and be Loaded before submission. Only the selected instance receives the removal command. After restart, it must be absent and the additional instances must retain their IDs, names, parents, rooms, versions, configured state and reported configuration items, and return to Loaded. Missing or changed instances prevent success. The library does not infer IDs or automatically broaden the list.

This option does not authorize arbitrary dependency removal, V1 rollback or a reboot after a timeout. Lost command responses remain uncertain and are never replayed. The standalone CLI's ordinary `remove` command remains reboot-free; use the library lifecycle handler or the NUnit workflow's explicit reboot policy for this advanced sequence.

On the development MC4-R, an additional V1 driver instance was installed from the existing catalogue and survived a Home configuration reboot. A startup inventory timeout required read-only verification to resume, without repeating commissioning or reboot. Removal using the reviewed shared scope and a second configuration reboot preserved the existing V1 driver and all other original devices. The temporary instance was absent afterward and the reservation was released. No new archive or actual-driver update was required. This establishes lifecycle behavior, not pairing or playback.
