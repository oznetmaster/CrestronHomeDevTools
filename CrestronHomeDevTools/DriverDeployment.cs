// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

using Renci.SshNet;
using Renci.SshNet.Common;

namespace CrestronHomeDevTools;

public sealed record DriverPackageInfo (string DriverId, string Model, string Manufacturer, string Version);
public sealed record DriverDeploymentResult (DriverPackageInfo Package, string Sha256, string CatalogueId, string RefreshStatus, bool Available);

public static class DriverDeployment
	{
	private const string ImportDirectory = "/user/ThirdPartyDrivers/Import";

	// Examine metadata without extracting files or executing driver code.
	public static DriverPackageInfo Inspect (string path)
		{
		using var input = File.OpenRead (path);
		return Inspect (input);
		}

	internal static DriverPackageInfo Inspect (Stream input)
		{
		using var zip = new ZipArchive (input, ZipArchiveMode.Read, true);
		var manifests = zip.Entries.Where (entry => !entry.FullName.Contains ('/') && !entry.FullName.Contains ('\\') && entry.FullName.EndsWith (".dat", StringComparison.OrdinalIgnoreCase)).ToArray ();
		if (manifests.Length != 1 || manifests[0].Length > 1024 * 1024)
			throw new InvalidDataException ("A driver package must contain one root .dat manifest, at most 1 MiB.");
		using var stream = manifests[0].Open ();
		using var document = JsonDocument.Parse (stream);
		string Required (string key) => document.RootElement.TryGetProperty (key, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace (value.GetString ())
			 ? value.GetString ()! : throw new InvalidDataException ("Driver package metadata is incomplete.");
		var package = new DriverPackageInfo (Required ("driverId"), Required ("baseModel"), Required ("manufacturer"), Required ("driverVersion"));
		if (!Guid.TryParse (package.DriverId, out _) || !Version.TryParse (package.Version, out _))
			throw new InvalidDataException ("The package driver ID or version is invalid.");
		var assemblyName = Path.ChangeExtension (manifests[0].FullName, ".dll");
		if (!zip.Entries.Any (entry => entry.FullName.Equals (assemblyName, StringComparison.OrdinalIgnoreCase)))
			throw new InvalidDataException ("The package is missing its driver assembly.");
		return package;
		}

	public static async Task<string> ReadSshFingerprintAsync (string host, CancellationToken cancellationToken = default)
		{
		string? fingerprint = null;
		// Reject the host key before SSH authentication; no credentials are sent by this probe.
		using var probe = new SftpClient (host, "host-key-probe", string.Empty);
		probe.ConnectionInfo.Timeout = TimeSpan.FromSeconds (10);
		probe.HostKeyReceived += (_, e) => { fingerprint = e.FingerPrintSHA256; e.CanTrust = false; };
		try
			{
			await probe.ConnectAsync (cancellationToken).ConfigureAwait (false);
			}
		catch (SshConnectionException) when (fingerprint != null) { }
		cancellationToken.ThrowIfCancellationRequested ();
		return fingerprint ?? throw new IOException ("The processor SSH host key could not be read.");
		}

	public static async Task<DriverDeploymentResult> DeployAsync (ConfigurationClient configuration, string host, NetworkCredential credential, string sshFingerprint, string packagePath, TimeSpan timeout, CancellationToken cancellationToken = default)
		{
		ArgumentNullException.ThrowIfNull (configuration);
		ArgumentNullException.ThrowIfNull (credential);
		ArgumentException.ThrowIfNullOrWhiteSpace (sshFingerprint);
		if (!packagePath.EndsWith (".pkg", StringComparison.OrdinalIgnoreCase))
			throw new ArgumentException ("Choose a .pkg driver package.");
		if (timeout <= TimeSpan.Zero)
			throw new ArgumentOutOfRangeException (nameof (timeout));
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource (cancellationToken);
		deadline.CancelAfter (timeout);
		// Keep the validated input open so another build cannot replace it during upload.
		await using var input = new FileStream (packagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
		var package = Inspect (input);
		input.Position = 0;
		var digest = Convert.ToHexString (await SHA256.HashDataAsync (input, deadline.Token).ConfigureAwait (false)).ToLowerInvariant ();
		input.Position = 0;
		var destination = ImportDirectory + "/" + Path.GetFileName (packagePath);
		var temporary = ImportDirectory + "/.upload-" + Guid.NewGuid ().ToString ("N") + ".tmp";
		using (var sftp = new SftpClient (host, credential.UserName, credential.Password))
			{
			sftp.ConnectionInfo.Timeout = TimeSpan.FromSeconds (10);
			sftp.OperationTimeout = TimeSpan.FromSeconds (30);
			sftp.HostKeyReceived += (_, e) => e.CanTrust = string.Equals (e.FingerPrintSHA256, sshFingerprint, StringComparison.Ordinal);
			await sftp.ConnectAsync (deadline.Token).ConfigureAwait (false);
			if (await sftp.ExistsAsync (destination, deadline.Token).ConfigureAwait (false))
				throw new IOException ("A package with this filename is already staged. Refresh or inspect the import directory before deploying again.");
			try
				{
				await sftp.UploadFileAsync (input, temporary, false, null, deadline.Token).ConfigureAwait (false);
				var attributes = await sftp.GetAttributesAsync (temporary, deadline.Token).ConfigureAwait (false);
				if (attributes.Size != input.Length)
					throw new IOException ("The uploaded package size did not match.");
				// Expose the .pkg extension only once the complete file is present.
				await sftp.RenameFileAsync (temporary, destination, deadline.Token).ConfigureAwait (false);
				}
			finally
				{
				using var cleanup = new CancellationTokenSource (TimeSpan.FromSeconds (3));
				try
					{
					if (sftp.IsConnected && await sftp.ExistsAsync (temporary, cleanup.Token).ConfigureAwait (false))
						await sftp.DeleteFileAsync (temporary, cleanup.Token).ConfigureAwait (false);
					}
				catch (Exception) { /* An interrupted connection may leave this uniquely named temporary file. */ }
				}
			}
		var operationId = await configuration.BeginLocalDriverRefreshAsync (deadline.Token).ConfigureAwait (false);
		var operation = await configuration.WaitForOperationAsync (operationId, timeout, deadline.Token).ConfigureAwait (false);
		if (operation.Status == "Failed")
			throw new ProcessorApiException ("The package was uploaded, but the processor reported an import failure.");
		while (true)
			{
			var drivers = await configuration.GetDriversAsync (package.Model, deadline.Token).ConfigureAwait (false);
			var matches = drivers.Where (driver => Matches (package, driver)).ToArray ();
			if (matches.Length > 1)
				throw new ProcessorApiException ("Multiple catalogue entries match the uploaded package; inspect the catalogue before updating.");
			if (matches.Length == 1)
				return new (package, digest, matches[0].Id, operation.Status, true);
			await Task.Delay (500, deadline.Token).ConfigureAwait (false);
			}
		}

	internal static bool Matches (DriverPackageInfo package, DriverInfo driver) =>
		 string.Equals (package.Model, driver.Model, StringComparison.OrdinalIgnoreCase)
		 && string.Equals (package.Manufacturer, driver.Manufacturer, StringComparison.OrdinalIgnoreCase)
		 && DriverVersions.Equal (package.Version, driver.Version) && driver.AvailabilityState == "LocalByUser";
	}