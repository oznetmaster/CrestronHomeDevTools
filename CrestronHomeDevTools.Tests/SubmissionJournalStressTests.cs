// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Security.Cryptography;
using System.Text;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

public sealed class SubmissionJournalStressTests
	{
	[Test]
	public async Task RapidDurableTransitionsAndCompletedReadsNeverRepeatProviders ()
		{
		var parent = Path.GetFullPath (TestContext.CurrentContext.WorkDirectory);
		var root = Path.Combine (parent, "journal-stress-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (root);
		try
			{
			var package = Path.Combine (root, "Example.pkg");
			var form = Path.Combine (root, "synthetic.pdf");
			File.WriteAllText (package, "Synthetic package, no external delivery");
			File.WriteAllText (form, "Synthetic form, no signature");
			string Hash (byte[] data) => Convert.ToHexString (SHA256.HashData (data)).ToLowerInvariant ();
			for (int index = 0; index < 100; index++)
				{
				var journal = Path.Combine (root, index.ToString (System.Globalization.CultureInfo.InvariantCulture));
				Directory.CreateDirectory (journal);
				var plan = new SubmissionDeliveryPlan (Hash (Encoding.UTF8.GetBytes ("synthetic-candidate-" + index)), new ('b', 64), new ('c', 64),
					Hash (File.ReadAllBytes (package)), Hash (File.ReadAllBytes (form)), "Example.pkg", "synthetic.pdf", "sender@example.test", "recipient@example.test");
				var transport = new Transport ();
				var completed = await SubmissionDelivery.ExecuteAsync (journal, plan, package, form, transport);
				var repeated = await SubmissionDelivery.ExecuteAsync (journal, plan, package, form, transport);
				Assert.That (completed.State, Is.EqualTo (SubmissionDeliveryState.Submitted), "Synthetic iteration " + index);
				Assert.That (repeated, Is.EqualTo (completed));
				Assert.That ((transport.Uploads, transport.Sends), Is.EqualTo ((1, 1)));
				Assert.That (Directory.GetFiles (journal, "*.tmp"), Is.Empty);
				}
			}
		finally
			{
			if (!Path.GetFullPath (root).StartsWith (parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException ("Unsafe test cleanup path");
			Directory.Delete (root, recursive: true);
			}
		}

	private sealed class Transport : ISubmissionDeliveryTransport
		{
		internal int Uploads, Sends;
		public Task<SubmissionUploadReceipt> UploadAsync (Stream input, string name, CancellationToken token)
			{
			Uploads++;
			return Task.FromResult (new SubmissionUploadReceipt ("https://example.test/synthetic", "no-network-upload"));
			}
		public Task<SubmissionMailReceipt> SendAsync (SubmissionDeliveryPlan plan, SubmissionUploadReceipt upload, Stream input, string messageId, CancellationToken token)
			{
			Sends++;
			return Task.FromResult (new SubmissionMailReceipt ("no-network-email"));
			}
		}
	}