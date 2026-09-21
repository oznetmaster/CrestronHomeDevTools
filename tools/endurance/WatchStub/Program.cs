// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;

// Offline process-boundary double. It never contacts SMTP or a processor.
if (args.Length != 13 || args[0] != "endurance-watch" || args[9] != "--send" || args[10] != "true") return 10;
var options = Enumerable.Range (0, 6).ToDictionary (i => args[(i * 2) + 1], i => args[(i * 2) + 2]);
string journal = options["--journal"];
File.AppendAllText (Path.Combine (journal, "calls.txt"), "watch\n");
if (Environment.GetEnvironmentVariables ().Keys.Cast<string> ().Any (k => k.StartsWith ("CRESTRON_HOME_", StringComparison.OrdinalIgnoreCase))) return 11;
if ((await Console.In.ReadToEndAsync ()).Length != 0) return 12;
using var bindings = JsonDocument.Parse (File.ReadAllText (options["--credentials"]));
if (bindings.RootElement.GetProperty ("Smtp").GetString () != "synthetic-mail") return 13;
string scenario = File.ReadAllText (options["--worker"]).Trim ();
if (scenario == "slow") await Task.Delay (2000);
Console.WriteLine ("{\"Health\":\"synthetic\",\"Notification\":\"no network\"}");
Console.Error.WriteLine ("Synthetic diagnostic.");
return scenario == "attention" ? 3 : 0;
