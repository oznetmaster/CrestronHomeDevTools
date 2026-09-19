// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.
// Synthetic snapshot orchestration only; never contacts hardware or proves acceptance.
using System.Text.Json;

string worker = args[Array.IndexOf (args, "--worker") + 1];
string run = args[Array.IndexOf (args, "--run") + 1];
using var document = JsonDocument.Parse (File.ReadAllText (worker));
var settings = document.RootElement;
string scenario = settings.GetProperty ("Scenario").GetString ()!;
string original = settings.GetProperty ("OriginalRun").GetString ()!;
bool copied = run != original;
File.AppendAllText (settings.GetProperty ("Calls").GetString ()!, args[0] + (copied ? ":copy" : ":source") + Environment.NewLine);
if (args.Length != 5 || Environment.GetEnvironmentVariables ().Keys.Cast<string> ().Any (key => key.StartsWith ("CRESTRON_HOME_", StringComparison.OrdinalIgnoreCase)))
	throw new Exception ("Unexpected connection arguments or inherited settings.");
if (args[0] == "endurance-status")
	{
	if (scenario == "malformed") { Console.WriteLine ("{}"); return 0; }
	Console.WriteLine (JsonSerializer.Serialize (new
		{
		ReservationState = scenario == "held" ? "Held" : "Released",
		Checkpoint = new { State = scenario == "collecting" ? "Collecting" : scenario == "failed" ? "Failed" : "Passed" }
		}));
	return 0;
	}
if (args[0] != "endurance-export") throw new Exception ("Mutation command was invoked.");
if (scenario == "export-error" || copied && scenario == "copy-error")
	{
	Console.Error.WriteLine ("Synthetic export failure retained.");
	return 3;
	}
if (copied && scenario == "source-change") File.AppendAllText (Path.Combine (original, "sample.json"), "changed");
if (copied && scenario == "copy-change") File.AppendAllText (Path.Combine (run, "sample.json"), "changed");
Console.WriteLine (JsonSerializer.Serialize (new
	{
	Outcome = "Passed", RequirementId = "synthetic-only",
	Identity = new { PackageSha256 = new string ('a', 64), PolicySha256 = new string (copied && scenario == "mismatch" ? 'd' : 'c', 64) }
	}));
return 0;