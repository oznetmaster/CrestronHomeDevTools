// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

using CrestronHomeDevTools;

internal static class EnduranceNotificationCommand
	{
	private sealed record Credentials (string UserName, string Password);
	private static readonly JsonSerializerOptions JsonOptions = new ()
		{ PropertyNameCaseInsensitive = true, WriteIndented = true, Converters = { new JsonStringEnumConverter () } };
	internal static async Task<int> RunAsync (string[] args, TextReader input, TextWriter output, TextWriter error,
		CancellationToken token, Func<SubmissionEnduranceNotificationSettings, NetworkCredential, string, SubmissionEnduranceNotifier>? factory = null)
		{
		if (args.SequenceEqual (["--help"]))
			{
			await output.WriteLineAsync ("endurance-notify --settings FILE --health FILE --journal PRIVATE_DIRECTORY --send true\n" +
				"Send an explicitly authorized operational alert or completion notice. SMTP credentials arrive as JSON on standard input.\n" +
				"Exit 0: quiet or SMTP accepted; 2: invalid input; 3: uncertain/failed delivery or journal access. Never automatically retry exit 3.");
			return 0;
			}
		try
			{
			var options = new Dictionary<string, string> (StringComparer.Ordinal);
			for (int index = 0; index < args.Length; index += 2)
				{
				if (index + 1 >= args.Length || args[index] is not ("--settings" or "--health" or "--journal" or "--send") ||
					!options.TryAdd (args[index], args[index + 1])) throw new ArgumentException ("Unknown, incomplete or duplicate notification option.");
				}
			if (options.Count != 4 || options["--send"] != "true")
				throw new ArgumentException ("Provide settings, health, journal and explicit --send true after authorizing the notification destination.");
			T Read<T> (string path)
				{
				using var file = File.OpenRead (path);
				if (file.Length > 65536) throw new ArgumentException ("Notification input exceeds its size limit.");
				return JsonSerializer.Deserialize<T> (file, JsonOptions) ?? throw new ArgumentException ("Notification input is empty.");
				}
			var settings = Read<SubmissionEnduranceNotificationSettings> (options["--settings"]);
			var report = Read<SubmissionEnduranceHealthReport> (options["--health"]);
			var buffer = new char[8193];
			int count = 0, read;
			while (count < buffer.Length && (read = await input.ReadAsync (buffer.AsMemory (count), token)) != 0) count += read;
			if (count == 0 || count == buffer.Length) throw new ArgumentException ("Supply a bounded SMTP credential document on standard input.");
			Credentials credentials;
			try { credentials = JsonSerializer.Deserialize<Credentials> (buffer.AsSpan (0, count), JsonOptions) ?? throw new ArgumentException ("SMTP credentials are empty."); }
			finally { Array.Clear (buffer); }
			var notifier = (factory ?? ((s, c, d) => new SubmissionEnduranceNotifier (s, c, d)))
				(settings, new NetworkCredential (credentials.UserName, credentials.Password), options["--journal"]);
			var result = await notifier.NotifyAsync (report, token);
			await output.WriteLineAsync (JsonSerializer.Serialize (result, JsonOptions));
			return result.RequiresInspection ? 3 : 0;
			}
		catch (Exception failure) when (failure is ArgumentException or JsonException)
			{ await error.WriteLineAsync ("Invalid notification configuration, credentials or health report; no automatic retry. Use --help."); return 2; }
		catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
			{ await error.WriteLineAsync ("Notification outcome or journal access requires inspection. Preserve the private journal; do not automatically resend."); return 3; }
		}
	}