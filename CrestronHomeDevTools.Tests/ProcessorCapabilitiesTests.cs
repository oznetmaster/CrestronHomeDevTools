// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Net;
using System.Text.Json;
using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class ProcessorCapabilitiesTests
    {
    private static JsonElement Json (string text) => JsonSerializer.Deserialize<JsonElement> (text);

    [TestCase ("4.012.0194", IoPortConnectionFormat.Legacy)]
    [TestCase ("4.013.0134", IoPortConnectionFormat.Typed)]
    public async Task AuthenticatedIdentityIsRawButOtherReadsStillRequireAnEnvelope (string version, IoPortConnectionFormat expected)
        {
        var handler = new Handler ($$"""{"SystemType":"CP4-R","FirmwareVersion":"2.8000.57","ProgramVersion":"{{version}}","ConfigurationSchema":{"Devices":1,"NewResource":2},"FutureField":"ignored"}""");
        await using var connection = new ProcessorConnection (new HttpClient (handler) { BaseAddress = new ("https://processor.example/cws/api/") });
        var identity = await connection.ReadProcessorIdentityAsync ();
        Assert.That (handler.Requests, Is.EqualTo (new[] { "GET https://processor.example/cws/api/v2" }));
        Assert.That (identity.ConfigurationSchema!["NewResource"], Is.EqualTo (2));
        Assert.That (IoPortConnections.FormatForVersion (identity.ProgramVersion), Is.EqualTo (expected));
        await Assert.ThrowsAsync<ProcessorApiException> (async () => await connection.GetAsync<ProcessorIdentity> ("v2/Devices"));
        await Assert.ThrowsAsync<ArgumentException> (async () => await connection.GetAsync<ProcessorIdentity> ("v2"));
        Assert.That (handler.Requests.Count, Is.EqualTo (2));
        }

    [TestCase ("{\"Result\":{},\"Error\":null}")]
    [TestCase ("{}")]
    [TestCase ("{\"Error\":\"private-sentinel\"}")]
    public async Task InvalidIdentificationDoesNotBecomePermissionOrExposeErrorText (string response)
        {
        await using var connection = new ProcessorConnection (new HttpClient (new Handler (response)) { BaseAddress = new ("https://processor.example/cws/api/") });
        var error = await Assert.ThrowsAsync<ProcessorApiException> (async () => await connection.ReadProcessorIdentityAsync ());
        Assert.That (error!.ToString (), Does.Not.Contain ("private-sentinel"));
        }

    [Test]
    public async Task DiscoveryProjectsIdentifiersAndPortsWithoutConfigurationValues ()
        {
        var provider = new DeviceInfo { Id = 52, Commands = ["read", "read"], PropertyValues = new () {
            ["secretProperty"] = Json ("\"private-sentinel\""),
            ["cp.processorSoftwareUpdate:properties"] = Json ("{\"ReleaseLevel\":\"crestronhomebeta\",\"Password\":\"private-sentinel\"}"),
            ["cp.ioPorts"] = Json ("[{\"Type\":\"Com\",\"PortNumber\":1,\"Name\":\"Serial1\",\"SerialSettings\":{\"Password\":\"private-sentinel\"}},{\"Type\":\"FuturePort\",\"PortNumber\":2}]") } };
        var connection = new IdentityConnection ("4.013.0134", provider, new () { Id = 99 });
        var report = await new ConfigurationClient (connection).GetCapabilitiesAsync ();
        Assert.That (report.Identity!.ProgramVersion, Is.EqualTo ("4.013.0134"));
        Assert.That (report.ReleaseLevel, Is.EqualTo ("crestronhomebeta"));
        Assert.That (report.Devices[0].Commands, Is.EqualTo (new[] { "read" }));
        Assert.That (report.Devices[0].IoPorts!.Select (p => p.Type), Is.EqualTo (new[] { "Serial", "FuturePort" }));
        Assert.That (report.Devices[1].IoPorts, Is.Null);
        Assert.That (JsonSerializer.Serialize (report), Does.Not.Contain ("private-sentinel").And.Not.Contain ("SerialSettings"));
        Assert.That (connection.Reads, Is.EqualTo (new[] { "v2/Devices" }));
        }

    [Test]
    public async Task ExistingCustomConnectionsRemainUsableWithoutIdentityInterface ()
        {
        var report = await new ConfigurationClient (new ReadOnlyConnection (new DeviceInfo { Id = 1 })).GetCapabilitiesAsync ();
        Assert.That (report.Identity, Is.Null);
        Assert.That (report.PortConnectionFormat, Is.EqualTo (IoPortConnectionFormat.Unknown));
        Assert.That (report.Devices.Count, Is.EqualTo (1));
        }

    [TestCase ("[]", 0)]
    [TestCase ("null", -1)]
    [TestCase ("{}", -1)]
    [TestCase ("[null]", -1)]
    [TestCase ("[{\"Type\":\"Serial\"}]", -1)]
    [TestCase ("[{\"Type\":\"Serial\",\"PortNumber\":-1}]", -1)]
    public void MissingOrMalformedPortsDoNotInventAnEmptySupportedList (string json, int count)
        {
        var ports = ProcessorCapabilities.ReadPorts (new () { PropertyValues = new () { ["cp.ioPorts"] = Json (json) } });
        Assert.That (ports?.Count ?? -1, Is.EqualTo (count));
        }

    private sealed class Handler (string response) : HttpMessageHandler
        {
        public List<string> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync (HttpRequestMessage request, CancellationToken token)
            {
            Requests.Add (request.Method + " " + request.RequestUri);
            return Task.FromResult (new HttpResponseMessage (HttpStatusCode.OK) { Content = new StringContent (response) });
            }
        }

    internal class ReadOnlyConnection (params DeviceInfo[] devices) : IConfigurationConnection
        {
        public List<string> Reads { get; } = [];
        public DeviceInfo[] Devices { get; set; } = devices;
        public Task<T?> GetAsync<T> (string relativePath, CancellationToken token = default)
            {
            token.ThrowIfCancellationRequested ();
            Reads.Add (relativePath);
            object value = relativePath == "v2/Devices" ? Devices.ToDictionary (d => d.Id.ToString ())
                : Devices.Single (d => relativePath == "v2/Devices/" + d.Id);
            return Task.FromResult (JsonSerializer.Deserialize<T> (JsonSerializer.Serialize (value)));
            }
        public Task<T?> ExecuteAsync<T> (int deviceId, string commandName, object? parameters = null, CancellationToken token = default)
            => throw new AssertionException ("Discovery/preparation must never issue commands.");
        public Task<T?> ExecuteResourceCommandAsync<T> (string resourcePath, string commandName, object? parameters = null, CancellationToken token = default)
            => throw new AssertionException ("Discovery/preparation must never issue resource commands.");
        public Task<OperationResult> WaitForOperationAsync (string id, TimeSpan timeout, CancellationToken token = default) => throw new AssertionException ("No operation may start.");
        public ValueTask DisposeAsync () => ValueTask.CompletedTask;
        }

    internal sealed class IdentityConnection (string? version, params DeviceInfo[] devices) : ReadOnlyConnection (devices), IProcessorIdentitySource
        {
        public string? Version { get; set; } = version;
        public Task<ProcessorIdentity> ReadProcessorIdentityAsync (CancellationToken token = default) => Task.FromResult (new ProcessorIdentity
            { SystemType = "CP4-R", ProgramVersion = Version, ConfigurationSchema = new () { ["Devices"] = 1 } });
        }
    }
