// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Globalization;
using NUnit.Framework;
namespace CrestronHomeDevTools.Tests;

public sealed class SubmissionDisplayTimeTests
{
 [Test] public void UtcPresentationConvertsOffsetWithoutChangingEvidenceInstant() {
  var value=DateTimeOffset.Parse("2026-09-29T08:52:28.1234567+01:00",CultureInfo.InvariantCulture);
  Assert.That(SubmissionDisplayTime.Utc(value),Is.EqualTo("29 Sep 2026, 07:52:28 UTC"));
  Assert.That(value.ToString("O"),Is.EqualTo("2026-09-29T08:52:28.1234567+01:00"));
 }
 [TestCase("2026-09-29T07:52:28Z","29 Sep 2026, 08:52:28 (UTC+01:00)")]
 [TestCase("2026-12-29T07:52:28Z","29 Dec 2026, 07:52:28 (UTC+00:00)")]
 public void ControllerDisplayHonorsItsTimeZoneIncludingSummerTime(string input,string expected) {
  var zone=TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
  Assert.That(SubmissionDisplayTime.InTimeZone(DateTimeOffset.Parse(input,CultureInfo.InvariantCulture),zone),Is.EqualTo(expected));
 }
}
