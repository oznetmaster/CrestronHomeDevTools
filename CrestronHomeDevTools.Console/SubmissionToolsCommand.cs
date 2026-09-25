// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace CrestronHomeDevTools;

internal static class SubmissionToolsCommand
	{
	private const string MANIFEST_RESOURCE = "SubmissionRuntimeManifest";

	internal static async Task<int> RunAsync (string[] args, CancellationToken cancellationToken)
		{
		if (args.Length == 0 || args[0] is "--help" or "help" or "-h")
			{
			Console.WriteLine ("""
                Optional driver submission preparation (offline; no upload or email):
                  submission runtime-check          Verify the bundled tools and dependencies.
                  submission build-help             Create help from a driver help profile.
                  submission render-help            Render help using a configured PDF renderer.
                  submission package-help           Stage and verify packaged help documents.
                  submission normalize-package      Normalize the candidate package structure.
                  submission dependency-notices     Generate notices from the dependency inventory.
                  submission coverage-plan          Check the declared official test coverage.
                  submission audit-android          Audit pinned Android run evidence.
                  submission self-test-form         Create a draft or evidence-populated review form.
                  submission prepare-review         Retain validated evidence and unsigned review.
                  submission prepare-review-request Prepare an unsigned request with disclosed omissions.
                  submission sign-self-test-form    Apply an independently authorized signature.
                  submission prepare-signed-review  Validate and retain the signed review.
                  submission prepare-delivery       Prepare an independently approved delivery plan.
                  submission revalidate-delivery    Revalidate retained artifacts before delivery.

                Append --help to any command for its inputs. Use private output directories.
                These commands do not certify a driver or send anything to Crestron.
                Actual delivery uses the separate protected submission-deliver command.
                Signing can replace --signature-image with a final --credentials PRIVATE_BINDINGS pair.
                PRIVATE_BINDINGS may also be an absolute encrypted snapshot-NAME.setup path from the Setup app.
                The exact reviewed-form authorization is still required; saving a signature grants no approval.
                """);
			return 0;
			}
		try
			{
			var assembly = typeof (SubmissionToolsCommand).Assembly;
			var directory = await VerifyInstalledBundleAsync (cancellationToken);
			var commands = JsonSerializer.Deserialize<Dictionary<string, string>> (await File.ReadAllTextAsync (Path.Combine (directory, "scripts", "commands.json"), cancellationToken))!;
			if (args[0] != "runtime-check" && !commands.ContainsKey (args[0]))
				throw new ArgumentException ("Unknown submission command. Run submission --help.");
			using var privateInput = SubmissionSignatureInput.Prepare (args);
			var start = new ProcessStartInfo (Path.Combine (directory, "runtime", "python.exe"))
				{
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardInput = true
				};
			foreach (var argument in new[] { "-I", "-B", "-X", "utf8", Path.Combine (directory, "scripts", "bundled_entry.py") }.Concat (privateInput.Arguments))
				start.ArgumentList.Add (argument);
			// Overwrite inherited redirection. Input files cannot substitute a different validator.
			start.Environment["CRESTRON_DEVTOOLS_BUNDLED_VALIDATOR"] = JsonSerializer.Serialize (ValidatorCommand (assembly));
			using var process = Process.Start (start) ?? throw new IOException ("Cannot start the bundled submission tools.");
			try
				{
				if (privateInput.Image != null)
					await process.StandardInput.BaseStream.WriteAsync (privateInput.Image, cancellationToken);
				process.StandardInput.Close ();
				await process.WaitForExitAsync (cancellationToken);
				return process.ExitCode;
				}
			finally
				{
				if (!process.HasExited)
					process.Kill (entireProcessTree: true);
				await process.WaitForExitAsync (CancellationToken.None);
				}
			}
		catch (OperationCanceledException)
			{
			Console.Error.WriteLine ("Submission command stopped. Inspect retained outputs before resuming; no action was retried.");
			return 130;
			}
		catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or JsonException or Win32Exception or CryptographicException or PlatformNotSupportedException or InvalidOperationException)
			{
			Console.Error.WriteLine (exception is InvalidDataException or ArgumentException ? exception.Message : "Cannot read or start the submission tools. Extract a complete console download into a new folder and check file access.");
			return 2;
			}
		}

	internal static async Task<string> VerifyInstalledBundleAsync (CancellationToken cancellationToken)
		{
		using var manifest = typeof (SubmissionToolsCommand).Assembly.GetManifestResourceStream (MANIFEST_RESOURCE)
			?? throw new InvalidDataException ("Submission tools are not included in this build. Use the complete Windows console download or tools/BuildSubmissionConsole.ps1.");
		var directory = Path.Combine (AppContext.BaseDirectory, "submission-tools");
		await VerifyBundleAsync (directory, manifest, cancellationToken);
		return directory;
		}

	private static string[] ValidatorCommand (Assembly assembly)
		{
		var host = Environment.ProcessPath ?? throw new IOException ("Cannot locate the DevTools validator.");
		return Path.GetFileNameWithoutExtension (host).Equals ("dotnet", StringComparison.OrdinalIgnoreCase)
			? [host, assembly.Location] : [host];
		}

	internal static async Task VerifyBundleAsync (string directory, Stream trustedManifest, CancellationToken cancellationToken)
		{
		var manifest = await JsonSerializer.DeserializeAsync<BundleManifest> (trustedManifest, cancellationToken: cancellationToken)
			?? throw new InvalidDataException ("Missing submission runtime inventory.");
		if (manifest.schemaVersion != 1 || manifest.platform != "win-x64" || manifest.files is null || manifest.files.Length == 0)
			throw new InvalidDataException ("Unsupported submission runtime inventory.");
		var root = Path.GetFullPath (directory) + Path.DirectorySeparatorChar;
		var expected = new HashSet<string> (StringComparer.OrdinalIgnoreCase);
		foreach (var file in manifest.files)
			{
			cancellationToken.ThrowIfCancellationRequested ();
			if (file is null || string.IsNullOrEmpty (file.path) || file.path.Contains ('\\') || file.path.Contains (':') || Path.IsPathRooted (file.path)
				|| file.path.Split ('/').Any (part => part is "" or "." or ".." || part.EndsWith ('.') || part.EndsWith (' '))
				|| string.IsNullOrEmpty (file.sha256) || file.sha256.Length != 64
				|| file.sha256.Any (character => !char.IsAsciiHexDigit (character))
				|| !expected.Add (file.path))
				throw new InvalidDataException ("Invalid submission runtime inventory path.");
			var full = Path.GetFullPath (Path.Combine (directory, file.path));
			if (!full.StartsWith (root, StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException ("Submission runtime path escapes its installation.");
			RejectLinks (full, directory);
			using var input = File.OpenRead (full);
			if (!Convert.ToHexStringLower (await SHA256.HashDataAsync (input, cancellationToken)).Equals (file.sha256, StringComparison.Ordinal))
				throw new InvalidDataException ("Submission tools differ from this console build. Extract the complete download into a new folder.");
			}
		// Unexpected modules must not shadow standard or third-party imports.
		foreach (var path in EnumerateFiles (directory))
			{
			var relative = Path.GetRelativePath (directory, path).Replace ('\\', '/');
			if (relative != "manifest.json" && !expected.Contains (relative))
				throw new InvalidDataException ("Unexpected file in submission tools. Extract the complete download into a new folder.");
			}
		}

	private static IEnumerable<string> EnumerateFiles (string directory)
		{
		foreach (var path in Directory.EnumerateFileSystemEntries (directory))
			{
			if ((File.GetAttributes (path) & FileAttributes.ReparsePoint) != 0)
				throw new InvalidDataException ("Linked submission runtime paths are not supported.");
			if (Directory.Exists (path))
				{
				foreach (var child in EnumerateFiles (path))
					yield return child;
				}
			else
				yield return path;
			}
		}

	private static void RejectLinks (string path, string directory)
		{
		var stop = Path.GetFullPath (directory);
		for (var current = path; current != null; current = Path.GetDirectoryName (current))
			{
			if ((File.GetAttributes (current) & FileAttributes.ReparsePoint) != 0)
				throw new InvalidDataException ("Linked submission runtime paths are not supported.");
			if (current.Equals (stop, StringComparison.OrdinalIgnoreCase))
				break;
			}
		}

	private sealed record BundleManifest (int schemaVersion, string platform, BundleFile[] files);
	private sealed record BundleFile (string path, string sha256);
	}
