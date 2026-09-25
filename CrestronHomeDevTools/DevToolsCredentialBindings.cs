// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Runtime.Versioning;
using System.Text.Json;

namespace CrestronHomeDevTools;

/// <summary>Private configuration references to saved entries. Contains no passwords or signature image.</summary>
public sealed record DevToolsCredentialBindings (string StoreDirectory, string? Smtp = null, string? Windows = null,
	string? Uploader = null, string? Processor = null, string? Signature = null)
	{
	public static DevToolsCredentialBindings Read (string path)
		{
		// The same --credentials input can refer directly to an encrypted setup snapshot.
		// No intermediate credential file or environment variables are needed.
		if (Path.GetExtension (path).Equals (".setup", StringComparison.OrdinalIgnoreCase))
			{
			if (!OperatingSystem.IsWindows ())
				throw new PlatformNotSupportedException ("Encrypted setup snapshots require Windows.");
			string filename = Path.GetFileNameWithoutExtension (path);
			if (!Path.IsPathFullyQualified (path) || !filename.StartsWith ("snapshot-", StringComparison.Ordinal))
				throw new ArgumentException ("Select an absolute encrypted submission snapshot path.");
			return DevToolsPrivateStore.Open (Path.GetDirectoryName (path)!).GetSubmissionSetupBindings (filename[9..]);
			}
		using var file = File.OpenRead (path);
		if (file.Length > 65536)
			throw new InvalidDataException ("Credential bindings exceed their size limit.");
		var value = JsonSerializer.Deserialize<DevToolsCredentialBindings> (file, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
			?? throw new InvalidDataException ("Credential bindings are empty.");
		if (!Path.IsPathFullyQualified (value.StoreDirectory))
			throw new ArgumentException ("Use an absolute private-store directory.");
		return value;
		}

	[SupportedOSPlatform ("windows")]
	public DevToolsStoredCredential Resolve (DevToolsCredentialPurpose purpose, string expectedHost, int? expectedPort = null, string? expectedSender = null)
		{
		string? name = purpose switch
			{
				DevToolsCredentialPurpose.Smtp => Smtp,
				DevToolsCredentialPurpose.Windows => Windows,
				DevToolsCredentialPurpose.Uploader => Uploader,
				DevToolsCredentialPurpose.Processor => Processor,
				_ => throw new ArgumentOutOfRangeException (nameof (purpose))
				};
		if (string.IsNullOrWhiteSpace (name))
			throw new InvalidOperationException ($"Configure a named {purpose} entry before this operation.");
		var credential = DevToolsPrivateStore.Open (StoreDirectory).LoadCredential (name, purpose, expectedHost);
		if (expectedPort != null && credential.Port != expectedPort || expectedSender != null && !string.Equals (credential.Sender, expectedSender, StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException ("Saved credentials do not match the selected port or sender.");
		return credential;
		}
	}
