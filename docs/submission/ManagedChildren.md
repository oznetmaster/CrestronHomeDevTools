# Persistent managed children in a submission run

Source preview: this route has offline regression coverage. The integrated route
still needs processor/app validation before an unattended rehearsal can establish
that it works for a particular driver. It does not establish Crestron acceptance.

Platform drivers often need installed children throughout app testing and endurance.
Use the optional `ManagedDevices` automation setting with `NUnit.ActualDriver`,
`NUnit.ReleaseCandidate` and the separate `InstalledAppTests` route. Do not combine
it with `NUnit.AndroidTests`: that workflow's temporary managed-child tests remove
their children after testing.

The worker obtains the platform ID, model and version from this run's verified
deployment receipts after processor tests. Each child selection supplies a unique
`Alias`, exact advertised `ManagedDeviceId`, installer `Name`, expected `Model`,
`LocationId`, `Configuration` dictionary of string-valued installer inputs, and
`RequiredCommands` array. For an initial wizard, leave `Configuration` empty and
supply ordered `ConfigurationSteps` (`Id` and `Values`) instead. The public
configuration API verifies the advertised step IDs and writable items.
`TimeoutSeconds` defaults to 120 (maximum 600).

For example, a privately configured temperature child might use:

```json
{
  "ManagedDevices": {
    "TimeoutSeconds": 120,
    "Children": [{
      "Alias": "temperature",
      "ManagedDeviceId": "the-exact-advertised-managed-id",
      "Name": "Demo Temperature",
      "Model": "expected-model",
      "LocationId": 123,
      "Configuration": {},
      "ConfigurationSteps": [{
        "Id": "Activation", "Values": { "ActivationMarker": "true" }
      }],
      "RequiredCommands": ["extension:doCommand"]
    }]
  }
}
```

Select actual advertised commands for the driver; the example is not universal.
The worker records creation before configuration, applies the reviewed inputs only
when initial configuration is required, and checks current readiness and required
controls. Inputs on an already configured child must match readable current settings;
unverifiable or differing settings do not silently pass. Merely reporting online
and ready does not satisfy a missing required control. API readiness is still not
proof that a tile renders correctly: the Android tests remain mandatory.

Successful children stay installed. `managed-devices.json` pins the private setup
journals and returned identities, including the native load below a lighting
wrapper. App tests, endurance, and review continue only after setup completes and
its processor reservation is released. The initial installed-app target is also
bound to the deployment receipts.

## Use actual child identities

Use exact string placeholders in `InstalledAppFixtureSettings`, optional
`PostEnduranceFixtureSettings`, and a deployment-bound endurance probe template:

* `${managed:temperature:deviceId}` becomes the created child's numeric ID.
* `${managed:light:nativeLoadId}` becomes the native light's numeric load ID.

Placeholders may appear in nested objects and arrays. Unknown aliases, embedded
placeholders, and requesting a native load for an ordinary child are errors. Other
values are preserved. Endurance templates still need the existing
`${deployedDeviceId}` and `${deployedCatalogueId}` bindings.

Final removal can also use actual IDs. Set `ManagedAlias` on an `App.Tiles` entry;
its reviewed name and room must match commissioning. `NativeLight: true` selects
the native load rather than its wrapper. List wrappers or other explicitly
nonvisual children in `App.NonvisualManagedAliases`, and use
`App.IncludeDeployedPlatformAsNonvisual` when the platform itself has no tile.
Concrete `DeviceId` values in aliased tile templates are replaced before observation.
The observer then checks that the complete selected installation is accounted for;
unresolved aliases cannot reach the observer. No child is silently omitted.

## Interrupted or failed setup

The worker records its lease owner and per-child creation/configuration journals
under `managed-devices/`. An interrupted call with no terminal outcome stops for
inspection, even when the scheduler invokes the stage again. It does not replay
commissioning, reset evidence, remove children, or release an uncertain reservation.
Terminal failed readiness remains a failure; successful earlier children remain
recorded. Inspect the original error and lease before explicit recovery.

Successful setup receipts are verified on continuation. A changed identity,
different deployment, altered journal, or changed reviewed plan cannot be used to
resume the earlier attempt. Ordinary temporary test cleanup and final opt-in driver
removal retain their separate responsibilities.

Keep concrete selections, installer inputs and evidence in the private workflow
store. This generic guide does not belong in an individual driver's release notes.
