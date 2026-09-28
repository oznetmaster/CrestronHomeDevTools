// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Net;
using System.Text.Json;

using CrestronHomeDevTools.Automation;

using CrestronHomeNUnit.Client;
using CrestronHomeNUnit.Workflow;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

public sealed partial class AutomationInstalledAppTests
	{
	private void ConfigureAdditionalInitial ()
		{
		settings = settings with
			{
			PreEnduranceTests = settings.InstalledAppTests,
			Endurance = new (null!, null!, null!),
			Review = new (null!, null!, null!, null!, null!, "Synthetic", "Synthetic", [])
			};
		}
 private void ConfigureSeparateInitial() {
  ConfigureAdditionalInitial();
  settings=settings with {PreEnduranceSeparateProcessor=true,
   PreEnduranceTests=settings.PreEnduranceTests! with {Host="outage.example",CertificateSha256=new('9',64),SshFingerprint="outage-pin",
    Target=settings.PreEnduranceTests.Target with {DeviceId=244}},
   PreEnduranceFixtureSettings=JsonSerializer.SerializeToElement(new {DeviceId=244,Target="outage.example"})};
 }
 [Test]
 public async Task ExplicitSeparateProcessorPreservesMainTargetAndRetainsBothPlans() {
  ConfigureSeparateInitial();var hosts=new List<string>();
  var result=await AdditionalController((p,c,f,t)=> {hosts.Add(p.Host);return Run(p,c,f,t);}).ExecuteAsync(context,default);
  Assert.That(result.Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
  Assert.That(hosts,Is.EqualTo(new[]{"processor.example","outage.example"}));
  Assert.That(settings.NUnit.Host,Is.EqualTo("processor.example"));
  var plan=AutomationFiles.Read<InstalledDriverTestPlan>(Path.Combine(context.RunDirectory,"pre-endurance","target-plan.json"));
  Assert.That(plan.Host,Is.EqualTo("outage.example"));
  Assert.That(plan.Target.DeviceId,Is.EqualTo(244));
  Assert.That(plan.PackageSha256,Is.EqualTo(settings.Release.PackageSha256));
  AutomationInitialAdditionalTests.VerifyRetained(context);
 }
 [TestCase("same-host")][TestCase("deployment")][TestCase("managed")][TestCase("no-inputs")][TestCase("candidate")]
 public async Task SeparateProcessorRejectsAmbiguousOrDifferentCandidateBeforeAnyExecution(string change) {
  ConfigureSeparateInitial();settings=change switch {
   "same-host"=>settings with {PreEnduranceTests=settings.PreEnduranceTests! with {Host=settings.NUnit.Host}},
   "deployment"=>settings with {PreEnduranceFromDeployment=true},
   "managed"=>settings with {PreEnduranceFixtureSettings=JsonSerializer.SerializeToElement(new {DeviceId="${managed:demo:deviceId}"})},
   "candidate"=>settings with {PreEnduranceTests=settings.PreEnduranceTests! with {PackageSha256=new('8',64)}},
   _=>settings with {PreEnduranceFixtureSettings=null}};
  await Assert.ThrowsAsync<InvalidDataException>(async()=>await AdditionalController().ExecuteAsync(context,default));
  Assert.That(calls,Is.Zero);
 }
 [Test]
 public void SeparateProcessorCredentialsRequireItsOwnPins() {
  ConfigureSeparateInitial();
  Assert.DoesNotThrow(()=>SubmissionAutomationStages.VerifyProcessorPins(settings,"outage.example",new('9',64),"outage-pin"));
  Assert.Throws<InvalidDataException>(()=>SubmissionAutomationStages.VerifyProcessorPins(settings,"outage.example",settings.NUnit.CertificateSha256,settings.NUnit.SshFingerprint));
  Assert.Throws<InvalidDataException>(()=>SubmissionAutomationStages.VerifyProcessorPins(settings,"undeclared.example",new('9',64),"outage-pin"));
 }
	private SubmissionAutomationStages AdditionalController (
	 Func<InstalledDriverTestPlan, NetworkCredential, string, CancellationToken, Task<InstalledDriverTestResult>>? runner = null) =>
	 new (settings, new ('f', 64), (_, _, _, _) => throw new AssertionException ("NUnit must not restart"), _ => new (), installedApp: runner ?? Run);

	[Test]
	public async Task AdditionalInitialTestsRunBeforeAppStageCompletesAndAreNotReplayed ()
		{
		ConfigureAdditionalInitial ();
		var controller = AdditionalController ();
		var result = await controller.ExecuteAsync (context, default);
		Assert.That (result.Status, Is.EqualTo (SubmissionWorkflowStatus.Completed));
		Assert.That (result.Receipt!.RelativePath, Is.EqualTo ("initial-tests.json"));
		Assert.That (calls, Is.EqualTo (2));
		AutomationInitialAdditionalTests.VerifyRetained (context);
		Assert.That (AutomationInitialAdditionalTests.RetainedFiles (context).Select (f => f.RelativePath), Does.Contain ("pre-endurance/installed-app/raw.xml"));
		Assert.That ((await controller.RecoverAsync (context, default)).Status, Is.EqualTo (SubmissionWorkflowStatus.Completed));
		Assert.That (calls, Is.EqualTo (2));
		}
	[Test]
	public async Task InterruptedAdditionalFixtureDoesNotReplayEitherFixture ()
		{
		ConfigureAdditionalInitial ();
		Task<InstalledDriverTestResult> Interrupt (InstalledDriverTestPlan p, NetworkCredential c, string f, CancellationToken t)
			{
			if (f.Contains ("pre-endurance"))
				{
				calls++;
				throw new IOException ("synthetic interrupted outage recorder");
				}
			return Run (p, c, f, t);
			}

		await Assert.ThrowsAsync<IOException> (async () => await AdditionalController (Interrupt).ExecuteAsync (context, default));
		var recovered = await AdditionalController ().RecoverAsync (context, default);
		Assert.That (recovered.Status, Is.EqualTo (SubmissionWorkflowStatus.OutcomeUnknown));
		Assert.That (calls, Is.EqualTo (2));
		Assert.That (Directory.Exists (Path.Combine (context.RunDirectory, "endurance")), Is.False);
		}
	[Test]
	public async Task FailedRestorationInAdditionalFixturePreventsStageCompletion ()
		{
		ConfigureAdditionalInitial ();
		Task<InstalledDriverTestResult> Incomplete (InstalledDriverTestPlan p, NetworkCredential c, string f, CancellationToken t)
			{
			if (!f.Contains ("pre-endurance"))
				return Run (p, c, f, t);
			calls++;
			Directory.CreateDirectory (f);
			var result = new InstalledDriverTestResult (new WorkflowTestOutcome (3, 0, 0, true), false, true, true, true, "synthetic restoration failure");
			File.WriteAllText (Path.Combine (f, "InstalledDriverTests.json"), JsonSerializer.Serialize (result));
			return Task.FromResult (result);
			}
		var failed = await AdditionalController (Incomplete).ExecuteAsync (context, default);
		Assert.That (failed.Status, Is.EqualTo (SubmissionWorkflowStatus.Failed));
		Assert.That (File.Exists (Path.Combine (context.RunDirectory, "pre-endurance", "installed-app-tests.json")), Is.False);
		Assert.That ((await AdditionalController ().RecoverAsync (context, default)).Status, Is.EqualTo (SubmissionWorkflowStatus.Failed));
		Assert.That (calls, Is.EqualTo (2));
		}
	[Test]
	public async Task AdditionalFixtureGetsItsOwnSettingsAndRejectsLaterMutation ()
		{
		ConfigureAdditionalInitial ();
		settings = settings with
			{
			PreEnduranceFixtureSettings = JsonSerializer.SerializeToElement (new
				{
				Purpose = "outage",
				DeviceId = 2
				})
			};
		await AdditionalController ().ExecuteAsync (context, default);
		using var fixture = JsonDocument.Parse (File.ReadAllBytes (Path.Combine (context.RunDirectory, "pre-endurance", "app-fixture-settings.json")));
		Assert.That (fixture.RootElement.GetProperty ("Purpose").GetString (), Is.EqualTo ("outage"));
		settings = settings with
			{
			PreEnduranceFixtureSettings = JsonSerializer.SerializeToElement (new
				{
				Purpose = "changed",
				DeviceId = 2
				})
			};
		await Assert.ThrowsAsync<InvalidDataException> (async () => await AdditionalController ().RecoverAsync (context, default));
		Assert.That (calls, Is.EqualTo (2));
		}
	[TestCase ("host")]
	[TestCase ("package")]
	[TestCase ("commit")]
	[TestCase ("base")]
	[TestCase ("instance")]
	public async System.Threading.Tasks.Task AdditionalFixtureMustBindSameCandidateBeforeAnyExecution (string changed)
		{
		ConfigureAdditionalInitial ();
		var plan = settings.PreEnduranceTests!;
		settings = changed switch
			{
				"host" => settings with { PreEnduranceTests = plan with { Host = "other.example" } },
				"package" => settings with { PreEnduranceTests = plan with { PackageSha256 = new ('f', 64) } },
				"commit" => settings with { PreEnduranceTests = plan with { PackageSourceCommit = new ('f', 40) } },
				"instance" => settings with { PreEnduranceTests = plan with { Target = plan.Target with { DeviceId = 42 } } },
				_ => settings with { InstalledAppTests = null }
				};
		await Assert.ThrowsAsync<InvalidDataException> (async () => await AdditionalController ().ExecuteAsync (context, default));
		Assert.That (calls, Is.Zero);
		}
	[Test]
	public async Task AdditionalResultsStayBoundToUnmodifiedInitialAttempt ()
		{
		ConfigureAdditionalInitial ();
		await AdditionalController ().ExecuteAsync (context, default);
		File.AppendAllText (Path.Combine (context.RunDirectory, "installed-app", "raw.xml"), "changed");
		Assert.Throws<InvalidDataException> (() => AutomationInitialAdditionalTests.VerifyRetained (context));
		}
	[Test]
	public async Task MissingAdditionalIntentNeverPermitsReplayingAnExistingOperation ()
		{
		ConfigureAdditionalInitial ();
		await AdditionalController ().ExecuteAsync (context, default);
		File.Delete (Path.Combine (context.RunDirectory, "pre-endurance", "installed-app-intent.json"));
		var result = await AdditionalController ().ExecuteAsync (context, default);
		Assert.That (result.Status, Is.EqualTo (SubmissionWorkflowStatus.OutcomeUnknown));
		Assert.That (calls, Is.EqualTo (2));
		}
	[Test]
	public async Task CompletedStagePinsAdditionalReceiptEvenIfItsInventoryIsRewritten ()
		{
		ConfigureAdditionalInitial ();
		var completed = await AdditionalController ().ExecuteAsync (context, default);
		context.Checkpoint.CompletedStages[SubmissionWorkflowStage.AppTests] = completed.Receipt!;
		string raw = Path.Combine (context.RunDirectory, "pre-endurance", "installed-app", "raw.xml");
		string receipt = Path.Combine (context.RunDirectory, "pre-endurance", "installed-app-tests.json");
		string old = AutomationFiles.Hash (raw);
		File.AppendAllText (raw, "changed");
		File.WriteAllText (receipt, File.ReadAllText (receipt).Replace (old, AutomationFiles.Hash (raw), StringComparison.Ordinal));
		Assert.Throws<InvalidDataException> (() => AutomationInitialAdditionalTests.VerifyRetained (context));
		Assert.That (calls, Is.EqualTo (2));
		}
	[Test]
	public void AdditionalConfigurationCannotExistWithoutItsPlan ()
		{
		settings = settings with
			{
			PreEnduranceFixtureSettings = JsonSerializer.SerializeToElement (new
				{
				Scope = "outage"
				})
			};
		Assert.That (SubmissionAutomationConfiguration.Check (settings).MissingBindings, Does.Contain ("PreEnduranceTests for PreEnduranceFixtureSettings"));
		Assert.Throws<InvalidDataException> (() => AutomationInitialAdditionalTests.Validate (settings));
		}
	[Test]
	public async Task AdditionalFixtureResolvesActualDeploymentInsteadOfZeroPlaceholder ()
		{
		ConfigurePostDeployment ();
		var plan = settings.PostEnduranceTests!;
		settings = settings with
			{
			PreEnduranceTests = plan with
				{
				Target = plan.Target with
					{
					DeviceId = 0
					}
				},
			PreEnduranceFromDeployment = true,
			PostEnduranceTests = null,
			PostEnduranceFromDeployment = false,
			Review = new (null!, null!, null!, null!, null!, "Synthetic", "Synthetic", [])
			};
		context = context with
			{
			Checkpoint = context.Checkpoint with
				{
				Stage = SubmissionWorkflowStage.AppTests
				}
			};
		context.Checkpoint.CompletedStages.Remove (SubmissionWorkflowStage.Endurance);
		context.Checkpoint.CompletedStages.Remove (SubmissionWorkflowStage.AppTests);
		await Advance ();
		int received = 0;
		var result = await AutomationInitialAdditionalTests.Advance (context, settings, (p, c, f, t) =>
		{
			received = p.Target.DeviceId;
			Assert.That (p.Target.CatalogueId, Is.EqualTo ("observed.catalogue.1.0.000.0000"));
			return Run (p, c, f, t);
		}, _ => new (), default);
		Assert.That (result.Status, Is.EqualTo (SubmissionWorkflowStatus.Completed));
		Assert.That (received, Is.EqualTo (167));
		}
	}
