// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

using CrestronHomeDevTools;

internal sealed class ProfileStore (string? directory = null)
	{
	private readonly string _directory = directory ?? Path.Combine (Environment.GetFolderPath (Environment.SpecialFolder.LocalApplicationData), "CrestronHomeDevTools", "Profiles");
	private static readonly byte[] Entropy = Encoding.UTF8.GetBytes ("CrestronHomeDevTools/profiles/v1");

	public bool Exists (string name) => File.Exists (GetPath (name));

	public string GetPath (string name)
		{
		if (string.IsNullOrWhiteSpace (name) || name.Length > 64 || !name.All (c => char.IsAsciiLetterOrDigit (c) || c is '-' or '_'))
			throw new ArgumentException ("Profile names must contain 1-64 letters, digits, hyphens or underscores.");
		return Path.Combine (_directory, name + ".profile");
		}

	[SupportedOSPlatform ("windows")]
	public ConsoleSettings Load (string name)
		{
		var encrypted = File.ReadAllBytes (GetPath (name));
		var bytes = ProtectedData.Unprotect (encrypted, Entropy, DataProtectionScope.CurrentUser);
		try
			{
			return JsonSerializer.Deserialize<ConsoleSettings> (bytes) ?? throw new InvalidDataException ("The saved profile is empty.");
			}
		finally { CryptographicOperations.ZeroMemory (bytes); }
		}

	[SupportedOSPlatform ("windows")]
	public void Save (string name, ConsoleSettings settings)
		{
		var path = GetPath (name);
		var bytes = JsonSerializer.SerializeToUtf8Bytes (settings);
		byte[] encrypted;
		try
			{
			encrypted = ProtectedData.Protect (bytes, Entropy, DataProtectionScope.CurrentUser);
			}
		finally { CryptographicOperations.ZeroMemory (bytes); }
		Directory.CreateDirectory (_directory);
		var temporary = path + "." + Guid.NewGuid ().ToString ("N") + ".tmp";
		try
			{
			File.WriteAllBytes (temporary, encrypted);
			File.Move (temporary, path, true);
			}
		finally { if (File.Exists (temporary)) File.Delete (temporary); }
		}
	}

