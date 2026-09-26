// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Renci.SshNet;

namespace CrestronHomeDevTools;

/// <summary>A complete bounded response to err plogcurrent; raw output may contain private diagnostic data.</summary>
public sealed record ProcessorErrorLogSnapshot(string Host, DateTimeOffset RequestSentUtc, DateTimeOffset ObservedUtc,
    string RawResponse, string[] Entries, string[] ConsoleLines, string[] UnrecognizedLines);

/// <summary>Comparable means the entire earlier persistent entry sequence remains an exact prefix.</summary>
public sealed record ProcessorErrorLogInterval(bool Comparable, bool NoNewErrorsOrExceptions, string Reason,
    string[] NewEntries, string[] ConcernLines);

/// <summary>Reads persistent logs without clearing them or changing logging settings.</summary>
public static class ProcessorErrorLog
{
    private const int Limit = 2 * 1024 * 1024;
    private const string Command = "err plogcurrent";
    private const string Header = "Persistent log contents during current boot:";
    private static readonly Regex Prompt = new(@"(?:^|[\r\n])([A-Za-z0-9][A-Za-z0-9_.:-]{0,80}>)", RegexOptions.CultureInvariant);
    private static readonly Regex Entry = new(@"^(Ok|Info|Notice|Warning|Error|Fatal):", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex Live = new(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}(?:\.\d+)? \[", RegexOptions.CultureInvariant);
    private static readonly Regex Concern = new(@"^(?:Error|Fatal):|\[(?:ERROR|FATAL)\]|\bexception\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static async Task<ProcessorErrorLogSnapshot> ReadAsync(string host, NetworkCredential credential,
        string sshFingerprint, TimeSpan timeout, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentException.ThrowIfNullOrWhiteSpace(sshFingerprint);
        ValidateTimeout(timeout);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout);
        using var client = new SshClient(host, credential.UserName, credential.Password);
        client.ConnectionInfo.Timeout = timeout;
        client.HostKeyReceived += (_, e) => e.CanTrust = e.FingerPrintSHA256 == sshFingerprint;
        await client.ConnectAsync(deadline.Token).ConfigureAwait(false);
        using var shell = client.CreateShellStream("xterm", 100, 24, 800, 600, 65536);
        return await ReadCoreAsync(new Session(client, shell), host, timeout, deadline.Token).ConfigureAwait(false);
    }

    internal static async Task<ProcessorErrorLogSnapshot> ReadCoreAsync(IUptimeSession session, string host,
        TimeSpan timeout, CancellationToken token)
    {
        ValidateTimeout(timeout);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout);
        var buffer = new StringBuilder();
        string? prompt = null;
        DateTimeOffset sent = default;
        while (true)
        {
            deadline.Token.ThrowIfCancellationRequested();
            if (!session.IsConnected) throw new IOException("Processor disconnected before the log response completed.");
            buffer.Append(session.ReadAvailable());
            if (buffer.Length > Limit) throw new InvalidDataException("Processor log response exceeded the 2 Mi-character bound; no complete log was obtained.");
            string text = buffer.ToString();
            if (prompt == null)
            {
                var match = Prompt.Match(text);
                if (match.Success)
                {
                    prompt = match.Groups[1].Value;
                    buffer.Clear();
                    sent = DateTimeOffset.UtcNow;
                    session.WriteLine(Command);
                }
            }
            else if (Regex.IsMatch(text, @"(?:^|[\r\n])" + Regex.Escape(prompt) + @"[ \r\n]*$", RegexOptions.CultureInvariant))
                return Parse(host, text, prompt, sent, DateTimeOffset.UtcNow);
            await Task.Delay(50, deadline.Token).ConfigureAwait(false);
        }
    }

