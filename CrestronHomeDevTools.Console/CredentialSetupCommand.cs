// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

using CrestronHomeDevTools;

internal static class CredentialSetupCommand
	{
	internal static int Run (string[] args, TextWriter output, TextWriter error)
		{
		if (args.Length == 0 || args.SequenceEqual (["--help"]))
			{
			output.WriteLine ("""
            credentials create [--store DIRECTORY] [--service-reader WINDOWS_SID]
              Create a private encrypted store. A service reader explicitly grants access to that account.
            credentials configure --name NAME --kind Processor|Windows|Smtp|Uploader [--store DIRECTORY] [--replace true]
              Prompt privately for a login and endpoint. No connection or email is made.
            credentials import --name NAME [--store DIRECTORY] [--replace true]
              Read one credential JSON object from protected stdin; never print it.
            credentials signature --name NAME --file PRIVATE_IMAGE [--store DIRECTORY] [--replace true]
              Store an encrypted image; does not sign any document.
            credentials list [--store DIRECTORY]
              List saved entry names only.
            credentials provision --name NAME --target-store DIRECTORY [--store DIRECTORY] [--replace true]
              Copy only that entry into an existing local service store; no remote transfer.
            credentials remove --name NAME [--store DIRECTORY]
              Forget the selected saved entry.
            """);
			return 0;
			}
		try
			{
			if (!OperatingSystem.IsWindows ())
				throw new PlatformNotSupportedException ("Encrypted stores require Windows.");
			var options = new Dictionary<string, string> (StringComparer.Ordinal);
			for (int i = 1; i < args.Length; i += 2)
				if (i + 1 >= args.Length || args[i] is not ("--store" or "--service-reader" or "--name" or "--kind" or "--replace" or "--file" or "--target-store") || !options.TryAdd (args[i], args[i + 1]))
					throw new ArgumentException ("Invalid credential setup options.");
			string Required (string key) => options.GetValueOrDefault (key) ?? throw new ArgumentException ($"Missing {key}.");
			string directory = options.GetValueOrDefault ("--store", DevToolsPrivateStore.DefaultDirectory);
			bool replace = options.GetValueOrDefault ("--replace", "false") switch
				{
					"true" => true,
					"false" => false,
					_ => throw new ArgumentException ("Use true or false for replace.")
					};
			if (args[0] == "create")
				{
				_ = DevToolsPrivateStore.Create (directory, options.GetValueOrDefault ("--service-reader"));
				output.WriteLine ("Created encrypted private store. No credentials collected and no operation authorized.");
				return 0;
				}
			var store = DevToolsPrivateStore.Open (directory);
			if (args[0] == "list")
				{
				output.WriteLine (JsonSerializer.Serialize (store.ListNames ()));
				return 0;
				}
			string name = Required ("--name");
			switch (args[0])
				{
				case "configure":
					if (Console.IsInputRedirected)
						throw new ArgumentException ("Configure needs an interactive console. Use import with protected stdin for automation.");
					if (!Enum.TryParse<DevToolsCredentialPurpose> (Required ("--kind"), true, out var kind) || !Enum.IsDefined (kind))
						throw new ArgumentException ("Unknown credential purpose.");
					string Prompt (string title)
						{
						error.Write (title + ": ");
						return Console.ReadLine ()?.Trim () is { Length: > 0 } value ? value : throw new ArgumentException ($"{title} is required.");
						}
					string host = Prompt ("Endpoint hostname or IP"), user = Prompt ("Username");
					error.Write ("Password: ");
					string password = ProfileSetup.ReadPassword ();
					int? port = kind is DevToolsCredentialPurpose.Smtp or DevToolsCredentialPurpose.Windows ? int.Parse (Prompt ("Port")) : null;
					string? sender = kind == DevToolsCredentialPurpose.Smtp ? Prompt ("Approved sender email address") : null;
					string? certificate = kind == DevToolsCredentialPurpose.Processor ? Prompt ("Verified certificate SHA-256") : null;
					string? ssh = kind is DevToolsCredentialPurpose.Processor or DevToolsCredentialPurpose.Windows ? Prompt ("Verified SSH SHA-256 fingerprint") : null;
					store.SaveCredential (name, new (kind, host, user, password, port, sender, certificate, ssh), replace);
					break;
				case "import":
					if (!Console.IsInputRedirected)
						throw new ArgumentException ("Import needs protected JSON on standard input.");
					char[] buffer = new char[16385];
					try
						{
						int count = 0, read;
						while (count < buffer.Length && (read = Console.In.Read (buffer, count, buffer.Length - count)) != 0)
							count += read;
						if (count == 0 || count == buffer.Length)
							throw new ArgumentException ("Missing or oversized credential input.");
						var credential = JsonSerializer.Deserialize<DevToolsStoredCredential> (buffer.AsSpan (0, count), new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter () } })
							?? throw new ArgumentException ("Empty credential input.");
						store.SaveCredential (name, credential, replace);
						}
					finally { Array.Clear (buffer); }
					break;
				case "signature":
					string file = Required ("--file");
					if (new FileInfo (file).Length > 8388608)
						throw new ArgumentException ("Signature image exceeds its size limit.");
					byte[] image = File.ReadAllBytes (file);
					try
						{
						store.SaveSignature (name, image, Path.GetExtension (file), replace);
						}
					finally { CryptographicOperations.ZeroMemory (image); }
					break;
				case "provision":
					store.Provision (name, DevToolsPrivateStore.Open (Required ("--target-store")), replace: replace);
					break;
				case "remove":
					store.Remove (name);
					break;
				default:
					throw new ArgumentException ("Unknown credentials command.");
				}
			output.WriteLine ("Private-store operation completed. No processor command, email or signing operation was performed.");
			return 0;
			}
		catch (Exception failure) when (failure is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException or JsonException or CryptographicException or PlatformNotSupportedException or FormatException)
			{
			error.WriteLine ("Credential setup did not complete. Check the store, account access and requested options. Values are not included in diagnostics. Use credentials --help.");
			return 2;
			}
		}
	}