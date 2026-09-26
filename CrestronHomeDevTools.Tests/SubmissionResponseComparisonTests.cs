// Copyright (c) 2026 Neil Colvin. MIT licensed.
using NUnit.Framework;
namespace CrestronHomeDevTools.Tests;

public sealed class SubmissionResponseComparisonTests
{
    private readonly SubmissionEvidenceIdentity identity = new(new('a',64),new('b',40),new('c',64),new('d',64));
    private readonly DateTimeOffset start = DateTimeOffset.Parse("2026-01-01T12:00:00Z");
    private SubmissionResponseSeries Series(bool after,double duration=500) => new(1,identity,"synthetic-device","synthetic-observer",
        [new("room:on",start.AddHours(after?2:-1),start.AddHours(after?2:-1).AddSeconds(1),duration)]);
    private SubmissionResponseComparisonReport Compare(SubmissionResponseSeries first,SubmissionResponseSeries last,SubmissionResponseLimits? limits=null) =>
        SubmissionResponseComparison.Compare(first,last,identity,start,start.AddHours(1),limits);
    [Test] public void ComparisonWithoutReviewedLimitsDoesNotClaimAcceptance() {
        var report=Compare(Series(false),Series(true,600));
        Assert.That(report.WithinReviewedLimits,Is.Null);
        Assert.That(report.Differences.Single(),Is.EqualTo(new SubmissionResponseDifference("room:on",500,600,100,1.2,null)));
    }
    [TestCase(600,true)][TestCase(700,false)]
    public void AllExplicitLimitsMustPass(double after,bool expected) {
        var report=Compare(Series(false),Series(true,after),new(1000,150,1.25,"Synthetic test limits; not Crestron requirements."));
        Assert.That(report.WithinReviewedLimits,Is.EqualTo(expected));
    }
    [Test] public void CandidateDeviceMethodAndRequestedStateMustMatch() {
        Assert.Throws<InvalidDataException>(()=>Compare(Series(false),Series(true) with{Identity=identity with{PackageSha256=new('e',64)}}));
        Assert.Throws<InvalidDataException>(()=>Compare(Series(false),Series(true) with{DeviceIdentity="another"}));
        Assert.Throws<InvalidDataException>(()=>Compare(Series(false),Series(true) with{Method="another"}));
        Assert.Throws<InvalidDataException>(()=>Compare(Series(false),Series(true) with{Measurements=[Series(true).Measurements[0] with{Id="room:off"}]}));
    }
    [Test] public void MeasurementsMustBracketEnduranceAndHaveDistinctFiniteValues() {
        Assert.Throws<InvalidDataException>(()=>Compare(Series(true),Series(false)));
        Assert.Throws<InvalidDataException>(()=>Compare(Series(false),Series(true,double.NaN)));
        Assert.Throws<InvalidDataException>(()=>Compare(Series(false,0),Series(true)));
        Assert.Throws<InvalidDataException>(()=>Compare(Series(false),Series(true) with{Measurements=[Series(true).Measurements[0],Series(true).Measurements[0]]}));
    }
    [Test] public void LimitsNeedAReviewRationaleAndCannotBeNonfinite() {
        Assert.Throws<InvalidDataException>(()=>Compare(Series(false),Series(true),new(1000,100,1.2,"")));
        Assert.Throws<InvalidDataException>(()=>Compare(Series(false),Series(true),new(double.PositiveInfinity,100,1.2,"test")));
    }
}
