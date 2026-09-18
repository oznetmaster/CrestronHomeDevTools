// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Security.Cryptography;
using System.Text.Json;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class SubmissionDeliveryRevalidationTests
	{
	private string _root = null!, _tools = null!, _attempts = null!, _validator = null!, _preparation = null!, _executable = null!;
	private SubmissionDeliveryPlan _plan = null!;
	[SetUp]
	public void SetUp ()
		{
		_root = Path.Combine (TestContext.CurrentContext.WorkDirectory, "revalidation-bridge-" + Guid.NewGuid ().ToString ("N"));
		_tools = Path.Combine (_root, "tools"); _attempts = Path.Combine (_root, "attempts"); _validator = Path.Combine (_root, "validator");
		foreach (string path in new[] { _root, _tools, _attempts, _validator, Path.Combine (_root, "prepared") }) Directory.CreateDirectory (path);
		_executable = Path.Combine (TestContext.CurrentContext.TestDirectory, "test-probe", "CrestronHomeDevTools.Tests.Probe.exe");
		_plan = new (new ('a', 64), new ('b', 64), new ('c', 64), new ('d', 64), new ('e', 64), "example.pkg", "signed.pdf", "sender@example.test", "recipient@example.test");
		File.WriteAllText (Path.Combine (_tools, "plan.json"), JsonSerializer.Serialize (_plan));
		File.WriteAllText (Path.Combine (_tools, "revalidate_delivery.py"), "valid");
		File.WriteAllText (Path.Combine (_validator, "validator.dll"), "synthetic validator");
		_preparation = Path.Combine (_root, "preparation.json");
		File.WriteAllText (_preparation, JsonSerializer.Serialize (new { dotnet = _executable, validator = Path.Combine (_validator, "validator.dll") }));
		}
	[TearDown]
	public void TearDown () => Directory.Delete (_root, recursive: true);
	private static string Hash (string file) => Convert.ToHexString (SHA256.HashData (File.ReadAllBytes (file))).ToLowerInvariant ();
	private static SubmissionEvidenceFile[] Pins (string root) => Directory.GetFiles (root).Select (path => new SubmissionEvidenceFile (Path.GetFileName (path), Hash (path))).ToArray ();
	private SubmissionDeliveryRevalidationSettings Settings () => new (_executable, Hash (_executable), _executable, Hash (_executable),
		_tools, Pins (_tools), _validator, Pins (_validator), _preparation, Hash (_preparation), Path.Combine (_root, "prepared"), _attempts, new ('f', 64), TimeSpan.FromSeconds (5));
	[Test]
	public async Task EachStepGetsASeparateCompletedCheckedAttempt ()
		{
		var settings = Settings ();
		foreach (var step in new[] { SubmissionDeliveryStep.Upload, SubmissionDeliveryStep.Send })
			{
			var approval = await SubmissionDeliveryRevalidation.CheckAsync (settings, _plan, step);
			Assert.That (approval.PlanSha256, Is.EqualTo (SubmissionDelivery.PlanDigest (_plan)));
			Assert.That (approval.ExpiresUtc, Is.GreaterThan (DateTimeOffset.UtcNow));
			}
		Assert.That (Directory.GetDirectories (_attempts), Has.Length.EqualTo (2));
		foreach (var path in Directory.GetDirectories (_attempts))
			Assert.That (JsonDocument.Parse (File.ReadAllBytes (Path.Combine (path, "finished.json"))).RootElement.GetProperty ("Success").GetBoolean (), Is.True);
		}
	[TestCase (true)]
	[TestCase (false)]
	public async Task EquivalentAbsoluteToolDirectoryPathsAreAccepted (bool forwardSlashes)
		{
		var settings = Settings ();
		string path = forwardSlashes ? settings.ToolsDirectory.Replace (Path.DirectorySeparatorChar, '/') : settings.ToolsDirectory + Path.DirectorySeparatorChar;
		var authorization = await SubmissionDeliveryRevalidation.CheckAsync (settings with { ToolsDirectory = path }, _plan, SubmissionDeliveryStep.Upload);
		Assert.That (authorization.PlanSha256, Is.EqualTo (SubmissionDelivery.PlanDigest (_plan)));
		}
	[TestCase ("exit")]
	[TestCase ("malformed")]
	[TestCase ("wrong-plan")]
	[TestCase ("expired")]
	[TestCase ("no-complete")]
	[TestCase ("wrong-marker")]
	[TestCase ("missing-field")]
	[TestCase ("different-stdout")]
	[TestCase ("changed-report")]
	[TestCase ("wrong-plan-file")]
	[TestCase ("stale-result")]
	[TestCase ("future-result")]
	public void InvalidProcessResultsDoNotAuthorizeDelivery (string mode)
		{
		File.WriteAllText (Path.Combine (_tools, "revalidate_delivery.py"), mode);
		var error = Assert.ThrowsAsync<InvalidDataException> (() => SubmissionDeliveryRevalidation.CheckAsync (Settings (), _plan, SubmissionDeliveryStep.Upload));
		Assert.That (error!.Message, Does.Not.Contain ("PRIVATE-SYNTHETIC"));
		Assert.That (File.Exists (Path.Combine (Directory.GetDirectories (_attempts).Single (), "finished.json")), Is.True);
		}
	[TestCase ("hang")]
	[TestCase ("stdout-limit")]
	[TestCase ("stderr-limit")]
	public void FailedOrOversizedProcessesExitBeforeTheAttemptIsFinished (string mode)
		{
		File.WriteAllText (Path.Combine (_tools, "revalidate_delivery.py"), mode);
		Assert.ThrowsAsync<InvalidDataException> (() => SubmissionDeliveryRevalidation.CheckAsync (Settings () with { Timeout = TimeSpan.FromSeconds (1) }, _plan, SubmissionDeliveryStep.Upload));
		string attempt = Directory.GetDirectories (_attempts).Single ();
		using var identity = JsonDocument.Parse (File.ReadAllBytes (Path.Combine (attempt, "process.json")));
		int id = identity.RootElement.GetProperty ("Id").GetInt32 ();
		bool alive;
		try { using var process = System.Diagnostics.Process.GetProcessById (id); alive = !process.HasExited && process.StartTime.ToUniversalTime () == identity.RootElement.GetProperty ("StartedUtc").GetDateTime (); }
		catch (ArgumentException) { alive = false; }
		Assert.That (alive, Is.False);
		Assert.That (File.Exists (Path.Combine (attempt, "finished.json")), Is.True);
		}
	[TestCase ("tools")]
	[TestCase ("validator")]
	[TestCase ("settings")]
	[TestCase ("extra-file")]
	public void ChangedInputsFailBeforeStartingAProcess (string change)
		{
		var settings = Settings ();
		string path = change switch { "tools" => Path.Combine (_tools, "revalidate_delivery.py"), "validator" => Path.Combine (_validator, "validator.dll"), "settings" => _preparation, _ => Path.Combine (_tools, "extra.py") };
		File.AppendAllText (path, "changed");
		Assert.ThrowsAsync<InvalidDataException> (() => SubmissionDeliveryRevalidation.CheckAsync (settings, _plan, SubmissionDeliveryStep.Upload));
		Assert.That (Directory.GetDirectories (_attempts), Is.Empty);
		}
	[Test]
	public void PartiallyWrittenTerminalRecordCannotPermitANewAttempt ()
		{
		string previous = Path.Combine (_attempts, "unfinished"); Directory.CreateDirectory (previous);
		File.WriteAllText (Path.Combine (previous, "finished.json"), "{");
		Assert.CatchAsync<Exception> (() => SubmissionDeliveryRevalidation.CheckAsync (Settings (), _plan, SubmissionDeliveryStep.Upload));
		Assert.That (Directory.GetDirectories (_attempts), Has.Length.EqualTo (1));
		}
	[Test]
	public void CancellationBeforePinningDoesNotStartAProcess ()
		{
		using var cancelled = new CancellationTokenSource (); cancelled.Cancel ();
		Assert.CatchAsync<OperationCanceledException> (() => SubmissionDeliveryRevalidation.CheckAsync (Settings (), _plan, SubmissionDeliveryStep.Upload, cancelled.Token));
		Assert.That (Directory.GetDirectories (_attempts), Is.Empty);
		}
	[Test]
	public void UnfinishedPriorProcessCannotBeBypassedByNewAttempt ()
		{
		Directory.CreateDirectory (Path.Combine (_attempts, "unfinished"));
		Assert.ThrowsAsync<InvalidOperationException> (() => SubmissionDeliveryRevalidation.CheckAsync (Settings (), _plan, SubmissionDeliveryStep.Upload));
		Assert.That (Directory.GetDirectories (_attempts), Has.Length.EqualTo (1));
		}
	}