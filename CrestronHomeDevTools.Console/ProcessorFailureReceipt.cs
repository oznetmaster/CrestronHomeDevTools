// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;

using CrestronHomeDevTools;

internal static class ProcessorFailureReceipt
	{
	public static async Task<string> WriteAsync (string leaseReceiptPath, ProcessorApiException exception)
		{
		if (!exception.DiagnosticResponse.HasValue || string.IsNullOrWhiteSpace (exception.DiagnosticCommand))
			throw new ArgumentException ("A processor command and its diagnostic response are required.", nameof (exception));
		var path = Path.ChangeExtension (leaseReceiptPath, ".failure.json");
		// Preserve a previous observation; failure recording never overwrites the lease or another reply.
		await using var output = new FileStream (path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
		await JsonSerializer.SerializeAsync (output, new
			{
			SchemaVersion = 1,
			RecordedUtc = DateTimeOffset.UtcNow,
			LeaseReceiptFile = Path.GetFileName (leaseReceiptPath),
			Command = exception.DiagnosticCommand,
			Message = exception.Message,
			Response = exception.DiagnosticResponse.Value
			}, new JsonSerializerOptions { WriteIndented = true });
		await output.FlushAsync ();
		return path;
		}
	}