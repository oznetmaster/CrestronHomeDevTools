// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Net;
using System.Text.Json;

using CrestronHomeDevTools;

if (args.Length == 0 && !Console.IsInputRedirected)
	{
	// Interactive startup discovers processors and saves a reusable encrypted profile.
	var configured = await RunSafelyAsync (["configure"]);
	if (configured != 0)
		return configured;
	Console.Error.WriteLine ("Ready. Enter a CLI command (help lists commands), or exit.");
	while (true)
		{
		Console.Error.Write ("ch> ");
		var line = Console.ReadLine ();
		if (line == null || line.Trim ().Equals ("exit", StringComparison.OrdinalIgnoreCase))
			return 0;
		if (string.IsNullOrWhiteSpace (line))
			continue;
		try
			{
			await RunSafelyAsync (ConsoleCommandLine.Split (line), interactive: true);
			}
		catch (ArgumentException exception) { Console.Error.WriteLine (exception.Message); }
		}
	}
return await RunSafelyAsync (args);

static async Task<int> RunSafelyAsync (string[] args, bool interactive = false)
	{
	try { return await RunAsync (args, interactive); }
	catch (IOException exception)
		{
		// Final lease cleanup can fail after the command itself has completed.
		Console.Error.WriteLine (exception.Message);
		return 3;
		}
	}

