// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestronHomeDevTools;

/// <summary>The purpose of a saved login. Consumers must request the matching purpose and endpoint.</summary>
public enum DevToolsCredentialPurpose
	{
	Processor, Windows, Smtp, Uploader
	}

/// <summary>A private login and its endpoint binding. Never log or serialize this object to public output.</summary>
public sealed record DevToolsStoredCredential (
	DevToolsCredentialPurpose Purpose, string Host, string UserName, string Password,
	int? Port = null, string? Sender = null, string? CertificateSha256 = null, string? SshFingerprint = null)
	{
	public override string ToString () => $"Saved {Purpose} credential (values hidden)";
	}

/// <summary>Windows-encrypted, named private inputs. Saving an entry grants no operation or signing approval.</summary>
[SupportedOSPlatform ("windows")]
public sealed class DevToolsPrivateStore
	{
	private sealed record Settings (int SchemaVersion, bool MachineScope, string OwnerSid, string? ReaderSid);
	private sealed record Entry (int SchemaVersion, string Name, DevToolsStoredCredential? Credential, byte[]? Signature, string? Extension);
	private static readonly JsonSerializerOptions Json = new () { Converters = { new JsonStringEnumConverter () } };
	private static readonly byte[] Entropy = Encoding.UTF8.GetBytes ("CrestronHomeDevTools/private-store/v1");
	private readonly Settings _settings;
	public string DirectoryPath
		{
		get;
		}
	public static string DefaultDirectory => Path.Combine (Environment.GetFolderPath (Environment.SpecialFolder.LocalApplicationData), "CrestronHomeDevTools", "PrivateStore");
	private DevToolsPrivateStore (string directory, Settings settings)
		{
		DirectoryPath = Path.GetFullPath (directory);
		_settings = settings;
		}

	/// <summary>Create a new store. A service reader explicitly selects machine DPAPI plus restricted NTFS access.</summary>
	public static DevToolsPrivateStore Create (string directory, string? serviceReaderSid = null)
		{
		if (!Path.IsPathFullyQualified (directory) || directory.StartsWith (@"\\", StringComparison.Ordinal))
			throw new ArgumentException ("Use an absolute local private-store directory.");
		if (Directory.Exists (directory) || File.Exists (directory))
			throw new IOException ("The private-store directory already exists; open it instead.");
		string owner = WindowsIdentity.GetCurrent ().User?.Value ?? throw new InvalidOperationException ("Windows identity is unavailable.");
		if (serviceReaderSid != null)
			_ = new SecurityIdentifier (serviceReaderSid);
		var settings = new Settings (1, serviceReaderSid != null, owner, serviceReaderSid);
		var acl = new DirectorySecurity ();
		acl.SetAccessRuleProtection (true, false);
		// Only change access rules. Assigning ownership also requires privileges unavailable to ordinary users.
		foreach (string sid in new[] { owner, "S-1-5-18", "S-1-5-32-544" }.Distinct ())
			acl.AddAccessRule (new FileSystemAccessRule (new SecurityIdentifier (sid), FileSystemRights.FullControl,
				InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
		if (serviceReaderSid != null && serviceReaderSid != owner && serviceReaderSid is not ("S-1-5-18" or "S-1-5-32-544"))
			acl.AddAccessRule (new FileSystemAccessRule (new SecurityIdentifier (serviceReaderSid), FileSystemRights.ReadAndExecute,
				InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
		var folder = Directory.CreateDirectory (directory);
		folder.SetAccessControl (acl);
		File.WriteAllBytes (Path.Combine (directory, "store.json"), JsonSerializer.SerializeToUtf8Bytes (settings, Json));
		return new (directory, settings);
		}

	public static DevToolsPrivateStore Open (string? directory = null)
		{
		directory ??= DefaultDirectory;
		var settings = JsonSerializer.Deserialize<Settings> (File.ReadAllBytes (Path.Combine (directory, "store.json")), Json)
			?? throw new InvalidDataException ("Private-store metadata is empty.");
		if (settings.SchemaVersion != 1 || settings.MachineScope != (settings.ReaderSid != null))
			throw new InvalidDataException ("Unsupported private-store metadata.");
		return new (directory, settings);
		}

	/// <summary>List entry names only; passwords, usernames and signature bytes are not returned.</summary>
	public IReadOnlyList<string> ListNames () => Directory.EnumerateFiles (DirectoryPath, "*.private").Select (p => Path.GetFileNameWithoutExtension (p)).Order (StringComparer.Ordinal).ToArray ();

	public void SaveCredential (string name, DevToolsStoredCredential credential, bool replace = false)
		{
		ArgumentNullException.ThrowIfNull (credential);
		if (!Enum.IsDefined (credential.Purpose) || string.IsNullOrWhiteSpace (credential.Host) || string.IsNullOrWhiteSpace (credential.UserName) || string.IsNullOrEmpty (credential.Password))
			throw new ArgumentException ("A credential needs a purpose, endpoint, username and password.");
		if (credential.Port is < 1 or > 65535)
			throw new ArgumentException ("Invalid endpoint port.");
		Save (name, new (1, name, credential, null, null), replace);
		}

	public DevToolsStoredCredential LoadCredential (string name, DevToolsCredentialPurpose purpose, string expectedHost)
		{
		var entry = Load (name);
		if (entry.Credential == null || entry.Credential.Purpose != purpose || !string.Equals (entry.Credential.Host, expectedHost, StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException ("The saved credential does not match the requested purpose and endpoint.");
		return entry.Credential;
		}

	/// <summary>Store a private signature image without applying it to any document.</summary>
	public void SaveSignature (string name, byte[] image, string extension, bool replace = false)
		{
		ArgumentNullException.ThrowIfNull (image);
		if (image.Length is 0 or > 8388608 || extension.ToLowerInvariant () is not (".jpg" or ".jpeg" or ".png"))
			throw new ArgumentException ("Choose a JPG or PNG signature image of at most 8 MiB.");
		Save (name, new (1, name, null, image, extension.ToLowerInvariant ()), replace);
		}

	/// <summary>Return private image bytes for an independently authorized signing operation. Clear them after use.</summary>
	public (byte[] Image, string Extension) LoadSignature (string name)
		{
		var entry = Load (name);
		if (entry.Signature == null || entry.Extension == null)
			throw new InvalidOperationException ("This entry is not a signature.");
		return (entry.Signature, entry.Extension);
		}

	/// <summary>Explicitly provision one named entry into another local store. Never copies the entire store.</summary>
	public void Provision (string name, DevToolsPrivateStore destination, string? destinationName = null, bool replace = false)
		{
		ArgumentNullException.ThrowIfNull (destination);
		var entry = Load (name);
		destinationName ??= name;
		try
			{
			destination.Save (destinationName, entry with
				{
				Name = destinationName
				}, replace);
			}
		finally { if (entry.Signature != null) CryptographicOperations.ZeroMemory (entry.Signature); }
		}

	public void Remove (string name) => File.Delete (GetPath (name));

	private string GetPath (string name)
		{
		if (string.IsNullOrEmpty (name) || name.Length > 64 || !name.All (c => char.IsAsciiLetterOrDigit (c) || c is '-' or '_'))
			throw new ArgumentException ("Entry names must contain 1-64 letters, digits, hyphens or underscores.");
		return Path.Combine (DirectoryPath, name + ".private");
		}

	private Entry Load (string name)
		{
		string path = GetPath (name);
		if (new FileInfo (path).Length > 16777216)
			throw new InvalidDataException ("Private entry exceeds its size limit.");
		byte[] plain = ProtectedData.Unprotect (File.ReadAllBytes (path), Entropy, _settings.MachineScope ? DataProtectionScope.LocalMachine : DataProtectionScope.CurrentUser);
		try
			{
			var entry = JsonSerializer.Deserialize<Entry> (plain, Json) ?? throw new InvalidDataException ("Private entry is empty.");
			if (entry.SchemaVersion != 1 || entry.Name != name)
				throw new InvalidDataException ("Private entry identity does not match.");
			return entry;
			}
		finally { CryptographicOperations.ZeroMemory (plain); }
		}

	private void Save (string name, Entry entry, bool replace)
		{
		string path = GetPath (name);
		if (!replace && File.Exists (path))
			throw new IOException ("The entry exists. Explicit replacement is required.");
		byte[] plain = JsonSerializer.SerializeToUtf8Bytes (entry, Json);
		byte[] encrypted;
		try
			{
			encrypted = ProtectedData.Protect (plain, Entropy, _settings.MachineScope ? DataProtectionScope.LocalMachine : DataProtectionScope.CurrentUser);
			}
		finally { CryptographicOperations.ZeroMemory (plain); }
		string temporary = path + "." + Guid.NewGuid ().ToString ("N") + ".tmp";
		try
			{
			using (var file = new FileStream (temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				{
				file.Write (encrypted);
				file.Flush (true);
				}
			File.Move (temporary, path, replace);
			}
		finally { if (File.Exists (temporary)) File.Delete (temporary); }
		}
	}