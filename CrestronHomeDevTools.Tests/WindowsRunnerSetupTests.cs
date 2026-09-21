// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

public sealed class WindowsRunnerSetupTests
	{
	private string _root = null!;
	private WindowsRunnerSetupPlan _plan = null!;
	[SetUp]
	public void Setup ()
		{
		_root = Path.Combine (TestContext.CurrentContext.WorkDirectory, "runner-setup-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (_root);
		_plan = new (1, Environment.MachineName, "https://github.com/example/repository", "worker", "2.300.0", new string ('A', 64),
			Path.Combine (_root, "runner"), @"NT AUTHORITY\NETWORK SERVICE", ["processor-tests"]);
		}
	[TearDown]
	public void Cleanup () => Directory.Delete (_root, true);
	private Task<WindowsRunnerSetupState> Apply (FakeOperations operations) => WindowsRunnerSetup.ApplyCoreAsync (_plan, WindowsRunnerSetup.Digest (_plan),
		Path.Combine (_root, "state"), new ("synthetic-registration"), operations, null, default);
	[Test]
	public async Task SuccessfulLocalSetupIsRecheckedWithoutClaimingGitHubOnline ()
		{
		var operations = new FakeOperations ();
		var result = await Apply (operations);
		Assert.That (result.State, Is.EqualTo ("Completed"));
		Assert.That (result.GitHubOnlineVerified || result.WorkloadVerified, Is.False);
		Assert.That ((operations.Installs, operations.Configurations), Is.EqualTo ((1, 1)));
		Assert.That ((await Apply (operations)).State, Is.EqualTo ("Completed"));
		Assert.That (operations.Configurations, Is.EqualTo (1));
		operations.Running = false;
		Assert.That ((await Apply (operations)).State, Is.EqualTo ("InspectionRequired"));
		Assert.That (operations.Configurations, Is.EqualTo (1));
		}
	[Test]
	public async Task InterruptedRegistrationIsNotReplayedEvenWhenRetryHasFreshToken ()
		{
		var operations = new FakeOperations { FailConfigure = true };
		Assert.ThrowsAsync<IOException> (() => Apply (operations));
		operations.FailConfigure = false;
		Assert.That ((await Apply (operations)).State, Is.EqualTo ("InspectionRequired"));
		Assert.That (operations.Configurations, Is.EqualTo (1));
		Assert.That (File.ReadAllText (Path.Combine (_root, "state", "runner-state.json")), Does.Not.Contain ("synthetic-registration"));
		}
	[TestCase (true, false)]
	[TestCase (false, true)]
	public async Task ExistingDirectoryOrServiceCannotBeReplaced (bool directory, bool conflict)
		{
		var operations = new FakeOperations { DirectoryExists = directory, Conflict = conflict };
		Assert.That ((await Apply (operations)).State, Is.EqualTo ("InspectionRequired"));
		Assert.That (operations.Configurations + operations.Installs, Is.Zero);
		}
	[Test]
	public void ChangedPlanAndWrongComputerCannotStartOperations ()
		{
		var operations = new FakeOperations ();
		string digest = WindowsRunnerSetup.Digest (_plan);
		Assert.ThrowsAsync<InvalidOperationException> (() => WindowsRunnerSetup.ApplyCoreAsync (_plan with
			{
			RunnerName = "changed"
			}, digest,
			Path.Combine (_root, "state"), new ("secret"), operations, null, default));
		var other = _plan with
			{
			MachineName = "other-machine"
			};
		Assert.ThrowsAsync<InvalidOperationException> (() => WindowsRunnerSetup.ApplyCoreAsync (other, WindowsRunnerSetup.Digest (other),
			Path.Combine (_root, "state"), new ("secret"), operations, null, default));
		Assert.That (operations.Inspections, Is.Zero);
		}
	[Test]
	public void RegistrationSecretsStayOutOfCommandArgumentsAndLongTokensArePreserved ()
		{
		string token = "ghs_" + new string ('x', 1000);
		var secrets = new WindowsRunnerSetupSecrets (token, "private-service-password");
		var start = WindowsRunnerSetupOperations.ConfigurationStart (_plan, secrets);
		Assert.That (start.Environment["ACTIONS_RUNNER_INPUT_TOKEN"], Is.EqualTo (token));
		Assert.That (start.Environment["ACTIONS_RUNNER_INPUT_WINDOWSLOGONPASSWORD"], Is.EqualTo (secrets.ServicePassword));
		Assert.That (string.Join (' ', start.ArgumentList), Does.Not.Contain (token).And.Not.Contain (secrets.ServicePassword).And.Not.Contain ("--replace"));
		Assert.That (secrets.ToString (), Does.Not.Contain (token));
		}
	[TestCase (false)]
	[TestCase (true)]
	public async Task ArchiveHashAndPathsAreCheckedBeforeAnyExtraction (bool trailingSeparator)
		{
		string archive = Path.Combine (_root, "runner.zip"), target = Path.Combine (_root, "extract");
		if (trailingSeparator)
			target += Path.DirectorySeparatorChar;
		using (var zip = ZipFile.Open (archive, ZipArchiveMode.Create))
			{
			using (var writer = new StreamWriter (zip.CreateEntry ("bin/Runner.Listener.exe").Open ()))
				writer.Write ("synthetic non-executable content");
			using (var writer = new StreamWriter (zip.CreateEntry ("../outside.txt").Open ()))
				writer.Write ("escape");
			}
		string hash = Convert.ToHexString (SHA256.HashData (File.ReadAllBytes (archive)));
		Assert.ThrowsAsync<InvalidDataException> (() => WindowsRunnerSetupOperations.ExtractVerifiedAsync (archive, new string ('0', 64), target, default));
		Assert.ThrowsAsync<InvalidDataException> (() => WindowsRunnerSetupOperations.ExtractVerifiedAsync (archive, hash, target, default));
		Assert.That (Directory.Exists (target), Is.False);
		Assert.That (File.Exists (Path.Combine (_root, "outside.txt")), Is.False);
		string valid = Path.Combine (_root, "valid.zip");
		using (var zip = ZipFile.Open (valid, ZipArchiveMode.Create))
		using (var writer = new StreamWriter (zip.CreateEntry ("bin/Runner.Listener.exe").Open ()))
			writer.Write ("synthetic non-executable content");
		await WindowsRunnerSetupOperations.ExtractVerifiedAsync (valid, Convert.ToHexString (SHA256.HashData (File.ReadAllBytes (valid))), target, default);
		Assert.That (File.Exists (Path.Combine (target, "bin", "Runner.Listener.exe")), Is.True);
		}
	[Test]
	public async Task PrepareCommandCreatesOnlyReviewPlanAndApplyRequiresExplicitFlag ()
		{
		string path = Path.Combine (_root, "plan.json");
		var output = new StringWriter ();
		Assert.That (await WindowsRunnerSetupCommand.RunAsync (["prepare-runner", "--url", _plan.GitHubUrl, "--name", _plan.RunnerName,
			"--version", _plan.Version, "--archive-sha256", _plan.ArchiveSha256, "--directory", _plan.InstallDirectory, "--account", _plan.ServiceAccount,
			"--labels", "processor-tests", "--output", path], output, TextWriter.Null, default), Is.Zero);
		Assert.That (Directory.Exists (_plan.InstallDirectory), Is.False);
		Assert.That (JsonSerializer.Deserialize<WindowsRunnerSetupPlan> (File.ReadAllBytes (path))!.RunnerName, Is.EqualTo (_plan.RunnerName));
		Assert.That (await WindowsRunnerSetupCommand.RunAsync (["apply-runner", "--apply-reviewed", "false"], TextWriter.Null, TextWriter.Null, default), Is.EqualTo (2));
		}
	private sealed class FakeOperations : IWindowsRunnerSetupOperations
		{
		internal int Inspections, Installs, Configurations;
		internal bool DirectoryExists, Registered, Running, Conflict, FailConfigure;
		public Task<WindowsRunnerSetupObservation> InspectAsync (WindowsRunnerSetupPlan plan, CancellationToken token)
			{
			Inspections++;
			return Task.FromResult (new WindowsRunnerSetupObservation (DirectoryExists, Registered, Registered, Running, Registered, Conflict));
			}
		public Task InstallAsync (WindowsRunnerSetupPlan plan, CancellationToken token)
			{
			Installs++;
			DirectoryExists = true;
			return Task.CompletedTask;
			}
		public Task ConfigureAsync (WindowsRunnerSetupPlan plan, WindowsRunnerSetupSecrets secrets, CancellationToken token)
			{
			Configurations++;
			if (FailConfigure)
				throw new IOException ("Interrupted registration");
			Registered = Running = true;
			return Task.CompletedTask;
			}
		}
	}