// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Net;
using System.Text.Json;

namespace CrestronHomeDevTools;

public sealed record DiscoveredProcessor (string SystemName, string Address, string Model, string ProgramVersion);

public static class ProcessorDiscovery
	{
	public static async Task<IReadOnlyList<DiscoveredProcessor>> FindAsync (CancellationToken cancellationToken = default)
		{
		var names = await NativeDiscovery.FindNamesAsync (cancellationToken).ConfigureAwait (false);
		// These anonymous identification requests carry no credentials. Discovery metadata
		// is untrusted; authenticated connections validate the processor certificate separately.
		using var handler = new HttpClientHandler { AllowAutoRedirect = false, ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
		using var http = new HttpClient (handler) { Timeout = TimeSpan.FromSeconds (3) };
		using var limit = new SemaphoreSlim (8);
		var candidates = names.Select (async entry =>
		{
			await limit.WaitAsync (cancellationToken).ConfigureAwait (false);
			try
				{
				using var response = await http.GetAsync ($"https://{entry.Key}/cws/api/v2", cancellationToken).ConfigureAwait (false);
				if (!response.IsSuccessStatusCode)
					return null;
				await using var body = await response.Content.ReadAsStreamAsync (cancellationToken).ConfigureAwait (false);
				using var json = await JsonDocument.ParseAsync (body, cancellationToken: cancellationToken).ConfigureAwait (false);
				return ParseIdentification (entry.Value, entry.Key, json.RootElement);
				}
			catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException)
				{
				cancellationToken.ThrowIfCancellationRequested ();
				return null;
				}
			finally { limit.Release (); }
		});
		return (await Task.WhenAll (candidates).ConfigureAwait (false)).OfType<DiscoveredProcessor> ().OrderBy (p => p.SystemName).ThenBy (p => p.Address).ToArray ();
		}

	public static async Task<DiscoveredProcessor> ResolveAsync (string systemName, CancellationToken cancellationToken = default)
		 => SelectByName (await FindAsync (cancellationToken).ConfigureAwait (false), systemName);

	internal static DiscoveredProcessor SelectByName (IEnumerable<DiscoveredProcessor> processors, string name)
		{
		ArgumentException.ThrowIfNullOrWhiteSpace (name);
		var matches = processors.Where (p => p.SystemName.Equals (name, StringComparison.OrdinalIgnoreCase)).ToArray ();
		return matches.Length switch
			{
				1 => matches[0],
				0 => throw new InvalidOperationException ("The named Crestron Home processor was not discovered. Use its IP address if broadcast discovery cannot reach its network."),
				_ => throw new InvalidOperationException ("More than one processor has that name. Select a processor by IP address.")
				};
		}

	internal static DiscoveredProcessor? ParseIdentification (string name, string address, JsonElement json)
		{
		if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty ("ConfigurationSchema", out var schema)
			 || schema.ValueKind != JsonValueKind.Object || !schema.TryGetProperty ("House", out _) || !schema.TryGetProperty ("Devices", out _)
			 || !json.TryGetProperty ("SystemType", out var model) || model.ValueKind != JsonValueKind.String
			 || !json.TryGetProperty ("ProgramVersion", out var version) || version.ValueKind != JsonValueKind.String)
			return null;
		return new DiscoveredProcessor (name, address, model.GetString ()!, version.GetString ()!);
		}
	}