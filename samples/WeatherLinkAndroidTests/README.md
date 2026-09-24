# WeatherLink app evidence fixture

This C# NUnit project uses the released public NUnit Android APIs and DevTools
configuration API. It is an opt-in hardware fixture for the separate
`InstalledAppTests` stage of the automation controller. Ordinary test discovery
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
`installed-app/AndroidUI` evidence directory. It rejects a different directory
layout, processor, device, package or release commit.

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
`installed-app/AndroidUI/weather-observations.json` in `Review.ObservationSources`.
It uses the `weather` coverage surface and the requirement IDs from the
WeatherLink coverage mapping. Review those IDs against the actual pinned policy.
The controller retains the observations, masked hierarchies, screenshots and
allowlisted API display comparisons automatically. It does not retain all
configuration properties, which could contain provider credentials.

This fixture does **not** supply configuration-dialog, changed-setting feedback,
outage, power, multiple-instance, removal or endurance observations. Those need
their own producers and applicability decisions. It cannot by itself complete
an official checklist. A no-key/no-forecast variant requires its own fixture
and mapping; do not reinterpret this fixture's missing forecast as N/A.

Validation: source builds and synthetic input/navigation/matching checks are
available in the main DevTools test project. The generalized fixture's fresh
hardware validation remains pending; historical one-off WeatherLink tests do
not establish that this new fixture passed.

Copyright (c) 2026 Neil Colvin. MIT licensed.
