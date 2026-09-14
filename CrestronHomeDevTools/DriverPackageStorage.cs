// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Net;
using System.Text.Json;

using Renci.SshNet;

namespace CrestronHomeDevTools;

/// <summary>A stored package and conservative references; absence of matches is not deletion authorization.</summary>
public sealed record StoredDriverPackage (string Path, long Bytes, DateTime ModifiedUtc, DriverPackageInfo? Package,
	 int[] MatchingInstalledDeviceIds, string[] MatchingCatalogueIds, string? InspectionError);

public static class DriverPackageStorage
	{
	public const string StorageDirectory = "/user/ThirdPartyDrivers/Storage/Rad";

	/// <summary>Read stored package manifests over pinned SFTP without extracting or loading assemblies.</summary>
	public static async Task<IReadOnlyList<StoredDriverPackage>> InspectAsync (ConfigurationClient configuration,
		 string host, NetworkCredential credential, string sshFingerprint, CancellationToken cancellationToken = default)
		{
		ArgumentNullException.ThrowIfNull (configuration);
		ArgumentNullException.ThrowIfNull (credential);
		ArgumentException.ThrowIfNullOrWhiteSpace (sshFingerprint);
		var devices = await configuration.GetDevicesAsync (cancellationToken).ConfigureAwait (false);
		var catalogue = await configuration.GetDriversAsync (cancellationToken: cancellationToken).ConfigureAwait (false);
		using var sftp = new SftpClient (host, credential.UserName, credential.Password);
		sftp.ConnectionInfo.Timeout = TimeSpan.FromSeconds (10);
		sftp.OperationTimeout = TimeSpan.FromSeconds (15);
		sftp.HostKeyReceived += (_, e) => e.CanTrust = e.FingerPrintSHA256 == sshFingerprint;
		await sftp.ConnectAsync (cancellationToken).ConfigureAwait (false);
		var result = new List<StoredDriverPackage> ();
		await foreach (var entry in sftp.ListDirectoryAsync (StorageDirectory, cancellationToken).ConfigureAwait (false))
			{
			if (entry.Name is "." or "..") continue;
			cancellationToken.ThrowIfCancellationRequested ();
			DriverPackageInfo? package = null;
			string? error = null;
			if (!entry.IsRegularFile || entry.IsSymbolicLink || !entry.Name.EndsWith (".pkg", StringComparison.OrdinalIgnoreCase) || entry.Length > 64 * 1024 * 1024)
				error = "Not inspected: expected a regular .pkg file no larger than 64 MiB.";
			else
				{
				try
					{
					await using var input = await sftp.OpenAsync (entry.FullName, FileMode.Open, FileAccess.Read, cancellationToken).ConfigureAwait (false);
					package = DriverDeployment.Inspect (input);
					}
				catch (Exception exception) when (exception is InvalidDataException or JsonException)
					{
					error = "Package metadata could not be interpreted; retain this entry for inspection.";
					}
				}
			result.Add (Describe (entry.FullName, entry.Length, entry.LastWriteTimeUtc, package, devices, catalogue, error));
			}
		return result.OrderBy (entry => entry.Path, StringComparer.Ordinal).ToArray ();
		}

	internal static StoredDriverPackage Describe (string path, long bytes, DateTime modifiedUtc, DriverPackageInfo? package,
		 IEnumerable<DeviceInfo> devices, IEnumerable<DriverInfo> catalogue, string? error = null)
		{
		// Device inventory does not consistently expose a driver GUID/manufacturer. Protect every
		// same-model instance, including other versions; never claim an entry is unused from filenames.
		var instances = package == null ? [] : devices.Where (d => string.Equals (d.Model?.Trim (), package.Model.Trim (), StringComparison.OrdinalIgnoreCase)).Select (d => d.Id).Distinct ().Order ().ToArray ();
		var catalogueIds = package == null ? [] : catalogue.Where (d => DriverDeployment.Matches (package, d)).Select (d => d.Id).Distinct ().Order (StringComparer.Ordinal).ToArray ();
		return new (path, bytes, modifiedUtc, package, instances, catalogueIds, error);
		}
	}