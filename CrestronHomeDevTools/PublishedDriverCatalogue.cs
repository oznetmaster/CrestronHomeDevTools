// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Net.Http.Json;
using System.Text.Json;

namespace CrestronHomeDevTools;

/// <summary>A published model entry, independent of processor installation and sideloaded packages.</summary>
public sealed record PublishedDriverInfo (int Id, int ModelId, string ModelName, string Manufacturer,
	string DriverVersion, string DeviceType, string CommunicationType, string Developer,
	string PlatformsSupported, bool IsExtensionDevice, string Certification, bool LicenseRequired)
	{
	public string HelpUrl => $"https://drivers-api.crestron.io/help/driver/{Id}";
	}

/// <summary>The portal catalogue covers Crestron Drivers generally; it does not attest Home compatibility.</summary>
public sealed record PublishedDriverSearchResult (string Source, DateTimeOffset RetrievedUtc,
	int TotalCatalogueModelEntries, string? Search, IReadOnlyList<PublishedDriverInfo> Drivers)
	{
	public string Scope => "Crestron published non-beta catalogue; Crestron Home compatibility is not established by this source.";
	}

/// <summary>Read-only search of the public portal's catalogue, with no processor or account credentials.</summary>
public sealed class PublishedDriverCatalogue
	{
	public const string Source = "https://drivers.crestron.io/";
	private static readonly HttpClient SharedHttp = new (new HttpClientHandler { UseCookies = false })
		{ Timeout = TimeSpan.FromSeconds (60), MaxResponseContentBufferSize = 32 * 1024 * 1024 };
	private readonly HttpClient _http;
	public PublishedDriverCatalogue (HttpClient? http = null) => _http = http ?? SharedHttp;

	public async Task<PublishedDriverSearchResult> SearchAsync (string? search = null, CancellationToken cancellationToken = default)
		{
		// Observed portal 4.0.58 request (31 October 2026). This endpoint currently
		// returns the whole catalogue regardless of first/rows/globalFilter. Search
		// locally only after checking totalRows and the complete row-number sequence.
		using var response = await _http.PostAsJsonAsync ("https://drivers-api.crestron.io/driver/findMany?isBeta=0", new
			{
			first = 0, rows = 50, sortOrder = 1, filters = new { }, globalFilter = "",
			multiSortMeta = new[] { new { field = "deviceType", order = 1 }, new { field = "manufacturer", order = 1 }, new { field = "modelName", order = 1 } }
			}, cancellationToken).ConfigureAwait (false);
		response.EnsureSuccessStatusCode ();
		var rows = await response.Content.ReadFromJsonAsync<CatalogueRow[]> (cancellationToken: cancellationToken).ConfigureAwait (false)
			?? throw new InvalidDataException ("Published catalogue response was missing.");
		if (rows.Length == 0 || rows.Any (r => r is null || r.TotalRows != rows.Length || r.RowNum < 1 || r.RowNum > rows.Length)
			|| rows.Select (r => r.RowNum).Distinct ().Count () != rows.Length)
			throw new InvalidDataException ("Published catalogue completeness could not be verified. No partial search results were returned.");
		// The real catalogue includes manufacturer-only rows with modelId=0 and
		// an empty modelName. Keep those rows without inventing a model label.
		if (rows.Any (r => r.Id <= 0 || r.ModelId is null or < 0 || r.ModelName is null
			|| string.IsNullOrWhiteSpace (r.Manufacturer) || string.IsNullOrWhiteSpace (r.DriverVersion)
			|| r.Available is null || r.IsBetaDriver is null))
			throw new InvalidDataException ("Published catalogue identity or publication metadata was missing.");
		string[] terms = string.IsNullOrWhiteSpace (search) ? [] : search.Split ((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
		var matches = rows.Where (r => r.Available == true && r.IsBetaDriver == false)
			.Where (r => terms.All (term => new[] { r.ModelName, r.Manufacturer, r.DriverVersion, r.DeviceType, r.CommunicationType, r.Developer }
				.Any (field => field?.Contains (term, StringComparison.OrdinalIgnoreCase) == true)))
			.Select (r => new PublishedDriverInfo (r.Id, r.ModelId!.Value, r.ModelName!, r.Manufacturer!, r.DriverVersion!,
				r.DeviceType ?? "", r.CommunicationType ?? "", r.Developer ?? "", r.PlatformsSupported ?? "",
				r.IsExtensionDevice, r.Certification ?? "", r.LicenseRequired))
			// One package can cover several models. Preserve model rows, including
			// duplicate server metadata, rather than conflating IDs with package counts.
			.ToArray ();
		return new (Source, DateTimeOffset.UtcNow, rows.Length, search, matches);
		}

	private sealed record CatalogueRow (int RowNum, int Id, int? ModelId, string? ModelName, string? Manufacturer,
		string? DriverVersion, string? DeviceType, string? CommunicationType, string? Developer, string? PlatformsSupported,
		bool IsExtensionDevice, string? Certification, bool LicenseRequired, bool? Available, bool? IsBetaDriver, int TotalRows);
	}
