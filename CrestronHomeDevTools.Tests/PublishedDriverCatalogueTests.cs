// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Net;
using System.Text.Json;
using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class PublishedDriverCatalogueTests
	{
	private sealed class Handler (Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
		{
		protected override Task<HttpResponseMessage> SendAsync (HttpRequestMessage request, CancellationToken token) => send (request, token);
		}
	private static object Row (int number, int total, string model = "Gateway", string manufacturer = "Fixture", string developer = "Test Developer",
		bool available = true, bool beta = false, int modelId = 2) => new
		{
		rowNum = number, totalRows = total, id = number, modelId, modelName = model, manufacturer, developer,
		driverVersion = "2.1.0", deviceType = "Platform", communicationType = "Cloud", platformsSupported = "4-Series",
		isExtensionDevice = false, certification = "Self", licenseRequired = false, available, isBetaDriver = beta
		};
	private static HttpClient Client (object rows) => new (new Handler ((_, _) => Task.FromResult (new HttpResponseMessage (HttpStatusCode.OK)
		{ Content = new StringContent (JsonSerializer.Serialize (rows)) })));

	[Test]
	public async Task SearchesWholePublishedCatalogueWithoutProcessorOrLocalOverlay ()
		{
		using var http = new HttpClient (new Handler (async (request, token) =>
			{
			Assert.That (request.Method, Is.EqualTo (HttpMethod.Post));
			Assert.That (request.RequestUri!.ToString (), Is.EqualTo ("https://drivers-api.crestron.io/driver/findMany?isBeta=0"));
			Assert.That (request.Headers.Authorization, Is.Null);
			using var body = JsonDocument.Parse (await request.Content!.ReadAsStringAsync (token));
			Assert.That (body.RootElement.GetProperty ("globalFilter").GetString (), Is.Empty);
			// There is deliberately no local inventory input: even a locally sideloaded
			// copy of this identity cannot suppress the published record.
			return new (HttpStatusCode.OK) { Content = new StringContent (JsonSerializer.Serialize (new[]
				{ Row (1, 3, "Shade Gateway", "Somfy"), Row (2, 3, available: false), Row (3, 3, beta: true) })) };
			}));
		var result = await new PublishedDriverCatalogue (http).SearchAsync ("somfy\tshade TEST");
		Assert.That (result.TotalCatalogueModelEntries, Is.EqualTo (3));
		Assert.That (result.Drivers.Select (d => d.Id), Is.EqualTo (new[] { 1 }));
		Assert.That (result.Drivers[0].HelpUrl, Is.EqualTo ("https://drivers-api.crestron.io/help/driver/1"));
		Assert.That (result.Scope, Does.Contain ("Home compatibility is not established"));
		}

	[Test]
	public async Task MoreThanTwoHundredRowsAreSearchable ()
		{
		using var http = Client (Enumerable.Range (1, 300).Select (n => Row (n, 300, n == 300 ? "Last Model" : "Other")).ToArray ());
		var result = await new PublishedDriverCatalogue (http).SearchAsync ("Last Model");
		Assert.That (result.Drivers.Single ().Id, Is.EqualTo (300));
		}

	[Test]
	public async Task PreservesManufacturerOnlyRowsAndReturnsEmptySearchTruthfully ()
		{
		using var http = Client (new[] { Row (1, 1, model: "", modelId: 0) });
		var catalogue = new PublishedDriverCatalogue (http);
		Assert.That ((await catalogue.SearchAsync ("fixture")).Drivers.Single ().ModelName, Is.Empty);
		Assert.That ((await catalogue.SearchAsync ("absent")).Drivers, Is.Empty);
		}

	[TestCase ("null")]
	[TestCase ("[]")]
	[TestCase ("[null]")]
	[TestCase ("[{\"rowNum\":1,\"totalRows\":201}]")]
	public async Task RejectsMissingOrTruncatedResponses (string json)
		{
		using var http = new HttpClient (new Handler ((_, _) => Task.FromResult (new HttpResponseMessage (HttpStatusCode.OK) { Content = new StringContent (json) })));
		await Assert.ThrowsAsync<InvalidDataException> (async () => await new PublishedDriverCatalogue (http).SearchAsync ());
		}

	[Test]
	public async Task RejectsDuplicateRowNumbersEvenWhenCountMatches ()
		{
		using var http = Client (new[] { Row (1, 2), Row (1, 2) });
		await Assert.ThrowsAsync<InvalidDataException> (async () => await new PublishedDriverCatalogue (http).SearchAsync ());
		}

	[Test]
	public async Task CliRejectsProcessorOptionsRatherThanFallingBackToLocalSearch ()
		{
		using var output = new StringWriter (); using var error = new StringWriter ();
		var exit = await PublishedDriversCommand.RunAsync (["--processor", "192.0.2.1"], output, error, default);
		Assert.That (exit, Is.EqualTo (2)); Assert.That (output.ToString (), Is.Empty);
		}

	[Test]
	public async Task CliReportsNetworkFailureWithoutEmittingResults ()
		{
		using var http = new HttpClient (new Handler ((_, _) => Task.FromResult (new HttpResponseMessage (HttpStatusCode.ServiceUnavailable))));
		using var output = new StringWriter (); using var error = new StringWriter ();
		var exit = await PublishedDriversCommand.RunAsync ([], output, error, default, new (http));
		Assert.That (exit, Is.EqualTo (3)); Assert.That (output.ToString (), Is.Empty);
		Assert.That (error.ToString (), Does.Contain ("No processor catalogue fallback"));
		}
	}
