// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class ProcessorFailureReceiptTests
	{
	[Test]
	public async Task RetainsOriginalResponseWithoutOverwritingLeaseOrPreviousFailure ()
		{
		var directory = Directory.CreateTempSubdirectory ("processor-failure-");
		try
			{
			var leasePath = Path.Combine (directory.FullName, "owner.json");
			const string originalLease = "{\"State\":\"Held\"}";
			await File.WriteAllTextAsync (leasePath, originalLease);
			var exception = new ProcessorApiException ("Installation was not confirmed.")
				{
				DiagnosticCommand = "cp.platformDriverController:commissionDevice",
				DiagnosticResponse = JsonSerializer.SerializeToElement (new { CommissioningResult = "Failed", Id = 0 })
				};
			var path = await ProcessorFailureReceipt.WriteAsync (leasePath, exception);
			var firstBytes = await File.ReadAllBytesAsync (path);
			using var receipt = JsonDocument.Parse (firstBytes);
			Assert.That (receipt.RootElement.GetProperty ("Command").GetString (), Is.EqualTo (exception.DiagnosticCommand));
			Assert.That (receipt.RootElement.GetProperty ("LeaseReceiptFile").GetString (), Is.EqualTo ("owner.json"));
			Assert.That (JsonElement.DeepEquals (receipt.RootElement.GetProperty ("Response"), exception.DiagnosticResponse.Value), Is.True);
			Assert.ThrowsAsync<IOException> (async () => await ProcessorFailureReceipt.WriteAsync (leasePath, exception));
			Assert.That (await File.ReadAllBytesAsync (path), Is.EqualTo (firstBytes));
			Assert.That (await File.ReadAllTextAsync (leasePath), Is.EqualTo (originalLease));
			}
		finally { directory.Delete (true); }
		}
	}