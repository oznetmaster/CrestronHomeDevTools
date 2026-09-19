// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

using CrestronHomeDevTools;

internal static class EnduranceObservationCommand
	{
	internal sealed record Settings (SubmissionEnduranceWindowsTask Task, SubmissionEnduranceObserverEndpoint? Remote);
	private sealed record Credentials (string UserName, string Password);
	private static readonly JsonSerializerOptions JsonOptions = new ()
		{ PropertyNameCaseInsensitive = true, WriteIndented = true, Converters = { new JsonStringEnumConverter () } };
	internal static async Task<int> RunAsync (string[] args, TextReader input, TextWriter output, TextWriter error,
		CancellationToken token, Func<SubmissionEndurancePlan, Settings, NetworkCredential?, CancellationToken, Task<SubmissionEnduranceHealthReport>>? assess = null)
		{
		if (args.SequenceEqual (["--help"]))
			{
			await output.WriteLineAsync ("endurance-observe --worker FILE --observer FILE\n" +
				"Read a Windows scheduled task and its completed snapshot locally or using pinned SSH. No collector/processor commands.\n" +
				"Remote credentials arrive as JSON on standard input. Exit 0: healthy/completed; 2: invalid input; 3: attention or observation failure.");
			return 0;
			}
		try
			{
			var options = new Dictionary<string, string> (StringComparer.Ordinal);
			for (int index = 0; index < args.Length; index += 2)
				if (index + 1 >= args.Length || args[index] is not ("--worker" or "--observer") || !options.TryAdd (args[index], args[index + 1]))
					throw new ArgumentException ("Invalid observer options.");
			if (options.Count != 2) throw new ArgumentException ("Provide worker and observer files.");
			var worker = EnduranceCommands.Read (options["--worker"]);
			using var stream = File.OpenRead (options["--observer"]);
			if (stream.Length > 65536) throw new ArgumentException ("Observer configuration exceeds its size limit.");
			var settings = JsonSerializer.Deserialize<Settings> (stream, JsonOptions) ?? throw new ArgumentException ("Empty observer configuration.");
			if (settings.Task == null) throw new ArgumentException ("Missing Windows task configuration.");
			NetworkCredential? credential = null;
			if (settings.Remote != null)
				{
				var buffer = new char[8193];
				try
					{
					int count = 0, read;
					while (count < buffer.Length && (read = await input.ReadAsync (buffer.AsMemory (count), token)) != 0) count += read;
					if (count == 0 || count == buffer.Length) throw new ArgumentException ("Supply bounded Windows SSH credentials on standard input.");
					var value = JsonSerializer.Deserialize<Credentials> (buffer.AsSpan (0, count), JsonOptions) ?? throw new ArgumentException ("Empty credentials.");
					credential = new (value.UserName, value.Password);
					}
				finally { Array.Clear (buffer); }
				}
			var report = await (assess ?? ((plan, s, c, ct) => SubmissionEnduranceWindowsObserver.AssessAsync (plan, s.Task, s.Remote, c, ct)))
				(worker.Plan, settings, credential, token);
			await output.WriteLineAsync (JsonSerializer.Serialize (report, JsonOptions));
			return report.RequiresAttention ? 3 : 0;
			}
		catch (Exception failure) when (failure is ArgumentException or JsonException)
			{ await error.WriteLineAsync ("Invalid observer configuration or credentials. Use --help; no previous healthy result may be substituted."); return 2; }
		catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException or PlatformNotSupportedException)
			{ await error.WriteLineAsync ("Independent observation could not be confirmed. Alert on this failure; no collector operation was attempted."); return 3; }
		}
	}