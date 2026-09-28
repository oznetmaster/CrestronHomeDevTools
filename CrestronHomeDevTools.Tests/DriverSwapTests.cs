// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class DriverSwapTests
	{
	private static JsonElement Completed (string operation = "op", string driver = "driver") => JsonSerializer.SerializeToElement (new
		{
		EventType = "cp.platformDriverController:swapDriverCompleted",
		OperationId = operation,
		DriverId = driver,
		IsRebootRequired = true,
		DeviceIdsRequiringReconfiguration = Array.Empty<int> ()
		});

	[TestCase (true)]
	[TestCase (false)]
	public async Task CompletionIsRetainedBeforeOrAfterWaitStarts (bool early)
		{
		var tracker = new DriverSwapTracker ();
		if (early)
			tracker.Accept (Completed ());
		var wait = tracker.WaitAsync ("op", "driver", TimeSpan.FromSeconds (1), default);
		if (!early)
			tracker.Accept (Completed ());
		var result = await wait;
		Assert.That (result.IsRebootRequired, Is.True);
		Assert.That (result.DeviceIdsRequiringReconfiguration, Is.Empty);
		}

	[Test]
	public async System.Threading.Tasks.Task WrongDriverCannotAuthorizeReboot ()
		{
		var tracker = new DriverSwapTracker ();
		tracker.Accept (Completed (driver: "other"));
		await Assert.ThrowsAsync<ProcessorApiException> (async () => await tracker.WaitAsync ("op", "driver", TimeSpan.FromSeconds (1), default));
		}

	[Test]
	public async System.Threading.Tasks.Task OtherOperationCannotCompleteWait ()
		{
		var tracker = new DriverSwapTracker ();
		tracker.Accept (Completed (operation: "other"));
		await Assert.ThrowsAsync<TimeoutException> (async () => await tracker.WaitAsync ("op", "driver", TimeSpan.FromMilliseconds (30), default));
		}

	[TestCase ("Succeeded")]
	[TestCase ("Ended")]
	public async System.Threading.Tasks.Task GenericTerminalStatusDoesNotConfirmSwap (string status)
		{
		var tracker = new DriverSwapTracker ();
		tracker.Accept (JsonSerializer.SerializeToElement (new
			{
			EventType = "cp.types:operationStatusChanged",
			OperationId = "op",
			LatestStatus = status
			}));
		await Assert.ThrowsAsync<TimeoutException> (async () => await tracker.WaitAsync ("op", "driver", TimeSpan.FromMilliseconds (30), default));
		}

	[TestCase ("cp.platformDriverController:swapDriverFailed")]
	[TestCase ("cp.types:operationStatusChanged")]
	public async System.Threading.Tasks.Task FailureCannotBeOverwrittenByLaterCompletion (string eventType)
		{
		var tracker = new DriverSwapTracker ();
		tracker.Accept (JsonSerializer.SerializeToElement (new
			{
			EventType = eventType,
			OperationId = "op",
			LatestStatus = "Failed"
			}));
		tracker.Accept (Completed ());
		await Assert.ThrowsAsync<ProcessorApiException> (async () => await tracker.WaitAsync ("op", "driver", TimeSpan.FromSeconds (1), default));
		}

	[TestCase ("IsRebootRequired")]
	[TestCase ("DeviceIdsRequiringReconfiguration")]
	public async System.Threading.Tasks.Task MissingCompletionFieldsCannotAuthorizeReboot (string field)
		{
		var message = JsonSerializer.Deserialize<Dictionary<string, JsonElement>> (Completed ().GetRawText ())!;
		message.Remove (field);
		var tracker = new DriverSwapTracker ();
		tracker.Accept (JsonSerializer.SerializeToElement (message));
		await Assert.ThrowsAsync<ProcessorApiException> (async () => await tracker.WaitAsync ("op", "driver", TimeSpan.FromSeconds (1), default));
		}

	[Test]
	public async System.Threading.Tasks.Task LostConnectionEndsPendingWaitWithoutSuccess ()
		{
		var tracker = new DriverSwapTracker ();
		var wait = tracker.WaitAsync ("op", "driver", TimeSpan.FromSeconds (1), default);
		tracker.Fail (new IOException ());
		await Assert.ThrowsAsync<IOException> (async () => await wait);
		}
	}