// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Xml;
using System.Xml.Linq;

namespace CrestronHomeDevTools.Automation;

// Reads original NUnit results without rewriting them or turning an invocation
// failure into a pass. The coordinator separately verifies candidate, fixture,
// reservation/restoration and immutable inventory bindings.
internal static class AutomationAppCaseResults
{
    internal sealed record Case(string Name, string Outcome, string ExecutionId);
    internal sealed record Snapshot(string TrxSha256, Case[] Cases);
    internal sealed record AcceptedCase(string Name, string TrxSha256, string ExecutionId);
    internal sealed record Recovery(AcceptedCase[] Accepted, string[] Outstanding);

    internal static Snapshot Read(string path, string expectedSha256)
    {
        if (expectedSha256.Length != 64 || !expectedSha256.All(char.IsAsciiHexDigit) ||
            AutomationFiles.Hash(path) != expectedSha256.ToLowerInvariant())
            throw new InvalidDataException("The inspected NUnit result changed.");
        using var reader = XmlReader.Create(path, new XmlReaderSettings {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 20 * 1024 * 1024 });
        var root = XDocument.Load(reader).Root ?? throw new InvalidDataException("Missing TRX root.");
        if (root.Name.LocalName != "TestRun") throw new InvalidDataException("Expected a TRX test run.");
        var ns = root.Name.Namespace;
        string Required(XElement e, string key) => (string?)e.Attribute(key) is { Length: > 0 } value
            ? value : throw new InvalidDataException("Missing TRX " + key + ".");
        var definitions = root.Element(ns + "TestDefinitions")?.Elements(ns + "UnitTest").ToArray()
            ?? throw new InvalidDataException("Missing NUnit definitions.");
        if (definitions.Select(d => Required(d, "id")).Distinct(StringComparer.Ordinal).Count() != definitions.Length)
            throw new InvalidDataException("Duplicate NUnit definitions.");
        var byId = definitions.ToDictionary(d => Required(d, "id"), StringComparer.Ordinal);
        var results = root.Element(ns + "Results")?.Elements(ns + "UnitTestResult").ToArray()
            ?? throw new InvalidDataException("Missing NUnit results.");
        if (results.Length is < 1 or > 1024 || results.Length != definitions.Length)
            throw new InvalidDataException("NUnit results must contain every defined case exactly once.");
        var seenTests = new HashSet<string>(StringComparer.Ordinal);
        var seenExecutions = new HashSet<string>(StringComparer.Ordinal);
        var cases = new List<Case>();
        foreach (var result in results)
        {
            string id = Required(result, "testId"), execution = Required(result, "executionId");
            if (!seenTests.Add(id) || !seenExecutions.Add(execution) || !byId.TryGetValue(id, out var definition))
                throw new InvalidDataException("Duplicate or undefined NUnit execution.");
            var method = definition.Element(ns + "TestMethod") ?? throw new InvalidDataException("Missing NUnit method.");
            if (Required(method, "adapterTypeName") != "executor://nunit3testexecutor/" ||
                Required(definition.Element(ns + "Execution") ?? throw new InvalidDataException("Missing definition execution."), "id") != execution)
                throw new InvalidDataException("NUnit execution identity differs from its definition.");
            string outcome = Required(result, "outcome");
            if (outcome is not ("Passed" or "Failed"))
                throw new InvalidDataException("Skipped, aborted or inconclusive cases require a complete reviewed selection.");
            cases.Add(new(Required(method, "className") + "." + Required(definition, "name"), outcome, execution));
        }
        if (cases.Select(c => c.Name).Distinct(StringComparer.Ordinal).Count() != cases.Count)
            throw new InvalidDataException("Ambiguous duplicate NUnit full names cannot be reused individually.");
        var summary = root.Element(ns + "ResultSummary") ?? throw new InvalidDataException("Missing TRX summary.");
        if (Required(summary, "outcome") is not ("Completed" or "Failed"))
            throw new InvalidDataException("The NUnit invocation did not finish.");
        var counters = summary.Element(ns + "Counters") ?? throw new InvalidDataException("Missing TRX counters.");
        int Count(string key) => int.TryParse(Required(counters, key), out var count) && count >= 0
            ? count : throw new InvalidDataException("Invalid TRX counter.");
        if (Count("total") != cases.Count || Count("executed") != cases.Count ||
            Count("passed") != cases.Count(c => c.Outcome == "Passed") ||
            Count("failed") != cases.Count(c => c.Outcome == "Failed"))
            throw new InvalidDataException("TRX totals disagree with the individual executions.");
        if (AutomationFiles.Hash(path) != expectedSha256.ToLowerInvariant())
            throw new InvalidDataException("NUnit result changed while reading it.");
        return new(expectedSha256.ToLowerInvariant(), cases.OrderBy(c => c.Name, StringComparer.Ordinal).ToArray());
    }

    internal static Recovery Evaluate(string[] required, params Snapshot[] attempts)
    {
        if (required.Length is < 1 or > 1024 || required.Any(string.IsNullOrWhiteSpace) ||
            required.Distinct(StringComparer.Ordinal).Count() != required.Length || attempts.Length is < 1 or > 128)
            throw new InvalidDataException("Use a bounded exact original case selection and retained attempt history.");
        var outstanding = required.ToHashSet(StringComparer.Ordinal);
        var accepted = new Dictionary<string, AcceptedCase>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var attempt in attempts)
        {
            if (attempt.TrxSha256.Length != 64 || !attempt.TrxSha256.All(char.IsAsciiHexDigit) || !seen.Add(attempt.TrxSha256) ||
                attempt.Cases.Length != outstanding.Count ||
                !attempt.Cases.Select(c => c.Name).ToHashSet(StringComparer.Ordinal).SetEquals(outstanding) ||
                attempt.Cases.Select(c => c.Name).Distinct(StringComparer.Ordinal).Count() != attempt.Cases.Length ||
                attempt.Cases.Any(c => c.Outcome is not ("Passed" or "Failed") || string.IsNullOrWhiteSpace(c.ExecutionId)))
                throw new InvalidDataException("A recovery must execute exactly the outstanding cases; passed cases cannot be repeated or omitted from provenance.");
            foreach (var item in attempt.Cases.Where(c => c.Outcome == "Passed"))
            {
                accepted.Add(item.Name, new(item.Name, attempt.TrxSha256, item.ExecutionId));
                outstanding.Remove(item.Name);
            }
        }
        return new(accepted.Values.OrderBy(c => c.Name, StringComparer.Ordinal).ToArray(), outstanding.Order(StringComparer.Ordinal).ToArray());
    }
}