    internal static ProcessorErrorLogSnapshot Parse(string host, string text, string prompt,
        DateTimeOffset sent, DateTimeOffset observed)
    {
        if (text.Length > Limit || observed < sent) throw new InvalidDataException("Invalid log response bound or observation clock.");
        var end = Regex.Match(text, @"(?:^|[\r\n])" + Regex.Escape(prompt) + @"[ \r\n]*$", RegexOptions.CultureInvariant);
        if (!end.Success) throw new InvalidDataException("Log response has no complete terminal prompt.");
        var entries = new List<string>();
        var live = new List<string>();
        var unknown = new List<string>();
        var current = new List<string>();
        bool header = false, ended = false;
        foreach (string rawLine in text[..end.Index].Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            if (line == Header)
            {
                if (header) throw new InvalidDataException("Ambiguous persistent log response.");
                header = true;
            }
            else if (line == "Persistent log contents during current boot end" && header && !ended)
            {
                if (current.Count > 0) entries.Add(string.Join('\n', current));
                current.Clear(); ended = true;
            }
            else if (line == Command || string.IsNullOrWhiteSpace(line)) { }
            else if (Live.IsMatch(line)) live.Add(line);
            else if (header && !ended && Entry.IsMatch(line))
            {
                if (current.Count > 0) entries.Add(string.Join('\n', current));
                current.Clear(); current.Add(line);
            }
            else if (header && !ended && current.Count > 0) current.Add(line);
            else unknown.Add(line);
        }
        if (!header) throw new InvalidDataException("The processor did not return its current persistent log.");
        if (current.Count > 0) entries.Add(string.Join('\n', current));
        return new(host, sent, observed, text, entries.ToArray(), live.ToArray(), unknown.ToArray());
    }

    /// <summary>Reports new diagnostics, not their cause or a driver certification result.</summary>
    public static ProcessorErrorLogInterval Compare(ProcessorErrorLogSnapshot before, ProcessorErrorLogSnapshot after)
    {
        ArgumentNullException.ThrowIfNull(before); ArgumentNullException.ThrowIfNull(after);
        if (!string.Equals(before.Host, after.Host, StringComparison.OrdinalIgnoreCase) || after.RequestSentUtc < before.ObservedUtc)
            throw new ArgumentException("Use successive observations of the same processor.");
        if (before.Entries.Length == 0 || before.UnrecognizedLines.Length != 0 || after.UnrecognizedLines.Length != 0)
            return new(false, false, "No nonempty baseline or unrecognized console output; inspect raw responses.", [], []);
        if (after.Entries.Length < before.Entries.Length || !before.Entries.SequenceEqual(after.Entries.Take(before.Entries.Length), StringComparer.Ordinal))
            return new(false, false, "Persistent log baseline changed or disappeared; do not infer a clean interval.", [], []);
        var added = after.Entries.Skip(before.Entries.Length).ToArray();
        bool suspended = false;
        foreach (string entry in before.Entries)
        {
            if (entry.Contains("logging is suspended", StringComparison.OrdinalIgnoreCase)) suspended = true;
            if (entry.Contains("logging is resumed", StringComparison.OrdinalIgnoreCase)) suspended = false;
        }
        if (suspended || added.Any(e => e.Contains("logging is suspended", StringComparison.OrdinalIgnoreCase)))
            return new(false, false, "Logging was suspended during the observation interval.", added, []);
        var concerns = added.Concat(after.ConsoleLines).Where(e => Concern.IsMatch(e)).ToArray();
        return new(true, concerns.Length == 0,
            concerns.Length == 0 ? "No new Error/Fatal entries or exception mentions observed in this retained interval."
                : "New error/fatal or exception diagnostics require review; attribution is not inferred.", added, concerns);
    }

    private static void ValidateTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(2)) throw new ArgumentOutOfRangeException(nameof(timeout));
    }
    private sealed class Session(SshClient client, ShellStream shell) : IUptimeSession
    {
        public bool IsConnected => client.IsConnected;
        public string ReadAvailable() => shell.DataAvailable ? shell.Read() : string.Empty;
        public void WriteLine(string command) => shell.WriteLine(command);
    }
}
