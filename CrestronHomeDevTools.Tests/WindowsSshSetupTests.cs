// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

public sealed class WindowsSshSetupTests
	{
	private string _directory = null!;
	private WindowsSshSetupPlan _plan = null!;
	[SetUp]
	public void Setup ()
		{
		_directory = Path.Combine (TestContext.CurrentContext.WorkDirectory, "ssh-setup-" + Guid.NewGuid ().ToString ("N"));
		_plan = new (1, Environment.MachineName, "LocalSubnet");
		}
	[TearDown]
	public void Cleanup ()
		{
		if (Directory.Exists (_directory))
			Directory.Delete (_directory, true);
		}
	private Task<WindowsSshSetupState> Apply (FakeOperations operations, bool resume = false) => WindowsSshSetup.ApplyCoreAsync (_plan,
		WindowsSshSetup.Digest (_plan), _directory, operations, null, CancellationToken.None, resume);
	[Test]
	public async Task RestartStopsLaterStepsAndSamePlanResumesAfterObservedReboot ()
		{
		var operations = new FakeOperations { RestartRequired = true };
		Assert.That ((await Apply (operations)).State, Is.EqualTo ("RestartRequired"));
		Assert.That ((operations.Installs, operations.Services, operations.Firewalls), Is.EqualTo ((1, 0, 0)));
		Assert.That ((await Apply (operations)).State, Is.EqualTo ("RestartRequired"));
		Assert.That (operations.Services, Is.Zero);
		operations.Boot = operations.Boot.AddMinutes (5);
		Assert.That ((await Apply (operations)).State, Is.EqualTo ("Completed"));
		Assert.That ((operations.Installs, operations.Services, operations.Firewalls), Is.EqualTo ((1, 1, 1)));
		await Apply (operations);
		Assert.That ((operations.Installs, operations.Services, operations.Firewalls), Is.EqualTo ((1, 1, 1)));
		}
	[Test]
	public async Task InterruptedInstallationRequiresInspectionBeforeRetrying ()
		{
		var operations = new FakeOperations { FailInstall = true };
		Assert.ThrowsAsync<IOException> (() => Apply (operations));
		Assert.That (JsonSerializer.Deserialize<WindowsSshSetupState> (File.ReadAllBytes (Path.Combine (_directory, "state.json")))!.State, Is.EqualTo ("Applying"));
		operations.FailInstall = false;
		Assert.That ((await Apply (operations)).State, Is.EqualTo ("InspectionRequired"));
		Assert.That (operations.Installs, Is.EqualTo (1));
		Assert.That ((await Apply (operations, resume: true)).State, Is.EqualTo ("Completed"));
		Assert.That (operations.Installs, Is.EqualTo (2));
		}
	[Test]
	public void WrongMachineOrChangedPlanCannotInspectOrMutate ()
		{
		var operations = new FakeOperations ();
		string reviewed = WindowsSshSetup.Digest (_plan);
		Assert.ThrowsAsync<InvalidOperationException> (() => WindowsSshSetup.ApplyCoreAsync (_plan with { RemoteAddress = "192.0.2.5" }, reviewed, _directory, operations, null, default));
		var wrong = _plan with
			{
			MachineName = "other-computer"
			};
		Assert.ThrowsAsync<InvalidOperationException> (() => WindowsSshSetup.ApplyCoreAsync (wrong, WindowsSshSetup.Digest (wrong), _directory, operations, null, default));
		Assert.That (operations.Inspections + operations.Installs, Is.Zero);
		}
	[Test]
	public async Task CompletedJournalIsRecheckedInsteadOfAssumingServiceStillRuns ()
		{
		var operations = new FakeOperations ();
		await Apply (operations);
		operations.Service = false;
		await Apply (operations);
		Assert.That (operations.Services, Is.EqualTo (2));
		Assert.That (operations.Installs, Is.EqualTo (1));
		}
	[Test]
	public async Task FalseSuccessFromInstallerCannotAdvanceToServiceChanges ()
		{
		var operations = new FakeOperations { InstalledAfterInstall = false };
		Assert.That ((await Apply (operations)).State, Is.EqualTo ("InspectionRequired"));
		Assert.That (operations.Services + operations.Firewalls, Is.Zero);
		}
	[Test]
	public async Task PlanPreparationUsesNewFileAndNeverInvokesWindowsServicing ()
		{
		Directory.CreateDirectory (_directory);
		using var output = new StringWriter ();
		int result = await WindowsSshSetupCommand.RunAsync (["prepare-ssh", "--remote-address", "LocalSubnet", "--output", Path.Combine (_directory, "plan.json")], output, TextWriter.Null, default);
		Assert.That (result, Is.Zero);
		Assert.That (output.ToString (), Does.Contain ("AutomaticReboot").And.Contain ("PlanSha256"));
		Assert.That (File.Exists (Path.Combine (_directory, "state.json")), Is.False);
		Assert.That (await WindowsSshSetupCommand.RunAsync (["apply-ssh", "--apply-reviewed", "false"], TextWriter.Null, TextWriter.Null, default), Is.EqualTo (2));
		}
	private sealed class FakeOperations : IWindowsSshSetupOperations
		{
		internal int Inspections, Installs, Services, Firewalls;
		internal bool Installed, Service, Firewall, RestartRequired, FailInstall;
		internal bool InstalledAfterInstall = true;
		internal DateTimeOffset Boot = DateTimeOffset.UtcNow.AddHours (-1);
		public Task<WindowsSshSetupSnapshot> InspectAsync (WindowsSshSetupPlan plan, CancellationToken token)
			{
			Inspections++;
			return Task.FromResult (new WindowsSshSetupSnapshot (Installed, Service, Firewall, Boot));
			}
		public Task<bool> InstallAsync (WindowsSshSetupPlan plan, CancellationToken token)
			{
			Installs++;
			if (FailInstall)
				throw new IOException ("Interrupted");
			Installed = InstalledAfterInstall;
			return Task.FromResult (RestartRequired);
			}
		public Task ConfigureServiceAsync (WindowsSshSetupPlan plan, CancellationToken token)
			{
			Services++;
			Service = true;
			return Task.CompletedTask;
			}
		public Task ConfigureFirewallAsync (WindowsSshSetupPlan plan, CancellationToken token)
			{
			Firewalls++;
			Firewall = true;
			return Task.CompletedTask;
			}
		}
	}