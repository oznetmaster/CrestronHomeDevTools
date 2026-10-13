// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Security.Cryptography;
using System.Text.Json;

using CrestronHomeDevTools;

internal sealed class SubmissionSignatureInput (string[] arguments, byte[]? image) : IDisposable
	{
	internal string[] Arguments { get; } = arguments;
	internal byte[]? Image { get; } = image;
	internal static SubmissionSignatureInput Prepare (string[] args)
		{
		// Planning records a binding path but must never resolve or decrypt it.
		if (args.Length > 0 && args[0] == "prepare-signing-plan") return new (args, null);
		int index = Array.IndexOf (args, "--credentials");
		if (index < 0)
			return new (args, null);
		if (args[0] is not ("sign-self-test-form" or "prepare-signed-review") || index != args.Length - 2 || args.Contains ("--signature-image") || args.Contains ("--signature-stdin"))
			throw new ArgumentException ("Signing credentials must be a final --credentials PRIVATE_BINDINGS pair, replacing --signature-image.");
		if (args[0] == "prepare-signed-review") CheckReviewIdentity (args);
		if (!OperatingSystem.IsWindows ())
			throw new PlatformNotSupportedException ("Named encrypted signatures require Windows.");
		var bindings = DevToolsCredentialBindings.Read (args[index + 1]);
		if (string.IsNullOrWhiteSpace (bindings.Signature))
			throw new ArgumentException ("Configure a named signature in the private bindings.");
		var signature = DevToolsPrivateStore.Open (bindings.StoreDirectory).LoadSignature (bindings.Signature);
		return new ([.. args[..index], "--signature-stdin"], signature.Image);
		}
    internal static void CheckReviewIdentity(string[] args)
        {
        string Option(string name)
            {
            int index=Array.IndexOf(args,name);
            if(index<0 || index+1>=args.Length || Array.LastIndexOf(args,name)!=index)
                throw new ArgumentException("Supply each exact signing input once.");
            return args[index+1];
            }
        byte[] Read(string path)
            {
            using var stream=File.OpenRead(path);
            if(stream.Length>8*1024*1024)throw new InvalidDataException("Signing metadata exceeds its bound.");
            var bytes=new byte[checked((int)stream.Length)];stream.ReadExactly(bytes);return bytes;
            }
        void Pinned(byte[] bytes,string digest)
            {
            if(digest.Length!=64 || !digest.All(Uri.IsHexDigit) ||
                !CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes),Convert.FromHexString(digest)))
                throw new InvalidDataException("Signing receipt or authorization differs from its reviewed digest; no signature was accessed.");
            }
        using var settings=JsonDocument.Parse(Read(Option("--settings")));
        var names=settings.RootElement.EnumerateObject().Select(p=>p.Name).ToArray();
        if(names.Distinct(StringComparer.Ordinal).Count()!=names.Length)
            throw new InvalidDataException("Ambiguous signing settings.");
        string review=settings.RootElement.GetProperty("reviewDirectory").GetString()!;
        string authorization=settings.RootElement.GetProperty("authorization").GetString()!;
        if(!Path.IsPathFullyQualified(review) || !Path.IsPathFullyQualified(authorization))
            throw new InvalidDataException("Signing inputs require absolute private paths.");
        string reviewDigest=Option("--review-sha256");
        Pinned(Read(Path.Combine(review,"review-receipt.json")),reviewDigest);
        if(System.Text.Encoding.ASCII.GetString(Read(Path.Combine(review,"COMPLETE"))).Trim()!=reviewDigest)
            throw new InvalidDataException("Review completion differs from the approved receipt; no signature was accessed.");
        Pinned(Read(authorization),Option("--authorization-sha256"));
        // Full schema, form/evidence, scope, expiry and signature authorization checks still run in the signing stage.
        }

	public void Dispose ()
		{
		if (Image != null)
			CryptographicOperations.ZeroMemory (Image);
		}
	}