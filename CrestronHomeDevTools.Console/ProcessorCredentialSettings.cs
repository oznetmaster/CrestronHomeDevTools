// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using CrestronHomeDevTools;

internal static class ProcessorCredentialSettings
	{
	internal static ConsoleSettings Resolve (string bindingsPath, string host, ConsoleSettings settings)
		{
		if (!OperatingSystem.IsWindows ())
			throw new PlatformNotSupportedException ("Named encrypted credentials require Windows.");
		if (settings.UserName != null || settings.Password != null || settings.CertificateSha256 != null || settings.SshFingerprint != null)
			throw new ArgumentException ("With named credentials, connection settings may contain only the target and ports; keep login and trust in the private store.");
		var credential = DevToolsCredentialBindings.Read (bindingsPath).Resolve (DevToolsCredentialPurpose.Processor, host);
		if ((credential.Port ?? 443) != settings.HttpsPort || string.IsNullOrWhiteSpace (credential.CertificateSha256))
			throw new InvalidOperationException ("Saved processor credentials need a verified certificate and matching HTTPS port.");
		return settings with
			{
			Host = host,
			UserName = credential.UserName,
			Password = credential.Password,
			CertificateSha256 = credential.CertificateSha256,
			SshFingerprint = credential.SshFingerprint
			};
		}
	}