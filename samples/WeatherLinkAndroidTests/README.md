# WeatherLink app evidence fixture

This C# NUnit project uses the released public NUnit Android APIs and DevTools
configuration API. It is an opt-in hardware fixture for either the separate
`InstalledAppTests` stage or the combined `NUnit.AndroidTests` deployment route. Ordinary test discovery
does not connect to equipment. The fixture must not be counted as an offline test
pass when it is skipped outside that stage.

Select this project in `InstalledAppTests.AndroidTests.Project`, include this
checkout in `SourceRoots`, and set `RequiredTests` to
`WeatherLinkAndroidTests.WeatherPagesTests.HomeCurrentConditionsForecastActionAndRestoration`.
Configure a normal private Android session profile and the exact installed
release candidate. The controller and NUnit coordinator own the processor and
Android reservations; the fixture does not install or replace drivers.

Add this factual object to the private release settings template:

```json
"InstalledAppFixtureSettings": {
  "ProcessorHost": "selected-processor.example",
  "DeviceId": 12345,
  "TileName": "Selected WeatherLink station",
  "CredentialBindings": "C:/PrivateEvidence/processor-bindings.json",
  "Identity": {
    "PackageSha256": "${packageSha256}",
    "SourceCommit": "${commit}",
    "PolicySha256": "reviewed-policy-sha256",
    "TemplateSha256": "official-template-sha256"
  }
}
```

Replace all illustrative data and pins. The binding must resolve only the named
processor credential with its trusted HTTPS certificate. No mail or signing
credential is needed. The fixture reads the controller's retained
`app-fixture-settings.json` two levels above the coordinator's
`installed-app/AndroidUI` evidence directory. It also accepts the combined
`nunit/AndroidUI` directory. It rejects any other layout, processor, device, package
or release commit.

For a fresh release deployment, select the project and required test in
`NUnit.AndroidTests`, configure the frozen `NUnit.ActualDriver` and
`NUnit.ReleaseCandidate`, and leave `InstalledAppTests` null. Set `DeviceId` to `0`
to use the instance ID passed by the deployment coordinator. A positive ID must
still match exactly. Zero is rejected by the separate installed-app route. The
controller writes the same fixture-settings file before starting the combined
workflow and retains it with the NUnit evidence; no manual ID edit is needed.
To bind later endurance to that same deployment, use the controller's
`EnduranceFromDeployment` option and the WeatherLink producer's deployment
placeholders. See the [worker guide](../../docs/submission/AutomationWorker.md).

The initial implementation targets the English WeatherLink two-page UI with a
configured forecast. It checks the Home tile, all current display fields, the
Next Week Forecast action, all seven forecast rows and attribution, both close
transitions, and restoration to Home. The forecast action can request online
weather; it does not change installer settings or operate physical equipment.
Display values are read immediately before and after each app capture. Matching
is scoped to the correct row and visible bounds, allowing the app's capitalization.
An empty source-detail line is allowed only on an observed Source row. This is
display correlation, not an assessment of the forecast provider's accuracy.

Navigation uses public `AdbCommandTransport`, `AndroidDevice`, session context
verification and front-page hierarchy scoping. The published high-level nested
helper is Room-specific, while this driver's tile is Home-only. The small
navigation helper therefore computes input coordinates from freshly observed
controls. It never retries an uncertain tap; cleanup waits for its pending
transition before sending any further input. Navigation failure retains evidence
and reports restoration honestly. Each page scan is limited to 16 viewports.

On success, include
`installed-app/AndroidUI/weather-observations.json` in `Review.ObservationSources`
for the separate route, or `nunit/AndroidUI/weather-observations.json` for deployment.
It uses the `weather` coverage surface and the requirement IDs from the
WeatherLink coverage mapping. Review those IDs against the actual pinned policy.
The controller retains the observations, masked hierarchies, screenshots and
allowlisted API display comparisons automatically. It does not retain all
configuration properties, which could contain provider credentials.
The fixture writes the public camel-case observation schema and derives capture
and restoration references from the coordinator's actual evidence directory.
Composition regression tests cover both output layouts and retain nonpassing
outcomes rather than converting them to passes.

The Home capture also needs a visual icon/arrow review before the complete Home
placement requirement can be asserted; this fixture does not auto-check that item.
This fixture does **not** supply configuration-dialog, changed-setting feedback,
outage, power, multiple-instance, removal or endurance observations. Those need
their own producers and applicability decisions. It cannot by itself complete
an official checklist. A no-key/no-forecast variant requires its own fixture
and mapping; do not reinterpret this fixture's missing forecast as N/A.

Validation: source builds and synthetic input/navigation/matching checks are
available in the main DevTools test project. The separate installed-app route
passed in the 24 September hardware rehearsal. The new deployment-to-app route
has offline binding tests; its fresh hardware validation remains pending.

Copyright (c) 2026 Neil Colvin. MIT licensed.
