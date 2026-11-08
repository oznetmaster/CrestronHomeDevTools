# Published driver search

`published-drivers` queries the public [Crestron Drivers portal](https://drivers.crestron.io/) independently of any processor. It requires neither a processor address nor credentials. Installed or sideloaded copies cannot hide a published result.

```powershell
.\CrestronHomeDevTools.Console.exe published-drivers --search "Neil Colvin"
.\CrestronHomeDevTools.Console.exe published-drivers --search "Somfy Tahoma"
```

The JSON includes the source, retrieval time, total catalogue **model-entry** count and matching records. A package can have multiple model entries; this count is not a package count. Search is case-insensitive, and every whitespace-separated term must occur in at least one of model, manufacturer, version, category, communication type or developer. This is DevTools' explicit search behavior, not a claim to reproduce Configure Pro's search rules. Manufacturer-only entries with an empty model name are retained as returned.

Only available, non-beta records are returned. “Published” includes self-certified drivers; it does not mean every driver is Crestron-certified. The general portal does **not establish Crestron Home compatibility**. Its 3-Series/4-Series platform labels do not answer that question. Do not treat a portal match as proof that Home can install a driver or use its features.

The public portal 4.0.58 currently returns its complete model array from the observed `driver/findMany?isBeta=0` request even though the request includes `first=0, rows=50`. DevTools validates each reported total and the complete row-number sequence before applying search. A truncated or changed response fails instead of returning an apparently complete subset. Network/validation failure never falls back to the processor's merged local catalogue. This is an observed interface, not a published API contract.

For comparison, `drivers --search TEXT --processor HOST --credentials BINDINGS` uses that processor's merged catalogue, including local imports. Its result limit is **200**, confirmed in Configure Pro 4.12.174's `DriverSearchConstants.MaxDriverResultsLimit`. An unfiltered response of 200 entries is not a complete inventory. `LocalByUser` alone does not prove that an equivalent driver is unpublished. Likewise, `IsExtensionDevice` on a root platform does not classify its managed children.

On 31 October 2026, a controlled .241 comparison removed the five submitted production packages and the Wiser/WeatherLink configured test instances, refreshed local entries and fully rebooted the processor. Four products then appeared as remote entries; Overkiz was absent. The local/cloud manifests and original packages were retained privately. This is a dated observation of that catalogue, not a permanent publication assertion. Private configuration, account information and evidence do not belong in public search output.

Library usage:

```csharp
var result = await new PublishedDriverCatalogue().SearchAsync("Somfy Tahoma", cancellationToken);
```

An optional caller-owned `HttpClient` supports testing or explicit network configuration. Default requests are anonymous and do not retain cookies. No download, installation, refresh, removal or reboot occurs during a published search.
