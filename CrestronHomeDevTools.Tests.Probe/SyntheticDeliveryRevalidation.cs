// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

using CrestronHomeDevTools;

internal static class SyntheticDeliveryRevalidation
	{
	internal static async Task<int> Run (string[] args)
		{
		string Argument (string name) => args[Array.IndexOf (args, name) + 1];
		string requestPath = Argument ("--settings");
		var request = JsonNode.Parse (await File.ReadAllTextAsync (requestPath))!;
		string output = request["output"]!.GetValue<string> ();
		string mode = await File.ReadAllTextAsync (Path.Combine (Environment.CurrentDirectory, "revalidate_delivery.py"));
		if (mode == "hang") { await Task.Delay (Timeout.Infinite); return 9; }
		if (mode == "exit") { Console.Error.Write ("PRIVATE-SYNTHETIC-AUTH-SECRET"); return 7; }
		if (mode == "stdout-limit") { Console.Write (new string ('x', 1024 * 1024 + 1)); await Task.Delay (Timeout.Infinite); return 9; }
		if (mode == "stderr-limit") { Console.Error.Write (new string ('x', 65537)); await Task.Delay (Timeout.Infinite); return 9; }
		if (mode == "malformed") { Console.Write ("PRIVATE-SYNTHETIC-AUTH-SECRET"); return 0; }
		Directory.CreateDirectory (output);
		var plan = JsonNode.Parse (await File.ReadAllTextAsync (Path.Combine (Environment.CurrentDirectory, "plan.json")))!;
		if (mode == "wrong-plan") plan["Recipient"] = "different@example.test";
		var filePlan = plan.DeepClone ();
		if (mode == "wrong-plan-file") filePlan["Recipient"] = "different@example.test";
		byte[] planBytes = JsonSerializer.SerializeToUtf8Bytes (filePlan), validation = "{}"u8.ToArray ();
		await File.WriteAllBytesAsync (Path.Combine (output, "delivery-plan.json"), planBytes);
		await File.WriteAllBytesAsync (Path.Combine (output, "validation-report.json"), validation);
		var receipt = JsonSerializer.SerializeToNode (new
			{
			schemaVersion = 1, state = "DeliveryRevalidated", deliveryReviewSha256 = Argument ("--delivery-review-sha256"),
			signedReviewSha256 = Argument ("--signed-review-sha256"), authorizationSha256 = Argument ("--authorization-sha256"),
			planFileSha256 = Hash (planBytes), plan, expiresUtc = DateTimeOffset.UtcNow.AddMinutes (mode == "expired" ? -1 : 10),
			revalidatedUtc = DateTimeOffset.UtcNow.AddHours (mode == "stale-result" ? -1 : mode == "future-result" ? 1 : 0), deliveryAttempted = false, submissionReady = false, validationReportSha256 = Hash (validation)
			})!;
		if (mode == "missing-field") receipt.AsObject ().Remove ("deliveryAttempted");
		byte[] bytes = JsonSerializer.SerializeToUtf8Bytes (receipt);
		await File.WriteAllBytesAsync (Path.Combine (output, "revalidation-receipt.json"), bytes);
		if (mode != "no-complete") await File.WriteAllTextAsync (Path.Combine (output, "COMPLETE"), mode == "wrong-marker" ? new ('0', 64) : Hash (bytes));
		if (mode == "changed-report") await File.WriteAllTextAsync (Path.Combine (output, "validation-report.json"), "changed");
		if (mode == "different-stdout") receipt["submissionReady"] = true;
		Console.Write (receipt.ToJsonString ());
		return 0;
		}
	internal static async Task<int> Bridge ()
		{
		try
			{
			var input = JsonNode.Parse (await Console.In.ReadToEndAsync ())!;
			var settings = input["settings"]!.Deserialize<SubmissionDeliveryRevalidationSettings> (Options)!;
			var plan = input["plan"]!.Deserialize<SubmissionDeliveryPlan> (Options)!;
			string journal = input["journal"]!.GetValue<string> (), package = input["package"]!.GetValue<string> (), form = input["form"]!.GetValue<string> ();
			var transport = new SimulatedTransport ();
			var result = await SubmissionDelivery.ExecuteAuthorizedAsync (journal, plan, package, form, transport,
				(step, token) => SubmissionDeliveryRevalidation.CheckAsync (settings, plan, step, token));
			Console.Write (JsonSerializer.Serialize (new { State = result.State.ToString (), transport.Uploads, transport.Sends, SyntheticTransport = true }));
			return 0;
			}
		catch { Console.Error.Write ("Synthetic bridge refused delivery; inspect private attempts."); return 1; }
		}
	private static readonly JsonSerializerOptions Options = new () { PropertyNameCaseInsensitive = true };
	private static string Hash (byte[] bytes) => Convert.ToHexString (SHA256.HashData (bytes)).ToLowerInvariant ();
	private sealed class SimulatedTransport : ISubmissionDeliveryTransport
		{
		internal int Uploads, Sends;
		public Task<SubmissionUploadReceipt> UploadAsync (Stream package, string name, CancellationToken token)
			{ Uploads++; return Task.FromResult (new SubmissionUploadReceipt ("https://synthetic.example.test/no-network", "synthetic-upload-only")); }
		public Task<SubmissionMailReceipt> SendAsync (SubmissionDeliveryPlan plan, SubmissionUploadReceipt upload, Stream form, string id, CancellationToken token)
			{ Sends++; return Task.FromResult (new SubmissionMailReceipt ("synthetic-mail-only")); }
		}
	}