// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json.Nodes;

if (args is ["--automation-review",var fixtureRoot,var bundledConsole])
 return await SyntheticAutomationReview.Run(fixtureRoot,bundledConsole);
if (args is ["--automation-protected",var protectedRoot,var protectedConsole,var phase])
 return await SyntheticAutomationReview.Run(protectedRoot,protectedConsole,phase);
if (args is ["--review-request-delivery", var requestDirectory, var workDirectory, var change])
	return await SyntheticReviewRequestDelivery.Run (requestDirectory, workDirectory, change);
if (args.Contains ("--delivery-review-sha256")) return await SyntheticDeliveryRevalidation.Run (args);
if (args.Contains ("--real-delivery-bridge")) return await SyntheticDeliveryRevalidation.Bridge ();
if (args.Length > 0 && args[0] is "--real-delivery-command" or "--delivery-command-revoke-after-upload")
	return await SyntheticDeliveryCommand.Run (args[1..], args[0] == "--delivery-command-revoke-after-upload");

// Test-only child process: never connects to a processor or claims real driver acceptance.
var request = JsonNode.Parse (await Console.In.ReadToEndAsync ())!;
string settings = request["SettingsFile"]!.GetValue<string> ();
string mode = await File.ReadAllTextAsync (settings);
await File.WriteAllTextAsync (settings + ".pid", Environment.ProcessId.ToString ());
switch (mode)
	{
	case "hang": await Task.Delay (Timeout.Infinite); return 9;
	case "exit": Console.Error.Write ("PRIVATE-SECRET-MUST-NOT-ESCAPE"); return 7;
	case "malformed": Console.Write ("PRIVATE-SECRET-MUST-NOT-ESCAPE"); return 0;
	case "stdout-limit": Console.Write (new string ('x', 9 * 1024 * 1024)); await Task.Delay (Timeout.Infinite); return 9;
	case "stderr-limit": Console.Error.Write (new string ('x', 70 * 1024)); await Task.Delay (Timeout.Infinite); return 9;
	}
var plan = request["Plan"]!;
var result = new JsonObject ();
foreach (string field in new[] { "Identity", "ProcessorIdentity", "InstallationIdentity", "ReservationId", "ProducerId" })
	result[field] = plan[field]!.DeepClone ();
result["BootIdentity"] = "synthetic-boot";
result["Outcome"] = 1; // SubmissionEvidenceOutcome.Passed; synthetic protocol evidence only.
result["Evidence"] = Convert.ToBase64String ("Synthetic process protocol test only"u8);
Console.Write (result.ToJsonString ());
return 0;
