// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Net;
using System.Net.Security;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace CrestronHomeDevTools;

public sealed class ProcessorConnection : IConfigurationConnection
	{
	private static readonly JsonSerializerOptions JsonOptions = new () { PropertyNameCaseInsensitive = true };
	private readonly HttpClient _http;
	private readonly ClientWebSocket? _socket;
	private readonly CancellationTokenSource _lifetime = new ();
	private readonly SemaphoreSlim _requests = new (1);
	private readonly OperationTracker _operations = new ();
	private readonly DriverSwapTracker _swaps = new ();
	public Task<DriverSwapResult> WaitForDriverSwapAsync (string operationId, string driverId, TimeSpan timeout, CancellationToken cancellationToken = default) => _swaps.WaitAsync (operationId, driverId, timeout, cancellationToken);
	private Task? _eventsTask;
	private readonly TaskCompletionSource _disconnected = new (TaskCreationOptions.RunContinuationsAsynchronously);
	public Task WaitForDisconnectAsync (CancellationToken cancellationToken = default) => _disconnected.Task.WaitAsync (cancellationToken);
	private DateTimeOffset _lastValidation;
	private bool _disposed;

	internal ProcessorConnection (HttpClient http, ClientWebSocket? socket = null)
		{
		_http = http;
		_socket = socket;
		_lastValidation = DateTimeOffset.UtcNow;
		}

	public static async Task<ProcessorConnection> ConnectAsync (ProcessorConnectionOptions options, NetworkCredential credential, CancellationToken cancellationToken = default)
		{
		ArgumentNullException.ThrowIfNull (options);
		ArgumentNullException.ThrowIfNull (credential);
		ArgumentException.ThrowIfNullOrWhiteSpace (options.Host);
		ArgumentException.ThrowIfNullOrWhiteSpace (credential.UserName);
		if (Uri.CheckHostName (options.Host) == UriHostNameType.Unknown)
			throw new ArgumentException ("Host must be a hostname or IP address.", nameof (options));
		if (options.HttpsPort is < 1 or > 65535 || options.WebSocketPort is < 1 or > 65535)
			throw new ArgumentOutOfRangeException (nameof (options));
		if (options.RequestTimeout <= TimeSpan.Zero)
			throw new ArgumentOutOfRangeException (nameof (options));
		var pin = NormalizePin (options.CertificateSha256);
		var socket = new ClientWebSocket ();
		socket.Options.RemoteCertificateValidationCallback = (_, certificate, _, errors) => ValidateCertificate (certificate, errors, pin);
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource (cancellationToken);
		timeout.CancelAfter (options.RequestTimeout);
		HttpClient? http = null;
		try
			{
			await socket.ConnectAsync (new UriBuilder ("wss", options.Host, options.WebSocketPort).Uri, timeout.Token).ConfigureAwait (false);
			var login = JsonSerializer.SerializeToUtf8Bytes (new
				{
				UserName = credential.UserName,
				Password = credential.Password
				});
			try
				{
				await socket.SendAsync (login, WebSocketMessageType.Text, true, timeout.Token).ConfigureAwait (false);
				}
			finally { CryptographicOperations.ZeroMemory (login); }
			using var authentication = await ReadMessageAsync (socket, timeout.Token).ConfigureAwait (false);
			var auth = authentication.RootElement;
			if (!auth.TryGetProperty ("Authenticated", out var accepted) || accepted.ValueKind != JsonValueKind.True)
				throw new ProcessorApiException ("Processor authentication failed.");
			var authKey = RequiredString (auth, "RestV2Token");
			var authToken = RequiredString (auth, "WebApiToken");
			var port = options.HttpsPort;
			if (auth.TryGetProperty ("HttpPort", out var portField) && portField.ValueKind == JsonValueKind.Number && portField.TryGetInt32 (out var reportedPort))
				{
				if (reportedPort is < 1 or > 65535)
					throw new ProcessorApiException ("Invalid HTTPS port in authentication response.");
				port = reportedPort;
				}
			var handler = new HttpClientHandler
				{
				AllowAutoRedirect = false,
				ServerCertificateCustomValidationCallback = (_, certificate, _, errors) => ValidateCertificate (certificate, errors, pin)
				};
			http = new HttpClient (handler) { BaseAddress = new UriBuilder ("https", options.Host, port, "/cws/api/").Uri, Timeout = options.RequestTimeout };
			http.DefaultRequestHeaders.Add ("Crestron-RestAPI-AuthToken", authToken);
			http.DefaultRequestHeaders.Add ("Crestron-RestAPI-AuthKey", authKey);
			var connection = new ProcessorConnection (http, socket);
			await connection.ValidateSessionAsync (timeout.Token).ConfigureAwait (false);
			connection._eventsTask = connection.ReceiveEventsAsync ();
			return connection;
			}
		catch { http?.Dispose (); socket.Dispose (); throw; }
		}

	public Task<T?> GetAsync<T> (string relativePath, CancellationToken cancellationToken = default)
		 => SendAsync<T> (HttpMethod.Get, relativePath, null, cancellationToken);

	public Task<T?> ExecuteAsync<T> (int deviceId, string commandName, object? parameters = null, CancellationToken cancellationToken = default)
		{
		ArgumentException.ThrowIfNullOrWhiteSpace (commandName);
		return SendAsync<T> (HttpMethod.Post, $"v2/Devices/{deviceId}/Command", new
			{
			CommandName = commandName,
			Parameters = parameters ?? new
				{
				}
			}, cancellationToken);
		}

	public Task<OperationResult> WaitForOperationAsync (string operationId, TimeSpan timeout, CancellationToken cancellationToken = default)
		 => _operations.WaitAsync (operationId, timeout, cancellationToken);

	private async Task<T?> SendAsync<T> (HttpMethod method, string path, object? payload, CancellationToken cancellationToken)
		{
		ObjectDisposedException.ThrowIf (_disposed, this);
		ValidatePath (path);
		if (_socket != null && _socket.State != WebSocketState.Open)
			throw new ProcessorApiException ("The processor event connection is closed. Reconnect before submitting another request.");
		using var linked = CancellationTokenSource.CreateLinkedTokenSource (cancellationToken, _lifetime.Token);
		await _requests.WaitAsync (linked.Token).ConfigureAwait (false);
		try
			{
			if (_socket != null && DateTimeOffset.UtcNow - _lastValidation > TimeSpan.FromMinutes (1))
				await ValidateSessionAsync (linked.Token).ConfigureAwait (false);
			using var request = new HttpRequestMessage (method, path);
			if (payload != null)
				request.Content = new StringContent (JsonSerializer.Serialize (payload, JsonOptions), Encoding.UTF8, "application/json");
			using var response = await _http.SendAsync (request, linked.Token).ConfigureAwait (false);
			EnsureSuccess (response, method == HttpMethod.Post ? "Configuration command" : "Configuration read");
			await using var body = await response.Content.ReadAsStreamAsync (linked.Token).ConfigureAwait (false);
			using var document = await JsonDocument.ParseAsync (body, cancellationToken: linked.Token).ConfigureAwait (false);
			var root = document.RootElement;
			if (root.TryGetProperty ("Error", out var error) && error.ValueKind != JsonValueKind.Null)
				throw new ProcessorApiException ("The processor rejected the request. Response details are withheld because they may contain configuration credentials.");
			if (!root.TryGetProperty ("Result", out var result))
				throw new ProcessorApiException ("Processor returned an unexpected response envelope.");
			return result.ValueKind == JsonValueKind.Null ? default : result.Deserialize<T> (JsonOptions);
			}
		finally { _requests.Release (); }
		}

	private async Task ValidateSessionAsync (CancellationToken cancellationToken)
		{
		using var response = await _http.GetAsync ("v2/login", cancellationToken).ConfigureAwait (false);
		EnsureSuccess (response, "Session validation");
		if (response.Headers.TryGetValues ("Crestron-RestAPI-AuthKey", out var values))
			{
			_http.DefaultRequestHeaders.Remove ("Crestron-RestAPI-AuthKey");
			_http.DefaultRequestHeaders.Add ("Crestron-RestAPI-AuthKey", values.First ());
			}
		_lastValidation = DateTimeOffset.UtcNow;
		}

	private async Task ReceiveEventsAsync ()
		{
		try
			{
			while (!_lifetime.IsCancellationRequested)
				{
				using var message = await ReadMessageAsync (_socket!, _lifetime.Token).ConfigureAwait (false);
				_operations.Accept (message.RootElement);
				_swaps.Accept (message.RootElement);
				}
			}
		catch (Exception exception)
			{
			_operations.Fail (new IOException ("The processor event stream ended.", exception));
			_swaps.Fail (new IOException ("The processor event stream ended.", exception));
			}
		finally { _disconnected.TrySetResult (); }
		}

	private static async Task<JsonDocument> ReadMessageAsync (ClientWebSocket socket, CancellationToken cancellationToken)
		{
		using var bytes = new MemoryStream ();
		var buffer = new byte[16384];
		WebSocketReceiveResult result;
		do
			{
			result = await socket.ReceiveAsync (buffer, cancellationToken).ConfigureAwait (false);
			if (result.MessageType == WebSocketMessageType.Close)
				throw new IOException ("Processor closed the event connection.");
			if (result.MessageType != WebSocketMessageType.Text)
				throw new ProcessorApiException ("Unexpected event message type.");
			bytes.Write (buffer, 0, result.Count);
			if (bytes.Length > 8 * 1024 * 1024)
				throw new ProcessorApiException ("Processor event exceeded the message size limit.");
			} while (!result.EndOfMessage);
		return JsonDocument.Parse (bytes.ToArray ());
		}

	internal static string? NormalizePin (string? pin)
		{
		if (pin == null)
			return null;
		pin = pin.Replace (":", "").Replace (" ", "").ToUpperInvariant ();
		if (pin.Length != 64 || !pin.All (Uri.IsHexDigit))
			throw new ArgumentException ("Certificate pin must contain a SHA-256 fingerprint.", nameof (pin));
		return pin;
		}

	internal static bool ValidateCertificate (X509Certificate? certificate, SslPolicyErrors errors, string? pin)
		 => pin == null ? certificate != null && errors == SslPolicyErrors.None : certificate != null && certificate.GetCertHashString (HashAlgorithmName.SHA256).Equals (pin, StringComparison.OrdinalIgnoreCase);

	private static string RequiredString (JsonElement element, string property)
		 => element.TryGetProperty (property, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty (value.GetString ())
			  ? value.GetString ()! : throw new ProcessorApiException ("Authentication response did not contain the required session fields.");

	internal static void ValidatePath (string path)
		{
		ArgumentException.ThrowIfNullOrWhiteSpace (path);
		if (!path.StartsWith ("v2/", StringComparison.Ordinal) || path.Contains ("..", StringComparison.Ordinal)
			 || path.Contains ('%') || path.Contains ('\\') || path.Contains ('#') || path.Contains (':'))
			throw new ArgumentException ("Only relative version 2 API paths are accepted.", nameof (path));
		}

	private static void EnsureSuccess (HttpResponseMessage response, string operation)
		{
		if (!response.IsSuccessStatusCode)
			throw new ProcessorApiException ($"{operation} failed with HTTP {(int)response.StatusCode}. The request was not retried.", response.StatusCode);
		}

	public async ValueTask DisposeAsync ()
		{
		if (_disposed)
			return;
		_disposed = true;
		await _lifetime.CancelAsync ().ConfigureAwait (false);
		_socket?.Abort ();
		if (_eventsTask != null)
			await _eventsTask.ConfigureAwait (false);
		_operations.Fail (new ObjectDisposedException (nameof (ProcessorConnection)));
		_swaps.Fail (new ObjectDisposedException (nameof (ProcessorConnection)));
		_socket?.Dispose ();
		_http.Dispose ();
		_lifetime.Dispose ();
		}
	}