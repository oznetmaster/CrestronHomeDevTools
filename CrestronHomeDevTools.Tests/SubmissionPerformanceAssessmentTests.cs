// Copyright (c) 2026 Neil Colvin. MIT licensed.
using NUnit.Framework;
namespace CrestronHomeDevTools.Tests;

public sealed class SubmissionPerformanceAssessmentTests
{
    private readonly SubmissionEvidenceIdentity identity = new(new('a',64),new('b',40),new('c',64),new('d',64));
    private readonly DateTimeOffset start = DateTimeOffset.Parse("2026-01-01T12:00:00Z");
    private SubmissionPerformanceAssessment Assessment() => new(1,identity,"system.response",start,start.AddHours(1),
        "synthetic reviewer",start.AddHours(2),[new("outlet","room:on",true,true,true,true,"Synthetic observed behavior.",
            [new("before/support.json",new('e',64))],[new("after/support.json",new('f',64))])]);
    private SubmissionEvidenceOutcome Assess(SubmissionPerformanceAssessment value) => SubmissionPerformanceReview.Assess(
        value,identity,"system.response",start,start.AddHours(1),
        new Dictionary<string,SubmissionResponseComparisonReport> { ["outlet"]=new(identity,"device","method",null,
            [new("room:on",500,1800,1300,3.6,null)]) });
    [Test] public void QualitativeEvidenceDoesNotApplyAnInventedNumericalLimit() =>
        Assert.That(Assess(Assessment()),Is.EqualTo(SubmissionEvidenceOutcome.Passed));
    [Test] public void UnconfirmedCriterionRemainsInconclusive() {
        var value=Assessment();
        Assert.That(Assess(value with {Findings=[value.Findings[0] with {FunctionsImmediate=null}]}),Is.EqualTo(SubmissionEvidenceOutcome.Inconclusive));
    }
    [Test] public void NegativeFindingIsFailureEvenWhenAnotherCriterionIsUnknown() {
        var value=Assessment();
        Assert.That(Assess(value with {Findings=[value.Findings[0] with {FunctionsImmediate=null,NoPerformanceDegradation=false}]}),Is.EqualTo(SubmissionEvidenceOutcome.Failed));
    }
    [Test] public void ReviewerIdentityIntervalAndCoverageCannotBeSubstituted() {
        var value=Assessment();
        Assert.Throws<InvalidDataException>(()=>Assess(value with {Reviewer=""}));
        Assert.Throws<InvalidDataException>(()=>Assess(value with {Identity=identity with {PackageSha256=new('f',64)}}));
        Assert.Throws<InvalidDataException>(()=>Assess(value with {EnduranceFinishedUtc=start.AddHours(2)}));
        Assert.Throws<InvalidDataException>(()=>Assess(value with {Findings=[]}));
        Assert.Throws<InvalidDataException>(()=>Assess(value with {Findings=[value.Findings[0],value.Findings[0]]}));
        Assert.Throws<InvalidDataException>(()=>Assess(value with {Findings=[value.Findings[0] with {Control="room:off"}]}));
    }
    [Test] public void AClaimWithoutSupportingEvidenceAndReasonIsRejected() {
        var value=Assessment();
        Assert.Throws<InvalidDataException>(()=>Assess(value with {Findings=[value.Findings[0] with {BeforeEvidence=[]}]}));
        Assert.Throws<InvalidDataException>(()=>Assess(value with {Findings=[value.Findings[0] with {Rationale=""}]}));
    }
}
