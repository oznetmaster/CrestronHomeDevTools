// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;
using System.Text.Json.Nodes;

using CrestronHomeDevTools;

/// <summary>Runs the real command parser and revalidator with transport substitution confined to this test executable.</summary>
internal static class SyntheticDeliveryCommand
	{
	internal static async Task<int> Run (string[] args, bool revokeAfterUpload)
		{
		var transport = new SimulatedTransport ();
		using var output = new StringWriter ();
		int code = await SubmissionDispatchCommand.RunAsync (args, Console.In, output, Console.Error,
			execute: async (settings, credentials, token) =>
				{
				if (credentials.UploadPassword != "synthetic-upload-secret" || credentials.SmtpPassword != "synthetic-mail-secret")
					throw new InvalidDataException ("The test requires synthetic credentials.");
				if (revokeAfterUpload)
					{
					string path = settings.Revalidation?.PreparationSettingsPath ?? settings.BundledRevalidation!.PreparationSettingsPath;
					var preparation = JsonNode.Parse (await File.ReadAllTextAsync (path, token))!;
					string authorization = preparation["authorization"]!.GetValue<string> ();
					transport.AfterUpload = () => File.AppendAllText (authorization, "\n");
					}
				return await SubmissionDispatchCommand.DispatchAsync (settings, transport,
					(step, cancellation) => SubmissionDispatchCommand.RevalidateAsync (settings, step, cancellation), token);
				});
		Console.Write (JsonSerializer.Serialize (new
			{
			Command = string.IsNullOrWhiteSpace (output.ToString ()) ? null : JsonNode.Parse (output.ToString ()),
			transport.Uploads, transport.Sends, SyntheticTransport = true
			}));
		return code;
		}

	private sealed class SimulatedTransport : ISubmissionDeliveryTransport
		{
		internal int Uploads, Sends;
		internal Action? AfterUpload;
		public Task<SubmissionUploadReceipt> UploadAsync (Stream package, string name, CancellationToken token)
			{
			Uploads++;
			AfterUpload?.Invoke ();
			return Task.FromResult (new SubmissionUploadReceipt ("https://synthetic.example.test/no-network", "synthetic-upload-only"));
			}
		public Task<SubmissionMailReceipt> SendAsync (SubmissionDeliveryPlan plan, SubmissionUploadReceipt upload, Stream form, string id, CancellationToken token)
			{
			Sends++;
			return Task.FromResult (new SubmissionMailReceipt ("synthetic-mail-only"));
			}
		}
	}