internal static class ProfileSetup
	{
	public static async Task ConfigureAsync (ProfileStore store, string profileName, string? hostOverride, ConsoleSettings existing, CancellationToken cancellationToken)
		{
		if (!OperatingSystem.IsWindows ())
			throw new PlatformNotSupportedException ("Saved encrypted profiles currently require Windows. Use environment variables on other systems.");
		if (Console.IsInputRedirected)
			throw new ArgumentException ("Configure requires an interactive console; use an existing profile for automation.");
		// Validate the profile name before prompting or making network requests.
		store.GetPath (profileName);
		string host;
		string? systemName;
		if (hostOverride != null)
			{
			if (IPAddress.TryParse (hostOverride, out _))
				{
				host = hostOverride;
				systemName = null;
				}
			else
				{
				var resolved = await ProcessorDiscovery.ResolveAsync (hostOverride, cancellationToken);
				host = resolved.Address;
				systemName = resolved.SystemName;
				}
			}
		else
			{
			Console.Error.WriteLine ("Discovering Crestron Home processors...");
			var processors = await ProcessorDiscovery.FindAsync (cancellationToken);
			if (processors.Count == 0)
				throw new InvalidOperationException ("No Crestron Home processors found. Check the network, or use configure --processor IP for a routed network.");
			for (var index = 0; index < processors.Count; index++)
				Console.Error.WriteLine ($"{index + 1}. {processors[index].SystemName} ({processors[index].Address}) - {processors[index].Model}");
			Console.Error.Write ("Select processor number: ");
			if (!int.TryParse (Console.ReadLine (), out var choice) || choice < 1 || choice > processors.Count)
				throw new ArgumentException ("Invalid processor selection.");
			host = processors[choice - 1].Address;
			systemName = processors[choice - 1].SystemName;
			}
		// Reusing a profile for another processor must not reuse its password or certificate.
		if (host != existing.Host && (systemName == null || systemName != existing.SystemName))
			existing = existing with
				{
				Password = null,
				CertificateSha256 = null,
				SshFingerprint = null
				};
		var user = Prompt ("Username", existing.UserName);
		Console.Error.Write (existing.Password == null ? "Password: " : "Password (Enter keeps saved password): ");
		var password = ReadPassword ();
		if (password.Length == 0)
			password = existing.Password ?? throw new ArgumentException ("A password is required.");
		var pin = existing.CertificateSha256;
		if (pin == null)
			{
			pin = await ReadCertificateFingerprintAsync (host, existing.WebSocketPort, cancellationToken);
			Console.Error.WriteLine ($"Processor: {host}");
			Console.Error.WriteLine ($"Certificate SHA-256: {pin}");
			Console.Error.Write ("Trust this certificate for this processor? Type yes: ");
			if (!string.Equals (Console.ReadLine (), "yes", StringComparison.OrdinalIgnoreCase))
				throw new ArgumentException ("Certificate was not trusted; no profile was saved.");
			}
		var settings = existing with
			{
			Host = host,
			SystemName = systemName,
			UserName = user,
			Password = password,
			CertificateSha256 = pin
			};
		var options = new ProcessorConnectionOptions { Host = host, CertificateSha256 = pin, HttpsPort = settings.HttpsPort, WebSocketPort = settings.WebSocketPort };
		await using (var client = await ConfigurationClient.ConnectAsync (options, new NetworkCredential (user, password), cancellationToken))
			_ = await client.GetDeviceAsync (-6, cancellationToken)
				?? throw new ProcessorApiException ("The processor driver-management service is unavailable.");
		if (settings.SshFingerprint == null)
			{
			var sshFingerprint = await DriverDeployment.ReadSshFingerprintAsync (host, cancellationToken);
			Console.Error.WriteLine ($"SSH SHA-256 fingerprint: {sshFingerprint}");
			Console.Error.Write ("Trust this SSH key for SFTP deployment? Type yes, or Enter to skip: ");
			if (string.Equals (Console.ReadLine (), "yes", StringComparison.OrdinalIgnoreCase))
				settings = settings with
					{
					SshFingerprint = sshFingerprint
					};
			}
		store.Save (profileName, settings);
		Console.WriteLine (JsonSerializer.Serialize (new
			{
			Profile = profileName,
			Saved = true,
			Authenticated = true
			}));
		}

	private static string Prompt (string title, string? current)
		{
		Console.Error.Write (current == null ? title + ": " : $"{title} [{current}]: ");
		var input = Console.ReadLine ();
		if (string.IsNullOrWhiteSpace (input))
			input = current;
		return !string.IsNullOrWhiteSpace (input) ? input : throw new ArgumentException ($"{title} is required.");
		}

	private static string ReadPassword ()
		{
		var value = new StringBuilder ();
		while (true)
			{
			var key = Console.ReadKey (true);
			if (key.Key == ConsoleKey.Enter)
				{
				Console.Error.WriteLine ();
				return value.ToString ();
				}
			if (key.Key == ConsoleKey.Backspace && value.Length > 0)
				{
				value.Length--;
				Console.Error.Write ("\b \b");
				}
			else if (!char.IsControl (key.KeyChar))
				{
				value.Append (key.KeyChar);
				Console.Error.Write ('*');
				}
			}
		}

	private static async Task<string> ReadCertificateFingerprintAsync (string host, int port, CancellationToken cancellationToken)
		{
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource (cancellationToken);
		deadline.CancelAfter (TimeSpan.FromSeconds (15));
		using var tcp = new TcpClient ();
		await tcp.ConnectAsync (host, port, deadline.Token);
		string? fingerprint = null;
		// Inspect a certificate without sending credentials. The operator decides whether to trust it.
		await using var tls = new SslStream (tcp.GetStream (), false, (_, certificate, _, _) =>
		{
			fingerprint = certificate?.GetCertHashString (HashAlgorithmName.SHA256);
			return certificate != null;
		});
		await tls.AuthenticateAsClientAsync (new SslClientAuthenticationOptions { TargetHost = host }, deadline.Token);
		return fingerprint ?? throw new IOException ("Processor did not provide a certificate.");
		}
	}