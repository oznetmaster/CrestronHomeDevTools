// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

using CrestronHomeDevTools;

internal static class EnduranceWatchCommand
	{
	private sealed record Credential (string UserName, string Password);
	private sealed record Credentials (Credential? Windows, Credential Smtp);
	private static readonly JsonSerializerOptions JsonOptions = new ()
		{
		PropertyNameCaseInsensitive = true,
		WriteIndented = true,
		Converters = { new JsonStringEnumConverter () }
		};
	internal static async Task<int> RunAsync (string[] args, TextReader input, TextWriter output, TextWriter error,
		CancellationToken token,
		Func<SubmissionEndurancePlan, EnduranceObservationCommand.Settings, NetworkCredential?, CancellationToken, Task<SubmissionEnduranceHealthReport>>? assess = null,
		Func<SubmissionEnduranceNotificationSettings, NetworkCredential, string, SubmissionEnduranceNotifier>? factory = null)
		{
		if (args.SequenceEqual (["--help"]))
			{
			await output.WriteLineAsync ("endurance-watch --worker FILE --observer FILE --notifications FILE --journal PRIVATE_DIRECTORY --send true [--credentials BINDINGS_JSON]\n" +
				"Perform one passive observation and notify the authorized destination, including observation failures.\n" +
				"Use named encrypted credential bindings, or stdin as {windows:{userName,password},smtp:{userName,password}}; omit windows for local observation.\n" +
				"Exit 0: healthy/completed with quiet or accepted notification; 2: invalid input; 3: monitoring attention or notification requiring inspection.\n" +
				"No collector or processor operation, installation, scheduling or automatic resend occurs.");
			return 0;
			}
		SubmissionEnduranceHealthReport? report = null;
		try
			{
			token.ThrowIfCancellationRequested ();
			var options = new Dictionary<string, string> (StringComparer.Ordinal);
			for (int i = 0; i < args.Length; i += 2)
				if (i + 1 >= args.Length || args[i] is not ("--worker" or "--observer" or "--notifications" or "--journal" or "--send" or "--credentials") ||
					!options.TryAdd (args[i], args[i + 1]))
					throw new ArgumentException ("Invalid watcher options.");
			if (new[] { "--worker", "--observer", "--notifications", "--journal", "--send" }.Any (key => !options.ContainsKey (key)) || options["--send"] != "true")
				throw new ArgumentException ("Explicit notification authorization is required.");
			T Read<T> (string path)
				{
				using var file = File.OpenRead (path);
				if (file.Length > 65536)
					throw new ArgumentException ("Watcher input exceeds its size limit.");
				return JsonSerializer.Deserialize<T> (file, JsonOptions) ?? throw new ArgumentException ("Empty watcher input.");
				}
			var worker = EnduranceCommands.Read (options["--worker"]);
			SubmissionEndurance.ValidatePlan (worker.Plan);
			var observer = Read<EnduranceObservationCommand.Settings> (options["--observer"]);
			if (observer.Task == null)
				throw new ArgumentException ("Missing task configuration.");
			var notifications = Read<SubmissionEnduranceNotificationSettings> (options["--notifications"]);
			Credentials credentials;
			if (options.TryGetValue ("--credentials", out var bindingsPath))
				{
				if (!OperatingSystem.IsWindows ())
					throw new PlatformNotSupportedException ("Saved credentials require Windows.");
				var bindings = DevToolsCredentialBindings.Read (bindingsPath);
				var savedSmtp = bindings.Resolve (DevToolsCredentialPurpose.Smtp, notifications.Host, notifications.Port, notifications.Sender);
				Credential? savedWindows = null;
				if (observer.Remote != null)
					{
					var value = bindings.Resolve (DevToolsCredentialPurpose.Windows, observer.Remote.Host, observer.Remote.Port);
					if (value.SshFingerprint != observer.Remote.HostKeyFingerprint)
						throw new ArgumentException ("Saved Windows trust does not match the observer endpoint.");
					savedWindows = new (value.UserName, value.Password);
					}
				credentials = new (savedWindows, new (savedSmtp.UserName, savedSmtp.Password));
				}
			else
				{
				var buffer = new char[16385];
				try
					{
					int count = 0, read;
					while (count < buffer.Length && (read = await input.ReadAsync (buffer.AsMemory (count), token)) != 0)
						count += read;
					if (count == 0 || count == buffer.Length)
						throw new ArgumentException ("Supply bounded credentials on standard input.");
					credentials = JsonSerializer.Deserialize<Credentials> (buffer.AsSpan (0, count), JsonOptions) ?? throw new ArgumentException ("Empty credentials.");
					}
				finally { Array.Clear (buffer); }
				}
			static NetworkCredential ConvertCredential (Credential? value)
				{
				if (value == null || string.IsNullOrWhiteSpace (value.UserName) || string.IsNullOrEmpty (value.Password))
					throw new ArgumentException ("Missing explicit credentials.");
				return new (value.UserName, value.Password);
				}
			var smtp = ConvertCredential (credentials.Smtp);
			var windows = observer.Remote == null ? null : ConvertCredential (credentials.Windows);
			if (observer.Remote == null && credentials.Windows != null)
				throw new ArgumentException ("Local observation must not use remote credentials.");
			var notifier = (factory ?? ((s, c, d) => new SubmissionEnduranceNotifier (s, c, d))) (notifications, smtp, options["--journal"]);
			report = await (assess ?? ((p, s, c, ct) => SubmissionEnduranceWindowsObserver.AssessAsync (p, s.Task, s.Remote, c, ct)))
				(worker.Plan, observer, windows, token);
			// Attention is data that must reach the notifier, not a success-only process condition.
			var notification = await notifier.NotifyAsync (report, token);
			await output.WriteLineAsync (JsonSerializer.Serialize (new
				{
				Health = report,
				Notification = notification
				}, JsonOptions));
			return report.RequiresAttention || notification.RequiresInspection ? 3 : 0;
			}
		catch (Exception failure) when (failure is ArgumentException or JsonException)
			{
			await WriteFailureAsync ("invalid-input");
			await error.WriteLineAsync ("Invalid watcher configuration or credentials. No previous healthy report may be substituted. Use --help.");
			return 2;
			}
		catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException or PlatformNotSupportedException or System.Security.Cryptography.CryptographicException)
			{
			await WriteFailureAsync ("inspection-required");
			await error.WriteLineAsync ("Observation or notification requires inspection. Preserve the journal and retained health report; never automatically resend.");
			return 3;
			}
		Task WriteFailureAsync (string reason) => output.WriteLineAsync (JsonSerializer.Serialize (
			new
				{
				Health = report,
				Notification = (SubmissionEnduranceNotificationResult?)null,
				Failure = reason
				}, JsonOptions));
		}
	}