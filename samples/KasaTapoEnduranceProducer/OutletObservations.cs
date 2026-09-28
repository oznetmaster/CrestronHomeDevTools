// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License.
using System.Net.Sockets;
using System.Text.Json;
using CrestronHomeDevTools;

sealed class OutletObservationException(string code, bool identityFailure = false) : Exception
{
    public string Code { get; } = code;
    public bool IdentityFailure { get; } = identityFailure;
}

static class ProbeDiagnostics
{
    // Do not serialize Message, Data, request URIs or response bodies: SDK errors may contain credentials.
    public static object Describe(Exception error)
    {
        var chain = new List<object>();
        for (Exception? current = error; current != null && chain.Count < 8; current = current.InnerException)
            chain.Add(new { Type = current.GetType().FullName, current.HResult,
                SocketError = current is SocketException socket ? socket.SocketErrorCode.ToString() : null,
                HttpStatus = current is HttpRequestException http ? (int?)http.StatusCode : null });
        return new { Code = error is OutletObservationException known ? known.Code : "external-observation-error", Exceptions = chain };
    }
}

static class OutletObservations
{
    internal static async Task<SubmissionEvidenceOutcome> ReadAsync(OutletTarget[] targets,
        Dictionary<string, object?> evidence,
        Func<OutletTarget, Dictionary<string, object?>, CancellationToken, Task<bool>> read,
        CancellationToken token)
    {
        var observations = new List<Dictionary<string, object?>>();
        evidence["independentOutlets"] = observations;
        var overall = SubmissionEvidenceOutcome.Passed;
        foreach (var target in targets)
        {
            token.ThrowIfCancellationRequested();
            var observation = new Dictionary<string, object?> { ["alias"] = target.Alias,
                ["startedUtc"] = DateTimeOffset.UtcNow, ["stage"] = "start", ["outcome"] = "NotTested" };
            observations.Add(observation);
            try
            {
                observation["power"] = await read(target, observation, token);
                observation["stage"] = "complete";
                observation["outcome"] = "Passed";
            }
            catch (Exception error) when (error is not (OutOfMemoryException or OperationCanceledException))
            {
                bool identityFailure = error is OutletObservationException { IdentityFailure: true };
                observation["outcome"] = identityFailure ? "Failed" : "Inconclusive";
                observation["category"] = identityFailure ? "physical-identity-check-failed" : "independent-observation-unavailable";
                observation["diagnostic"] = ProbeDiagnostics.Describe(error);
                if (identityFailure) overall = SubmissionEvidenceOutcome.Failed;
                else if (overall != SubmissionEvidenceOutcome.Failed) overall = SubmissionEvidenceOutcome.Inconclusive;
            }
            finally { observation["observedUtc"] = DateTimeOffset.UtcNow; }
        }
        return overall;
    }
}

static class OutletChecks
{
    internal static async Task RunAsync()
    {
        var targets = new[] { new OutletTarget("first", "one", "one", null), new OutletTarget("second", "two", "two", null), new OutletTarget("third", "three", "three", null) };
        var evidence = new Dictionary<string, object?>();
        var calls = new List<string>();
        var outcome = await OutletObservations.ReadAsync(targets, evidence, (target, row, _) =>
        {
            calls.Add(target.Alias);
            row["stage"] = "connect-and-authenticate";
            if (target.Alias == "second") throw new HttpRequestException("password=secret-value", new SocketException((int)SocketError.ConnectionRefused));
            return Task.FromResult(false);
        }, default);
        string retained = JsonSerializer.Serialize(evidence);
        if (outcome != SubmissionEvidenceOutcome.Inconclusive || calls.Count != 3 ||
            retained.Contains("secret-value") || !retained.Contains("ConnectionRefused") || !retained.Contains("second") || !retained.Contains("connect-and-authenticate"))
            throw new Exception("Per-outlet evidence or safe diagnostics failed.");
        outcome = await OutletObservations.ReadAsync(targets, evidence, (target, _, _) =>
            target.Alias == "second" ? throw new OutletObservationException("authenticated-identity-mismatch", true) : Task.FromResult(true), default);
        if (outcome != SubmissionEvidenceOutcome.Failed) throw new Exception("Identity failure was downgraded.");
        foreach (string code in new[] { "discovery-not-found", "power-state-unavailable" })
        {
            outcome = await OutletObservations.ReadAsync(targets, evidence, (_, _, _) => throw new OutletObservationException(code), default);
            if (outcome != SubmissionEvidenceOutcome.Inconclusive || !JsonSerializer.Serialize(evidence).Contains(code))
                throw new Exception("Unavailable observation lost its cause.");
        }
        try
        {
            await OutletObservations.ReadAsync(targets, evidence, (_, _, _) => throw new OperationCanceledException(), default);
            throw new Exception("Cancellation was swallowed.");
        }
        catch (OperationCanceledException)
        {
            if (!JsonSerializer.Serialize(evidence).Contains("first")) throw new Exception("Interrupted outlet lost its context.");
        }
    }
}
