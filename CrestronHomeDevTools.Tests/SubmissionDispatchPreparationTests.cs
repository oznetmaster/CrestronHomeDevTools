// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class SubmissionDispatchPreparationTests
	{
	private string _root = null!, _console = null!, _output = null!, _receipt = null!;
	private SubmissionDispatchPreparationSettings _settings = null!;
	private static readonly JsonSerializerOptions Json = new () { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

	[SetUp]
	public void SetUp ()
		{
		_root = Path.Combine (TestContext.CurrentContext.WorkDirectory, "dispatch-setup-" + Guid.NewGuid ().ToString ("N"));
		_console = Path.Combine (_root, "console");
		_output = Path.Combine (_root, "dispatch.json");
		foreach (string relative in new[] { "CrestronHomeDevTools.Console.exe", "CrestronHomeDevTools.Console.dll", "CrestronHomeDevTools.dll",
			"submission-tools/manifest.json", "submission-tools/runtime/python.exe", "submission-tools/scripts/bundled_entry.py" })
			{
			string path = Path.Combine (_console, relative);
			Directory.CreateDirectory (Path.GetDirectoryName (path)!);
			File.WriteAllText (path, "Synthetic inventory only; not executable.");
			}
		foreach (string relative in new[] { "prepared", "journal", "attempts", "uploads", "mail", "signed", "review" })
			Directory.CreateDirectory (Path.Combine (_root, relative));
		string preparation = Path.Combine (_root, "preparation.json"), prepared = Path.Combine (_root, "prepared");
		File.WriteAllText (preparation, JsonSerializer.Serialize (new
			{
			schemaVersion = 1,
			output = prepared,
			signedReviewDirectory = Path.Combine (_root, "signed"),
			reviewDirectory = Path.Combine (_root, "review"),
			authorization = Path.Combine (_root, "approval.json")
			}));
		var plan = new SubmissionDeliveryPlan (new ('a', 64), new ('b', 64), new ('c', 64), new ('d', 64), new ('e', 64),
			"example.pkg", "signed.pdf", "sender@example.test", "drivers@crestron.com");
		string planPath = Path.Combine (prepared, "delivery-plan.json");
		File.WriteAllText (planPath, JsonSerializer.Serialize (plan, Json));
		_receipt = Path.Combine (prepared, "delivery-review-receipt.json");
		File.WriteAllText (_receipt, JsonSerializer.Serialize (new
			{
			schemaVersion = 1,
			state = "DeliveryPlanPrepared",
			deliveryAuthorized = true,
			deliveryAttempted = false,
			submissionReady = false,
			expiresUtc = DateTimeOffset.UtcNow.AddMinutes (5),
			planFileSha256 = Hash (planPath),
			signedReviewSha256 = plan.ReviewSha256,
			authorizationSha256 = plan.AuthorizationSha256
			}));
		_settings = new (1, prepared, preparation, Hash (_receipt), Path.Combine (_root, "journal"), Path.Combine (_root, "attempts"),
			Path.Combine (_root, "uploads"), new ('f', 64), new ('a', 64), Path.Combine (_root, "mail"), "smtp.example.test", 587, 60, 60, 60);
		RepinReceipt ();
		}
	[TearDown]
	public void TearDown () => Directory.Delete (_root, recursive: true);
	private static string Hash (string path) => Convert.ToHexStringLower (SHA256.HashData (File.ReadAllBytes (path)));
	private void RepinReceipt ()
		{
		_settings = _settings with
			{
			DeliveryReviewSha256 = Hash (_receipt)
			};
		File.WriteAllText (Path.Combine (_settings.PreparedDirectory, "COMPLETE"), _settings.DeliveryReviewSha256);
		}
	private Task<SubmissionDispatchSettings> Prepare () => SubmissionDispatchPreparationCommand.PrepareAsync (_settings, _console, _output, CancellationToken.None);

	[Test]
	public async Task GeneratedSettingsUseReviewedPlanAndCompleteConsoleInventory ()
		{
		var actual = await Prepare ();
		Assert.That (actual.SchemaVersion, Is.EqualTo (2));
		Assert.That (actual.Revalidation, Is.Null);
		Assert.That (actual.BundledRevalidation!.ConsoleFiles, Has.Count.EqualTo (6));
		Assert.That (actual.BundledRevalidation.PreparationSettingsSha256, Is.EqualTo (Hash (_settings.PreparationSettingsPath)));
		Assert.That (actual.PackagePath, Is.EqualTo (Path.Combine (_settings.PreparedDirectory, "delivery", "example.pkg")));
		Assert.That (File.Exists (_output), Is.False, "Preparation returns settings; writing requires a new file in the command.");
		}
	[TestCase ("deliveryAuthorized", "false")]
	[TestCase ("deliveryAttempted", "true")]
	[TestCase ("submissionReady", "true")]
	[TestCase ("expiresUtc", "\"2000-01-01T00:00:00Z\"")]
	[TestCase ("state", "\"SignedReviewPrepared\"")]
	[TestCase ("authorizationSha256", "\"wrong\"")]
	public async System.Threading.Tasks.Task InvalidReviewedPreparationDoesNotProduceSettings (string field, string json)
		{
		var receipt = JsonNode.Parse (File.ReadAllText (_receipt))!;
		receipt[field] = JsonNode.Parse (json);
		File.WriteAllText (_receipt, receipt.ToJsonString ());
		RepinReceipt ();
		await Assert.ThrowsAsync<InvalidDataException> (() => Prepare ());
		}
	[TestCase ("dotnet")]
	[TestCase ("validator")]
	[TestCase ("Dotnet")]
	public async System.Threading.Tasks.Task PreparationCannotOverrideBundledValidator (string field)
		{
		var preparation = JsonNode.Parse (File.ReadAllText (_settings.PreparationSettingsPath))!;
		preparation[field] = "untrusted";
		File.WriteAllText (_settings.PreparationSettingsPath, preparation.ToJsonString ());
		await Assert.ThrowsAsync<InvalidDataException> (() => Prepare ());
		}
	[TestCase ("review")]
	[TestCase ("signed")]
	[TestCase ("console")]
	[TestCase ("prepared")]
	public async System.Threading.Tasks.Task MutableReceiptStorageCannotOverlapInputs (string directory)
		{
		_settings = _settings with
			{
			MailReceiptDirectory = Path.Combine (_root, directory)
			};
		await Assert.ThrowsAsync<InvalidDataException> (() => Prepare ());
		}
	[Test]
	public void LegacyAndBundledConfigurationCannotBeMixed ()
		{
		var settings = Prepare ().GetAwaiter ().GetResult ();
		Assert.Throws<InvalidDataException> (() => SubmissionDispatchCommand.Validate (settings with { SchemaVersion = 1 }));
		Assert.Throws<InvalidDataException> (() => SubmissionDispatchCommand.Validate (settings with { BundledRevalidation = null }));
		}
	[Test]
	public async System.Threading.Tasks.Task IncompleteConsoleAndDifferentPreparationOutputAreRefused ()
		{
		File.Delete (Path.Combine (_console, "CrestronHomeDevTools.Console.exe"));
		await Assert.ThrowsAsync<InvalidDataException> (() => Prepare ());
		File.WriteAllText (Path.Combine (_console, "CrestronHomeDevTools.Console.exe"), "synthetic");
		var preparation = JsonNode.Parse (File.ReadAllText (_settings.PreparationSettingsPath))!;
		preparation["output"] = _root;
		File.WriteAllText (_settings.PreparationSettingsPath, preparation.ToJsonString ());
		await Assert.ThrowsAsync<InvalidDataException> (() => Prepare ());
		}

	private SubmissionRehearsalPreparationSettings MockSettings () => new (1, SubmissionDeliveryEnvironment.Rehearsal,
		_settings.PreparedDirectory, _settings.PreparationSettingsPath, _settings.DeliveryReviewSha256,
		_settings.JournalDirectory, _settings.AttemptsDirectory,
		Directory.CreateDirectory (Path.Combine (_root, "mock")).FullName, _settings.RevalidationTimeoutSeconds);

	private void SelectRehearsal (bool sendEmail = false)
		{
		string planPath = Path.Combine (_settings.PreparedDirectory, "delivery-plan.json");
		var plan = JsonNode.Parse (File.ReadAllText (planPath))!;
		plan["environment"] = "Rehearsal";
		plan["sendRehearsalEmail"] = sendEmail;
		plan["recipient"] = "test@example.test";
		File.WriteAllText (planPath, plan.ToJsonString ());
		var receipt = JsonNode.Parse (File.ReadAllText (_receipt))!;
		receipt["environment"] = "Rehearsal";
		receipt["planFileSha256"] = Hash (planPath);
		File.WriteAllText (_receipt, receipt.ToJsonString ());
		var preparation = JsonNode.Parse (File.ReadAllText (_settings.PreparationSettingsPath))!;
		preparation["environment"] = "Rehearsal";
		preparation["sendRehearsalEmail"] = sendEmail;
		preparation["rehearsalRecipient"] = "test@example.test";
		File.WriteAllText (_settings.PreparationSettingsPath, preparation.ToJsonString ());
		RepinReceipt ();
		}

	[TestCase (false)]
	[TestCase (true)]
	public async Task RehearsalPreparationUsesSamePlanAndRevalidationInventoryWithoutProviderCredentials (bool sendEmail)
		{
		var production = await Prepare ();
		SelectRehearsal (sendEmail);
		var input = MockSettings () with { Mail = sendEmail ? new (_settings.MailReceiptDirectory, "smtp.example.test", 587, 30) : null };
		var mock = await SubmissionDispatchPreparationCommand.PrepareRehearsalAsync (input, _console, _output, default);
		Assert.That (mock.Environment, Is.EqualTo (SubmissionDeliveryEnvironment.Rehearsal));
		Assert.That (mock.Plan, Is.EqualTo (production.Plan with { Environment = SubmissionDeliveryEnvironment.Rehearsal, Recipient = "test@example.test", SendRehearsalEmail = sendEmail }));
		Assert.That (mock.PackagePath, Is.EqualTo (production.PackagePath));
		Assert.That (mock.SignedFormPath, Is.EqualTo (production.SignedFormPath));
		Assert.That (mock.BundledRevalidation!.ConsoleFiles, Is.EqualTo (production.BundledRevalidation!.ConsoleFiles));
		Assert.That (mock.BundledRevalidation.PreparationSettingsSha256, Is.EqualTo (Hash (_settings.PreparationSettingsPath)));
		Assert.That (mock.Revalidation, Is.Null);
		Assert.That (File.Exists (_output), Is.False);
		}

	[TestCase ("expired")]
	[TestCase ("changed-plan")]
	[TestCase ("incomplete")]
	[TestCase ("missing-runtime")]
	[TestCase ("review")]
	[TestCase ("journal")]
	[TestCase ("production")]
	public async Task RehearsalPreparationPreservesProductionArtifactAndStorageRequirements (string scenario)
		{
		SelectRehearsal ();
		if (scenario == "expired")
			{
			var receipt = JsonNode.Parse (File.ReadAllText (_receipt))!;
			receipt["expiresUtc"] = "2000-01-01T00:00:00Z";
			File.WriteAllText (_receipt, receipt.ToJsonString ());
			RepinReceipt ();
			}
		if (scenario == "changed-plan") File.AppendAllText (Path.Combine (_settings.PreparedDirectory, "delivery-plan.json"), " ");
		if (scenario == "incomplete") File.WriteAllText (Path.Combine (_settings.PreparedDirectory, "COMPLETE"), "wrong");
		if (scenario == "missing-runtime") File.Delete (Path.Combine (_console, "CrestronHomeDevTools.Console.exe"));
		var settings = MockSettings ();
		if (scenario == "review") settings = settings with { DestinationDirectory = Path.Combine (_root, "review") };
		if (scenario == "journal") settings = settings with { DestinationDirectory = settings.JournalDirectory };
		if (scenario == "production") settings = settings with { Environment = SubmissionDeliveryEnvironment.Production };
		await Assert.ThrowsAsync<InvalidDataException> (() => SubmissionDispatchPreparationCommand.PrepareRehearsalAsync (settings, _console, _output, default));
		Assert.That (File.Exists (_output), Is.False);
		}
	}
