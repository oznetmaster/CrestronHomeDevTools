// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.
// Synthetic scheduler test double. Never contacts a processor or generates acceptance evidence.
using System.Text.Json;

string worker = args[Array.IndexOf (args, "--worker") + 1];
string directory = args[Array.IndexOf (args, "--run") + 1];
Directory.CreateDirectory (directory);
File.AppendAllText (Path.Combine (directory, "calls.txt"), args[0] + Environment.NewLine);
if (Environment.GetEnvironmentVariables ().Keys.Cast<string> ().Any (s => s.StartsWith ("CRESTRON_HOME_", StringComparison.OrdinalIgnoreCase)))
	throw new Exception ("The scheduler inherited a connection override.");
using var document = JsonDocument.Parse (File.ReadAllText (worker));
string scenario = document.RootElement.GetProperty ("Scenario").GetString ()!;
bool tick = args[0] == "endurance-tick";
bool alreadyTicked = File.Exists (Path.Combine (directory, "ticked"));
if (tick)
	{
	File.WriteAllText (Path.Combine (directory, "ticked"), "synthetic");
	if (scenario == "hang") await Task.Delay (TimeSpan.FromMinutes (10));
	if (scenario == "error") return 2;
	Console.WriteLine ("{}");
	return scenario == "failure" ? 1 : 0;
	}
bool invalidStatus = scenario.StartsWith ("before-", StringComparison.Ordinal) ||
	scenario.StartsWith ("after-", StringComparison.Ordinal) && alreadyTicked;
if (invalidStatus)
	{
	string response = scenario[(scenario.IndexOf ('-') + 1)..];
	switch (response)
		{
		case "empty": return 0;
		case "unavailable": Console.Error.WriteLine ("Synthetic file/reservation status unavailable."); return 3;
		case "null": Console.WriteLine ("null"); return 0;
		case "missing": Console.WriteLine ("{}"); return 0;
		case "array": Console.WriteLine ("[]"); return 0;
		case "array-single": Console.WriteLine ("[{\"ReservationState\":\"Held\",\"Checkpoint\":{\"State\":\"Collecting\"}}]"); return 0;
		case "scalar": Console.WriteLine ("42"); return 0;
		case "malformed": Console.WriteLine ("not JSON"); return 0;
		case "checkpoint": Console.WriteLine ("{\"ReservationState\":\"Held\",\"Checkpoint\":{}}"); return 0;
		}
	}
if (scenario == "malformed") { Console.WriteLine ("not JSON"); return 0; }
string ownership = scenario switch
	{
		"acquiring" => "Acquiring", "releasing" => "Releasing", "not-started" => "NotStarted",
		"complete" or "failed" => "Released",
		"passing" or "failure" when alreadyTicked => "Released", _ => "Held"
	};
string? phase = scenario switch
	{
		"pending" => "ProbePending", "interrupted" => "Interrupted", "unknown" => "Surprising",
		"complete" => "Passed", "failed" => "Failed", "first" or "not-started" when !alreadyTicked => null,
		"passing" when alreadyTicked => "Passed", "failure" when alreadyTicked => "Failed", _ => "Collecting"
	};
Console.WriteLine (JsonSerializer.Serialize (new { ReservationState = ownership, Checkpoint = phase == null ? null : new { State = phase } }));
return phase == "Failed" ? 1 : phase is "ProbePending" or "Interrupted" || ownership is "Acquiring" or "Releasing" ? 3 : 0;