// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Security.Cryptography;

using CrestronHomeDevTools;

internal sealed class SubmissionSignatureInput (string[] arguments, byte[]? image) : IDisposable
	{
	internal string[] Arguments { get; } = arguments;
	internal byte[]? Image { get; } = image;
	internal static SubmissionSignatureInput Prepare (string[] args)
		{
		int index = Array.IndexOf (args, "--credentials");
		if (index < 0)
			return new (args, null);
		if (args[0] is not ("sign-self-test-form" or "prepare-signed-review") || index != args.Length - 2 || args.Contains ("--signature-image") || args.Contains ("--signature-stdin"))
			throw new ArgumentException ("Signing credentials must be a final --credentials PRIVATE_BINDINGS pair, replacing --signature-image.");
		if (!OperatingSystem.IsWindows ())
			throw new PlatformNotSupportedException ("Named encrypted signatures require Windows.");
		var bindings = DevToolsCredentialBindings.Read (args[index + 1]);
		if (string.IsNullOrWhiteSpace (bindings.Signature))
			throw new ArgumentException ("Configure a named signature in the private bindings.");
		var signature = DevToolsPrivateStore.Open (bindings.StoreDirectory).LoadSignature (bindings.Signature);
		return new ([.. args[..index], "--signature-stdin"], signature.Image);
		}
	public void Dispose ()
		{
		if (Image != null)
			CryptographicOperations.ZeroMemory (Image);
		}
	}