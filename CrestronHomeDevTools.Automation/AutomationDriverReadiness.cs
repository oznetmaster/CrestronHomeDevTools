// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Net;

namespace CrestronHomeDevTools.Automation;

internal sealed class InstalledAppPreflightException(string reasonCode, Exception inner)
    : Exception("Installed app preflight stopped before test execution. Inspect retained readiness evidence.", inner)
{
    internal string ReasonCode { get; } = reasonCode;
}

internal static class AutomationDriverReadiness
{
    internal static async Task Check(string host, string certificate, NetworkCredential credential,
        DriverInstanceReady target, string outputDirectory, CancellationToken token,
        IReadOnlyDictionary<int, string[]>? commands = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        await using var client = await ConfigurationClient.ConnectAsync(new ProcessorConnectionOptions
            { Host = host, CertificateSha256 = certificate }, credential, timeout.Token);
        var report = await DriverReadiness.InspectAsync(client, target.DeviceId, target.Model, target.Version, commands, timeout.Token);
        Directory.CreateDirectory(outputDirectory);
        AutomationFiles.Write(Path.Combine(outputDirectory, Guid.NewGuid().ToString("N") + ".json"), new { Host = host, Report = report });
        if (!report.Ready) throw new InvalidDataException("Installed driver or child readiness failed. Inspect the retained readiness report; no automatic reconfiguration was attempted.");
    }
}
