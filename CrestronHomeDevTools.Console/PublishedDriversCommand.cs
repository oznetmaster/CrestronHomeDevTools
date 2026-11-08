// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;
using CrestronHomeDevTools;

internal static class PublishedDriversCommand
	{
	internal static async Task<int> RunAsync (string[] args, TextWriter output, TextWriter error, CancellationToken token,
		PublishedDriverCatalogue? catalogue = null)
		{
		if (args.SequenceEqual (["--help"]))
			{
			await output.WriteLineAsync ("published-drivers [--search TEXT]\nSearch Crestron's published non-beta catalogue independently of processor installations. No credentials required. This catalogue does not establish Crestron Home compatibility.");
			return 0;
			}
		if (args.Length != 0 && (args.Length != 2 || args[0] != "--search" || string.IsNullOrWhiteSpace (args[1])))
			{
			await error.WriteLineAsync ("Use published-drivers [--search TEXT]. Processor, profile and credential options do not apply.");
			return 2;
			}
		try
			{
			var result = await (catalogue ?? new PublishedDriverCatalogue ()).SearchAsync (args.Length == 2 ? args[1] : null, token);
			await output.WriteLineAsync (JsonSerializer.Serialize (result, new JsonSerializerOptions { WriteIndented = true }));
			return 0;
			}
		catch (Exception exception) when (exception is HttpRequestException or InvalidDataException or JsonException or OperationCanceledException)
			{
			await error.WriteLineAsync ("Published catalogue could not be retrieved and validated. No processor catalogue fallback or partial results were returned.");
			return 3;
			}
		}
	}