static async Task<int> RunAsync (string[] args, bool interactive = false)
	{
	if (args is ["capabilities"])
		{
		Console.WriteLine (JsonSerializer.Serialize (new { SharedProcessorLease = 1, VerifiedPackageImport = true }));
		return 0;
		}
	if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
		{
		Console.WriteLine ("""
            CrestronHomeDevTools - unofficial Crestron Home configuration CLI

            Inspect configuration (read-only):
              discover                 Find Crestron Home processors on the local network.
              capabilities             Show automation features without connecting.
              drivers [--search text]  List available driver packages and their catalogue IDs.
              stored-packages          Inspect retained packages and matching device references.
              devices                  List installed devices, their IDs, names and room IDs.
              driver-configuration --device ID
                                       Show current settings; honour driver-defined masking.
              eligibility --driver ID  Show installed devices eligible for that driver update.
              reload-scope --device ID Show devices associated with a proposed driver reload.

            Save settings or prepare an update (no processor changes):
              configure                Choose a processor and save encrypted credentials locally.
              plan-update --driver ID --output plan.json
                                       Save update versions and affected device IDs for review.

            Change processor configuration:
              deploy --package FILE    Upload a .pkg by SFTP, import it and verify its version.
                                       Does not replace any installed driver instance.
              refresh                  Import uploaded packages into the driver catalogue.
                                       This does not update installed driver instances.
              update --plan plan.json  Apply the reviewed update to every eligible instance.
                                       Refuses if versions or affected devices have changed.
              reload --device ID       Reload only the selected device's driver instance.
                                       Requires reload support and refuses a required reboot.

              activate --driver ID --name NAME --room ID [--device ID]
                                       Install if absent, upgrade if older, or verify loaded.
                                       Waits for update readiness; refuses ambiguous targets.
              configure-driver --device ID --model NAME --version VERSION --input FILE
                                       Apply private initial settings or ordered wizard steps.
                                       Verify configured state; preserve existing configuration.
              remove --device ID --model NAME --version VERSION
                                       Remove a matching instance and verify it disappeared.
                                       Refuses reboot requirements or other affected devices.

            Reboot and interactive console:
              reboot                   Reboot the whole processor after typed confirmation.
                                       CLI requires --processor TARGET --confirm-reboot TARGET.
              help                     Show this command guide.
              exit                     Close the interactive console.

            Use an Id from 'drivers' for --driver, or from 'devices' for --device.
            Example: drivers --search Tests
            Run configure again to select a processor or change saved credentials.

            Options:
              --processor IP-or-system-name        Select processor for this command
              --profile name                      Saved encrypted profile (default: default)
              --settings private-settings.json    Optional private connection settings
              --timeout seconds                   Wait timeout (default 120; reboot 600)
              --help                              Show this help

            Connection settings may instead be supplied through environment variables:
              CRESTRON_HOME_HOST, CRESTRON_HOME_USER, CRESTRON_HOME_PASSWORD,
              CRESTRON_HOME_CERT_SHA256 (optional, verified SHA-256 certificate pin),
              CRESTRON_HOME_SSH_FINGERPRINT (required for mutations unless saved).

            Settings file fields: host, userName, password, certificateSha256,
              httpsPort (443), webSocketPort (49000), sshFingerprint (for mutations).
            Environment variables override file values. Passwords are never CLI arguments.

            Standard output: JSON results. Standard error: concise diagnostics.
            Exit codes: 0 success, 1 processor/operation error, 2 usage/settings error,
              3 timeout or unconfirmed outcome, 130 cancelled.
            Update/reload/refresh wait for an operation result on the same connection.
            Cancellation does not undo a submitted processor operation.
            """);
		return 0;
		}

	using var cancellation = new CancellationTokenSource ();
	ConsoleCancelEventHandler cancelHandler = (_, e) => { e.Cancel = true; cancellation.Cancel (); };
	Console.CancelKeyPress += cancelHandler;
	ProcessorOperationLease? lease = null;
	bool mutationSubmitted = false;
	bool mutationStopped = false;
	string? leaseReceiptPath = null;
	try
		{
		var command = args[0];
		var allowed = command switch
			{
				"configure" or "discover" => Array.Empty<string> (),
				"drivers" => ["search"],
				"driver-configuration" => ["device"],
				"devices" or "refresh" or "stored-packages" => [],
				"reboot" => ["confirm-reboot"],
				"deploy" => ["package"],
				"activate" => ["driver", "name", "room", "device"],
				"remove" => ["device", "model", "version"],
				"configure-driver" => ["device", "model", "version", "input"],
				"eligibility" => ["driver"],
				"plan-update" => ["driver", "output"],
				"update" => ["plan"],
				"reload" or "reload-scope" => ["device"],
				_ => throw new ArgumentException ("Unknown command. Run with --help.")
				};
		var options = new Dictionary<string, string> (StringComparer.Ordinal);
		for (var index = 1; index < args.Length; index += 2)
			{
			if (!args[index].StartsWith ("--", StringComparison.Ordinal) || index + 1 == args.Length)
				throw new ArgumentException ("Options must use --name value pairs.");
			var key = args[index][2..];
			if (!allowed.Concat (["settings", "timeout", "profile", "processor"]).Contains (key) || !options.TryAdd (key, args[index + 1]))
				throw new ArgumentException ("Unknown or duplicate option. Run with --help.");
			}
		var preauthorizedReboot = command == "reboot" && RebootConfirmation.ValidateCliAuthorization (
			 options.GetValueOrDefault ("processor"), options.GetValueOrDefault ("confirm-reboot"), interactive && !Console.IsInputRedirected);
		string Required (string key) => options.TryGetValue (key, out var value) && !string.IsNullOrWhiteSpace (value) ? value : throw new ArgumentException ($"Missing --{key}.");
		var timeout = TimeSpan.FromSeconds (command == "reboot" ? 600 : 120);
		if (options.TryGetValue ("timeout", out var timeoutText))
			{
			if (!int.TryParse (timeoutText, out var seconds) || seconds is < 1 or > 3600)
				throw new ArgumentException ("Timeout must be 1 to 3600 seconds.");
			timeout = TimeSpan.FromSeconds (seconds);
			}
		string? driverId = command is "eligibility" or "plan-update" or "activate" ? Required ("driver") : null;
		var deviceId = 0;
		if (command is "reload" or "reload-scope" or "remove" or "configure-driver" or "driver-configuration")
			{
			if (!int.TryParse (Required ("device"), out deviceId) || deviceId <= 0)
				throw new ArgumentException ("Device ID must be a positive integer.");
			}
		DriverConfiguration.Inputs? configurationInputs = null;
		DriverInstanceReady? configurationTarget = null;
		if (command == "configure-driver")
			{
			configurationInputs = DriverConfiguration.ReadInputs (Required ("input"));
			configurationTarget = new (deviceId, Required ("model"), Required ("version"), "Configure");
			if (!Version.TryParse (configurationTarget.Version, out _)) throw new ArgumentException ("Expected driver version is invalid.");
			}
		var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = true };
		if (command == "discover")
			{
			Console.WriteLine (JsonSerializer.Serialize (await ProcessorDiscovery.FindAsync (cancellation.Token), jsonOptions));
			return 0;
			}
		DriverUpdatePlan? plan = null;
		if (command == "update")
			plan = JsonSerializer.Deserialize<DriverUpdatePlan> (await File.ReadAllTextAsync (Required ("plan"), cancellation.Token), jsonOptions)
				 ?? throw new ArgumentException ("The plan file was empty.");
		var output = command == "plan-update" ? Required ("output") : null;
		if (output != null && File.Exists (output))
			throw new ArgumentException ("The plan output file already exists. Choose a new path.");
		if (options.ContainsKey ("settings") && options.ContainsKey ("profile"))
			throw new ArgumentException ("Use either --settings or --profile.");
		var store = new ProfileStore ();
		var profileName = options.GetValueOrDefault ("profile", "default");
		var settings = options.TryGetValue ("settings", out var file)
			 ? JsonSerializer.Deserialize<ConsoleSettings> (await File.ReadAllTextAsync (file, cancellation.Token), jsonOptions) ?? new () : new ConsoleSettings ();
		if (file == null && store.Exists (profileName))
			{
			if (!OperatingSystem.IsWindows ())
				throw new PlatformNotSupportedException ("Saved profiles require Windows.");
			settings = store.Load (profileName);
			}
		else if (file == null && options.ContainsKey ("profile") && command != "configure")
			throw new ArgumentException ("The named profile does not exist. Run configure first.");
		if (command == "configure")
			{
			await ProfileSetup.ConfigureAsync (store, profileName, options.GetValueOrDefault ("processor"), settings, cancellation.Token);
			return 0;
			}
		string? Setting (string environmentName, string? fallback) => Environment.GetEnvironmentVariable (environmentName) ?? fallback;
		var selector = options.GetValueOrDefault ("processor") ?? Setting ("CRESTRON_HOME_HOST", settings.SystemName ?? settings.Host)
			 ?? throw new ArgumentException ("Run configure, choose --processor, or provide CRESTRON_HOME_HOST.");
		var host = IPAddress.TryParse (selector, out _) ? selector : (await ProcessorDiscovery.ResolveAsync (selector, cancellation.Token)).Address;
		var user = Setting ("CRESTRON_HOME_USER", settings.UserName) ?? throw new ArgumentException ("Provide a processor user in settings or CRESTRON_HOME_USER.");
		var password = Setting ("CRESTRON_HOME_PASSWORD", settings.Password) ?? throw new ArgumentException ("Provide a processor password in settings or CRESTRON_HOME_PASSWORD.");
		var connectionOptions = new ProcessorConnectionOptions
			{
			Host = host,
			HttpsPort = settings.HttpsPort,
			WebSocketPort = settings.WebSocketPort,
			CertificateSha256 = Setting ("CRESTRON_HOME_CERT_SHA256", settings.CertificateSha256)
			};
		if (command is "reboot" or "activate" or "remove" or "deploy" or "refresh" or "reload" or "update" or "configure-driver")
			{
			var leaseFingerprint = Setting ("CRESTRON_HOME_SSH_FINGERPRINT", settings.SshFingerprint)
				?? throw new ArgumentException ("Processor mutations require a verified SSH fingerprint for the shared lease. Run configure first.");
			lease = await ProcessorOperationLease.AcquireAsync (host, new NetworkCredential (user, password), leaseFingerprint, Guid.NewGuid ().ToString ("N"), cancellation.Token);
			var leaseDirectory = Path.Combine (Environment.GetFolderPath (Environment.SpecialFolder.LocalApplicationData), "CrestronHomeDevTools", "Leases");
			Directory.CreateDirectory (leaseDirectory);
			leaseReceiptPath = Path.Combine (leaseDirectory, lease.Owner + ".json");
			await File.WriteAllTextAsync (leaseReceiptPath, JsonSerializer.Serialize (new { Host = host, SshFingerprint = leaseFingerprint, Owner = lease.Owner, State = "Held", Command = command }, jsonOptions), cancellation.Token);
			Console.Error.WriteLine ("Processor lease acquired; private receipt: " + leaseReceiptPath);
			}
		if (command == "reboot")
			{
			var fingerprint = Setting ("CRESTRON_HOME_SSH_FINGERPRINT", settings.SshFingerprint)
				 ?? throw new ArgumentException ("Reboot requires a verified SSH fingerprint. Run configure first.");
			// A profile label only applies to its saved target; never label an override with another processor's name.
			var name = selector == settings.SystemName || host == settings.Host ? settings.SystemName : selector;
			await using var previous = await ConfigurationClient.ConnectAsync (connectionOptions, new NetworkCredential (user, password), cancellation.Token);
			mutationSubmitted = true;
			var reboot = await ProcessorReboot.RequestAsync (new (host, name), new NetworkCredential (user, password), fingerprint,
				 (target, token) => preauthorizedReboot ? Task.FromResult (true) : RebootConfirmation.ConfirmAsync (target, Console.In, Console.Error, token), timeout, cancellation.Token);
			mutationStopped = reboot.Status == ProcessorRebootStatus.Cancelled;
			if (reboot.Status == ProcessorRebootStatus.Accepted)
				{
				Console.Error.WriteLine ("Reboot acknowledged; waiting for shutdown and authenticated Home startup while retaining the processor lease.");
				await using var recovered = await ProcessorRestartRecovery.WaitAsync (previous,
					ct => ConfigurationClient.ConnectAsync (connectionOptions, new NetworkCredential (user, password), ct), timeout, cancellation.Token);
				await lease!.VerifyAfterReconnectAsync (host, cancellation.Token);
				mutationStopped = true;
				}
			Console.WriteLine (JsonSerializer.Serialize (new { reboot.Status, StartupVerified = reboot.Status == ProcessorRebootStatus.Accepted && mutationStopped }, jsonOptions));
			Console.Error.WriteLine (reboot.Status switch
				{
					ProcessorRebootStatus.Cancelled => "Reboot cancelled; no command was sent.",
					ProcessorRebootStatus.Accepted => "Processor shutdown and authenticated Home startup verified.",
					_ => "Reboot outcome is unconfirmed. Inspect processor availability; the command was not retried."
					});
			return reboot.Status == ProcessorRebootStatus.Unconfirmed ? 3 : 0;
			}
		await using var client = await ConfigurationClient.ConnectAsync (connectionOptions, new NetworkCredential (user, password), cancellation.Token);
		object? result;
		string? operationId = null;
		switch (command)
			{
			case "driver-configuration":
				result = await DriverConfigurationInspection.GetAsync (client, deviceId, cancellation.Token);
				break;
			case "configure-driver":
				Console.Error.WriteLine ("Applying private initial configuration to the matching driver; waiting for configured state.");
				mutationSubmitted = true;
				result = await DriverConfiguration.ConfigureAsync (client, configurationTarget!, configurationInputs!, timeout, cancellation.Token);
				break;
			case "activate":
				if (!int.TryParse (Required ("room"), out var roomId) || roomId <= 0)
					throw new ArgumentException ("Room ID must be a positive integer.");
				int? existingId = null;
				if (options.TryGetValue ("device", out var existingText))
					{
					if (!int.TryParse (existingText, out var existingValue) || existingValue <= 0)
						throw new ArgumentException ("Device ID must be a positive integer.");
					existingId = existingValue;
					}
				Console.Error.WriteLine ("Installing, updating or verifying the configured driver instance; waiting for its requested version to load.");
				mutationSubmitted = true;
				result = await DriverInstanceLifecycle.EnsureAsync (client, driverId!, Required ("name"), roomId, existingId, timeout, cancellation.Token);
				break;
			case "remove":
				mutationSubmitted = true;
				await client.RemoveDriverInstanceAsync (deviceId, Required ("model"), Required ("version"), timeout, cancellation.Token);
				result = new
					{
					DeviceId = deviceId,
					Removed = true
					};
				break;
			case "deploy":
				var sshFingerprint = Setting ("CRESTRON_HOME_SSH_FINGERPRINT", settings.SshFingerprint)
					?? throw new ArgumentException ("Deploy requires a verified SSH fingerprint. Run configure or provide CRESTRON_HOME_SSH_FINGERPRINT.");
				Console.Error.WriteLine ("Uploading package and refreshing the catalogue; installed instances will not be updated.");
				mutationSubmitted = true;
				result = await DriverDeployment.DeployAsync (client, host, new NetworkCredential (user, password), sshFingerprint, Required ("package"), timeout, cancellation.Token);
				break;
			case "drivers":
				result = await client.GetDriversAsync (options.GetValueOrDefault ("search"), cancellation.Token);
				break;
			case "stored-packages":
				result = await DriverPackageStorage.InspectAsync (client, host, new NetworkCredential (user, password),
					Setting ("CRESTRON_HOME_SSH_FINGERPRINT", settings.SshFingerprint) ?? throw new ArgumentException ("Storage inspection requires a verified SSH fingerprint."), cancellation.Token);
				break;
			case "devices":
				result = (await client.GetDevicesAsync (cancellation.Token)).Select (d => new { d.Id, d.Name, d.Model, d.LocationId });
				break;
			case "eligibility":
				result = await client.GetDriverUpdateEligibilityAsync (driverId!, cancellation.Token);
				break;
			case "plan-update":
				result = await client.PlanDriverUpdateAsync (driverId!, cancellation.Token);
				await using (var stream = new FileStream (output!, FileMode.CreateNew, FileAccess.Write, FileShare.None))
					await JsonSerializer.SerializeAsync (stream, result, jsonOptions, cancellation.Token);
				break;
			case "reload-scope":
				result = await client.GetReloadAffectedDevicesAsync (deviceId, cancellation.Token);
				break;
			case "refresh":
				mutationSubmitted = true;
				operationId = await client.BeginLocalDriverRefreshAsync (cancellation.Token);
				result = null;
				break;
			case "reload":
				mutationSubmitted = true;
				operationId = await client.BeginReloadDriverAsync (deviceId, cancellation.Token);
				result = null;
				break;
			case "update":
				mutationSubmitted = true;
				operationId = await client.BeginDriverUpdateAsync (plan!, cancellation.Token);
				result = null;
				break;
			default:
				throw new ArgumentException ("Unknown command.");
			}
		if (operationId != null)
			{
			Console.Error.WriteLine ($"Submitted operation {operationId}; waiting for outcome.");
			var completedOperation = await client.WaitForOperationAsync (operationId, timeout, cancellation.Token);
			result = completedOperation;
			if (command == "update" && completedOperation.Status != "Failed")
				{
				var instances = await client.WaitForDriverVersionAsync (plan!.Eligibility.EligibleDeviceIds!, plan.Eligibility.AvailableDriverVersion!, timeout, cancellation.Token);
				result = new
					{
					Operation = completedOperation,
					Verified = true,
					Instances = instances
					};
				}
			}
		mutationStopped = true;
		Console.WriteLine (JsonSerializer.Serialize (result, jsonOptions));
		return result is OperationResult operation && !operation.Succeeded ? operation.Status == "Failed" ? 1 : 3 : 0;
		}
	catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
		{
		Console.Error.WriteLine ("Cancelled locally. A submitted processor operation may still be running.");
		return 130;
		}
	catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
		{
		Console.Error.WriteLine ("Timed out. A submitted processor operation has an unknown outcome; inspect before retrying.");
		return 3;
		}
	catch (ArgumentException exception) { Console.Error.WriteLine (exception.Message); return 2; }
	catch (ProcessorBusyException exception) { Console.Error.WriteLine (exception.Message); return 3; }
	catch (JsonException) { Console.Error.WriteLine ("Invalid JSON settings, plan or processor response."); return 2; }
	catch (ProcessorApiException exception) { Console.Error.WriteLine (exception.Message); return 1; }
	catch (InvalidOperationException exception) { Console.Error.WriteLine (exception.Message); return 1; }
	catch (Exception) { Console.Error.WriteLine ("Connection or file operation failed. Check connectivity, trusted certificate, credentials and file access."); return 1; }
	finally
		{
		try
			{
			if (lease != null && (!mutationSubmitted || mutationStopped))
				{
				using var cleanup = new CancellationTokenSource (TimeSpan.FromSeconds (20));
				await lease.ReleaseAsync (cleanup.Token);
				if (leaseReceiptPath != null) await File.WriteAllTextAsync (leaseReceiptPath, JsonSerializer.Serialize (new { Owner = lease.Owner, State = "Released" }));
				}
			else if (lease != null) Console.Error.WriteLine ("Processor lease retained: submitted operation or reboot startup has not been confirmed stopped. Inspect the private receipt and processor before releasing it.");
			}
		catch { throw new IOException ("Processor lease release could not be confirmed; inspect the private receipt before another operation."); }
		finally { lease?.Dispose (); Console.CancelKeyPress -= cancelHandler; }
		}
	}

internal sealed record ConsoleSettings
	{
	public string? Host
		{
		get; init;
		}
	public string? SystemName
		{
		get; init;
		}
	public string? UserName
		{
		get; init;
		}
	public string? SshFingerprint
		{
		get; init;
		}
	public string? Password
		{
		get; init;
		}
	public string? CertificateSha256
		{
		get; init;
		}
	public int HttpsPort { get; init; } = 443;
	public int WebSocketPort { get; init; } = 49000;
	}