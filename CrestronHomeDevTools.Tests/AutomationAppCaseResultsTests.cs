// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Xml.Linq;
using CrestronHomeDevTools.Automation;
using NUnit.Framework;
namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class AutomationAppCaseResultsTests
{
    string root = null!;
    [SetUp] public void Setup() { root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "case-results-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); }
    [TearDown] public void Cleanup() { Directory.Delete(root, true); }
    internal static XDocument Trx(params (string Name, string Outcome)[] cases)
    {
        XNamespace n = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        var ids = cases.Select(_ => (Test: Guid.NewGuid().ToString(), Execution: Guid.NewGuid().ToString())).ToArray();
        return new(new XElement(n + "TestRun",
            new XElement(n + "TestDefinitions", cases.Select((c, i) => new XElement(n + "UnitTest",
                new XAttribute("id", ids[i].Test), new XAttribute("name", c.Name),
                new XElement(n + "Execution", new XAttribute("id", ids[i].Execution)),
                new XElement(n + "TestMethod", new XAttribute("className", "Fixture"), new XAttribute("adapterTypeName", "executor://nunit3testexecutor/"))))),
            new XElement(n + "Results", cases.Select((c, i) => new XElement(n + "UnitTestResult",
                new XAttribute("testId", ids[i].Test), new XAttribute("executionId", ids[i].Execution), new XAttribute("outcome", c.Outcome)))),
            new XElement(n + "ResultSummary", new XAttribute("outcome", cases.Any(c => c.Outcome == "Failed") ? "Failed" : "Completed"),
                new XElement(n + "Counters", new XAttribute("total", cases.Length), new XAttribute("executed", cases.Length),
                    new XAttribute("passed", cases.Count(c => c.Outcome == "Passed")), new XAttribute("failed", cases.Count(c => c.Outcome == "Failed"))))));
    }
    AutomationAppCaseResults.Snapshot Read(XDocument xml)
    {
        string path = Path.Combine(root, Guid.NewGuid().ToString("N") + ".trx"); xml.Save(path);
        return AutomationAppCaseResults.Read(path, AutomationFiles.Hash(path));
    }
    [Test] public void TenOriginalPassesAndOneRepairedCaseRetainSeparateExecutionProvenance()
    {
        var names = Enumerable.Range(0, 11).Select(i => "Case" + i).ToArray();
        var original = Read(Trx(names.Select((n, i) => (n, i == 10 ? "Failed" : "Passed")).ToArray()));
        var repair = Read(Trx((names[10], "Passed")));
        var result = AutomationAppCaseResults.Evaluate(names.Select(n => "Fixture." + n).ToArray(), original, repair);
        Assert.That(result.Outstanding, Is.Empty); Assert.That(result.Accepted, Has.Length.EqualTo(11));
        Assert.That(result.Accepted.Count(c => c.TrxSha256 == original.TrxSha256), Is.EqualTo(10));
        Assert.That(result.Accepted.Single(c => c.Name == "Fixture.Case10").TrxSha256, Is.EqualTo(repair.TrxSha256));
        Assert.That(original.Cases.Single(c => c.Name == "Fixture.Case10").Outcome, Is.EqualTo("Failed"));
    }
    [Test] public void FailedFollowupRemainsOutstandingAndMayHaveAnotherExplicitAttempt()
    {
        var first = Read(Trx(("A", "Passed"), ("B", "Failed")));
        var second = Read(Trx(("B", "Failed")));
        var third = Read(Trx(("B", "Passed")));
        Assert.That(AutomationAppCaseResults.Evaluate(["Fixture.A", "Fixture.B"], first, second).Outstanding, Is.EqualTo(new[] { "Fixture.B" }));
        Assert.That(AutomationAppCaseResults.Evaluate(["Fixture.A", "Fixture.B"], first, second, third).Outstanding, Is.Empty);
    }
    [TestCase("repeat-pass")][TestCase("missing-failure")][TestCase("different-case")][TestCase("missing-original")]
    public void SelectionCannotSilentlyRerunOrDropCases(string change)
    {
        var first = Read(Trx(("A", "Passed"), ("B", "Failed"), ("C", "Failed")));
        var next = change switch {
            "repeat-pass" => Read(Trx(("A", "Passed"), ("B", "Passed"), ("C", "Passed"))),
            "missing-failure" => Read(Trx(("B", "Passed"))),
            _ => Read(Trx(("B", "Passed"), ("D", "Passed"))) };
        Assert.Throws<InvalidDataException>(() => AutomationAppCaseResults.Evaluate(
            change == "missing-original" ? ["Fixture.A", "Fixture.B"] : ["Fixture.A", "Fixture.B", "Fixture.C"], first, next));
    }
    [TestCase("counter")][TestCase("definition")][TestCase("execution")][TestCase("adapter")]
    [TestCase("duplicate-name")][TestCase("skipped")][TestCase("aborted")]
    public void InconsistentOrAmbiguousTrxCannotSupplyRetainedPasses(string change)
    {
        var xml = Trx(("A", "Passed"), ("B", "Failed")); var n = xml.Root!.Name.Namespace;
        var definitions = xml.Descendants(n + "UnitTest").ToArray();
        var results = xml.Descendants(n + "UnitTestResult").ToArray();
        switch(change) {
            case "counter": xml.Descendants(n + "Counters").Single().SetAttributeValue("passed", 2); break;
            case "definition": results[1].SetAttributeValue("testId", "unknown"); break;
            case "execution": results[1].SetAttributeValue("executionId", (string?)results[0].Attribute("executionId")); break;
            case "adapter": definitions[0].Element(n + "TestMethod")!.SetAttributeValue("adapterTypeName", "other"); break;
            case "duplicate-name": definitions[1].SetAttributeValue("name", "A"); break;
            case "skipped": results[1].SetAttributeValue("outcome", "NotExecuted"); break;
            case "aborted": xml.Descendants(n + "ResultSummary").Single().SetAttributeValue("outcome", "Aborted"); break;
        }
        Assert.Throws<InvalidDataException>(() => Read(xml));
    }
    [Test] public void ChangedRetainedTrxIsRejected()
    {
        string path = Path.Combine(root, "original.trx"); Trx(("A", "Passed")).Save(path);
        string hash = AutomationFiles.Hash(path); File.AppendAllText(path, " ");
        Assert.Throws<InvalidDataException>(() => AutomationAppCaseResults.Read(path, hash));
    }
    [Test] public void ExternalXmlEntitiesAreProhibited()
    {
        string path = Path.Combine(root, "entity.trx");
        File.WriteAllText(path, "<!DOCTYPE TestRun [<!ENTITY e SYSTEM 'file:///unrelated'>]><TestRun>&e;</TestRun>");
        Assert.Throws<System.Xml.XmlException>(() => AutomationAppCaseResults.Read(path, AutomationFiles.Hash(path)));
    }
}
