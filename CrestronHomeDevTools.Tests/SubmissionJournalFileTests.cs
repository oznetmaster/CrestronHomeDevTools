// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

public sealed class SubmissionJournalFileTests
	{
	[TestCase (5)]
	[TestCase (32)]
	[TestCase (33)]
	public void TemporaryWindowsRefusalRetriesOnlyIdenticalReplace (int error)
		{
		int attempts = 0;
		var waits = new List<int> ();
		SubmissionJournalFile.Replace (() => { if (++attempts < 3) throw new IOException ("Synthetic file refusal", unchecked((int)0x80070000) | error); }, waits.Add, true);
		Assert.That (attempts, Is.EqualTo (3));
		Assert.That (waits, Is.EqualTo (new[] { 25, 50 }));
		}

	[Test]
	public void PermanentDenialIsBoundedAndOriginalFailureIsPreserved ()
		{
		var failure = new UnauthorizedAccessException ("Synthetic permanent denial");
		int attempts = 0;
		var waits = new List<int> ();
		var actual = Assert.Throws<UnauthorizedAccessException> (() => SubmissionJournalFile.Replace (
			() => { attempts++; throw failure; }, waits.Add, true));
		Assert.That (actual, Is.SameAs (failure));
		Assert.That (attempts, Is.EqualTo (5));
		Assert.That (waits.Sum (), Is.EqualTo (375));
		}

	[TestCase (2)]
	[TestCase (3)]
	[TestCase (112)]
	public void MissingPathsAndStorageErrorsAreNotRetried (int error)
		{
		int attempts = 0;
		var failure = new IOException ("Synthetic permanent storage error", unchecked((int)0x80070000) | error);
		Assert.That (Assert.Throws<IOException> (() => SubmissionJournalFile.Replace (() => { attempts++; throw failure; },
			_ => throw new AssertionException ("Must not wait"), true)), Is.SameAs (failure));
		Assert.That (attempts, Is.EqualTo (1));
		}

	[Test]
	public void NonWindowsErrorsAreNotTreatedAsWindowsSharingFailures () => Assert.Throws<UnauthorizedAccessException> (() =>
		SubmissionJournalFile.Replace (() => throw new UnauthorizedAccessException (), _ => throw new AssertionException ("Must not wait"), false));

	[Test]
	public void SuccessfulReplaceDoesNotDelay ()
		{
		int calls = 0;
		SubmissionJournalFile.Replace (() => calls++, _ => throw new AssertionException ("Must not wait"), true);
		Assert.That (calls, Is.EqualTo (1));
		}

	[Test]
	public void CancellationIsNeverRetried () => Assert.Throws<OperationCanceledException> (() =>
		SubmissionJournalFile.Replace (() => throw new OperationCanceledException (), _ => throw new AssertionException ("Must not wait"), true));

	[Test]
	public void TemporaryReaderKeepsOldJournalIntactUntilAtomicReplacementSucceeds ()
		{
		if (!OperatingSystem.IsWindows ()) Assert.Ignore ("Windows sharing semantics");
		var parent = Path.GetFullPath (TestContext.CurrentContext.WorkDirectory);
		var root = Path.Combine (parent, "journal-atomic-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (root);
		FileStream? reader = null;
		try
			{
			var temporary = Path.Combine (root, "next.tmp");
			var destination = Path.Combine (root, "journal.json");
			File.WriteAllText (temporary, "new-intent");
			File.WriteAllText (destination, "previous-state");
			reader = new FileStream (destination, FileMode.Open, FileAccess.Read, FileShare.Read);
			int refusals = 0;
			SubmissionJournalFile.Replace (() => File.Move (temporary, destination, overwrite: true), delay =>
				{
				refusals++;
				Assert.That (File.ReadAllText (destination), Is.EqualTo ("previous-state"));
				Assert.That (File.ReadAllText (temporary), Is.EqualTo ("new-intent"));
				reader.Dispose ();
				Thread.Sleep (delay);
				}, true);
			Assert.That (refusals, Is.GreaterThanOrEqualTo (1));
			Assert.That (File.ReadAllText (destination), Is.EqualTo ("new-intent"));
			Assert.That (File.Exists (temporary), Is.False);
			}
		finally
			{
			reader?.Dispose ();
			if (!Path.GetFullPath (root).StartsWith (parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException ("Unsafe test cleanup path");
			Directory.Delete (root, recursive: true);
			}
		}
	}