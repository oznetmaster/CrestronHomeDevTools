// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestronHomeDevTools;

public sealed record ProcessorConnectionOptions
	{
	public required string Host
		{
		get; init;
		}
	public int HttpsPort { get; init; } = 443;
	public int WebSocketPort { get; init; } = 49000;
	public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds (30);
	public string? CertificateSha256
		{
		get; init;
		}
	}

public sealed record DriverInfo
	{
	public required string Id
		{
		get; init;
		}
	public string? Model
		{
		get; init;
		}
	public string? Manufacturer
		{
		get; init;
		}
	public string? Version
		{
		get; init;
		}
	public string? Developer
		{
		get; init;
		}
	public string? PrimaryUxCategory
		{
		get; init;
		}
	public string? AvailabilityState
		{
		get; init;
		}
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? AdditionalFields
		{
		get; init;
		}
	}

public sealed record DriverUpdateEligibility
	{
	public string? InstalledDriverVersion
		{
		get; init;
		}
	public string? AvailableDriverVersion
		{
		get; init;
		}
	public bool? IsSupportsSwapDriver
		{
		get; init;
		}
	public bool? IsSwapDriverRequiresReboot
		{
		get; init;
		}
	public int[]? EligibleDeviceIds
		{
		get; init;
		}
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? AdditionalFields
		{
		get; init;
		}
	}

public sealed record DeviceInfo
	{
	public int? ParentDeviceId
		{
		get; init;
		}
	public int Id
		{
		get; init;
		}
	public string? Name
		{
		get; init;
		}
	public string? Model
		{
		get; init;
		}
	public int? LocationId
		{
		get; init;
		}
	public string[] Commands { get; init; } = [];
	// Configuration properties may contain credentials; do not log this collection.
	public Dictionary<string, JsonElement> PropertyValues { get; init; } = [];
	}

public sealed record DriverInstanceState (int DeviceId, string? Version, string? LoadingStatus);

public sealed record DriverUpdatePlan (string DriverId, DriverUpdateEligibility Eligibility);

public sealed record OperationResult (string OperationId, string Status, string? Message)
	{
	public bool Succeeded => Status == "Succeeded";
	}

public sealed class ProcessorApiException (string message, HttpStatusCode? statusCode = null) : Exception (message)
	{
	public HttpStatusCode? StatusCode { get; } = statusCode;
	}

public interface IConfigurationConnection : IAsyncDisposable
	{
	Task<DriverSwapResult> WaitForDriverSwapAsync (string operationId, string driverId, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException ("This connection cannot confirm driver swap completion.");
	Task WaitForDisconnectAsync (CancellationToken cancellationToken = default) => throw new NotSupportedException ("This connection cannot observe processor restarts.");
	Task<T?> GetAsync<T> (string relativePath, CancellationToken cancellationToken = default);
	Task<T?> ExecuteAsync<T> (int deviceId, string commandName, object? parameters = null, CancellationToken cancellationToken = default);
	Task<OperationResult> WaitForOperationAsync (string operationId, TimeSpan timeout, CancellationToken cancellationToken = default);
	}