// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestronHomeDevTools;

public enum DevToolsResourceKind
	{
	CrestronProcessor, WindowsComputer
	}
public enum DevToolsResourceState
	{
	Planned, Ready, Disabled
	}
public enum DevToolsResourceRole
	{
	Development, ProcessorTests, SubmissionTests, Endurance, Build, GitHubRunner, AndroidEmulator, ConfigureProUi, Notifications, SubmissionDelivery, GeneralAutomation
	}

/// <summary>A capability verified by the operator or a tool, with its retained evidence reference.</summary>
public sealed record DevToolsVerifiedCapability (string Name, DateTimeOffset VerifiedUtc, string EvidenceReference);

/// <summary>A configured machine. Credential references are names, never passwords or signature material.</summary>
public sealed record DevToolsResource (
	string Name, DevToolsResourceKind Kind, DevToolsResourceState State, string? Address,
	IReadOnlyList<DevToolsResourceRole> Roles, IReadOnlyList<DevToolsVerifiedCapability> Capabilities,
	IReadOnlyDictionary<string, string> CredentialReferences, bool SharedWithRealUse = false);

/// <summary>Private machine inventory; selection does not acquire execution leases or authorize operations.</summary>
public sealed record DevToolsResourceInventory (int SchemaVersion, IReadOnlyList<DevToolsResource> Resources)
	{
	private static readonly JsonSerializerOptions Json = new () { WriteIndented = true, Converters = { new JsonStringEnumConverter () } };
	public static DevToolsResourceInventory Load (string path)
		{
		using var file = File.OpenRead (path);
		if (file.Length > 1048576)
			throw new InvalidDataException ("Resource inventory exceeds its size limit.");
		var inventory = JsonSerializer.Deserialize<DevToolsResourceInventory> (file, Json) ?? throw new InvalidDataException ("Empty resource inventory.");
		inventory.Validate ();
		return inventory;
		}

	public void Save (string path, bool replace = false)
		{
		Validate ();
		string temporary = path + "." + Guid.NewGuid ().ToString ("N") + ".tmp";
		try
			{
			using (var file = new FileStream (temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				{
				JsonSerializer.Serialize (file, this, Json);
				file.Flush (true);
				}
			File.Move (temporary, path, replace);
			}
		finally { if (File.Exists (temporary)) File.Delete (temporary); }
		}

	public void Validate ()
		{
		if (SchemaVersion != 1 || Resources == null)
			throw new ArgumentException ("Unsupported resource inventory.");
		var names = new HashSet<string> (StringComparer.OrdinalIgnoreCase);
		foreach (var resource in Resources)
			{
			if (resource == null || string.IsNullOrWhiteSpace (resource.Name) || !names.Add (resource.Name) ||
				!Enum.IsDefined (resource.Kind) || !Enum.IsDefined (resource.State) || resource.Roles == null || resource.Capabilities == null || resource.CredentialReferences == null)
				throw new ArgumentException ("Resources need unique names, a known kind/state, roles, capabilities and credential references.");
			if (resource.State == DevToolsResourceState.Ready && string.IsNullOrWhiteSpace (resource.Address))
				throw new ArgumentException ("A ready resource needs an address or resolvable machine name.");
			foreach (var role in resource.Roles)
				{
				bool processorRole = role is DevToolsResourceRole.Development or DevToolsResourceRole.ProcessorTests or DevToolsResourceRole.SubmissionTests or DevToolsResourceRole.Endurance;
				bool windowsRole = role is not (DevToolsResourceRole.ProcessorTests or DevToolsResourceRole.SubmissionTests);
				if (!Enum.IsDefined (role) || (resource.Kind == DevToolsResourceKind.CrestronProcessor ? !processorRole : !windowsRole))
					throw new ArgumentException ("The role is incompatible with the resource kind.");
				}
			if (resource.Capabilities.Any (c => c == null || string.IsNullOrWhiteSpace (c.Name) || string.IsNullOrWhiteSpace (c.EvidenceReference) || c.VerifiedUtc == default))
				throw new ArgumentException ("Verified capabilities need a name, observation time and evidence reference.");
			foreach (var reference in resource.CredentialReferences)
				if (string.IsNullOrWhiteSpace (reference.Key) || string.IsNullOrWhiteSpace (reference.Value) || reference.Value.Length > 64 || !reference.Value.All (c => char.IsAsciiLetterOrDigit (c) || c is '-' or '_'))
					throw new ArgumentException ("Use named private-store references, not credential values.");
			}
		}

	/// <summary>Choose one ready, permitted and verified resource; ambiguity requires an explicit name.</summary>
	public DevToolsResource Select (DevToolsResourceKind kind, DevToolsResourceRole role, IEnumerable<string> requiredCapabilities, string? name = null)
		{
		Validate ();
		string[] required = requiredCapabilities.ToArray ();
		var matches = Resources.Where (r => r.Kind == kind && r.State == DevToolsResourceState.Ready && r.Roles.Contains (role) &&
			(name == null || r.Name.Equals (name, StringComparison.OrdinalIgnoreCase)) &&
			required.All (c => r.Capabilities.Any (v => v.Name.Equals (c, StringComparison.OrdinalIgnoreCase)))).ToArray ();
		return matches.Length switch
			{
				1 => matches[0],
				0 => throw new InvalidOperationException ("No ready resource matches the requested role and verified capabilities."),
				_ => throw new InvalidOperationException ("Several resources match; select one by name.")
				};
		}
	}