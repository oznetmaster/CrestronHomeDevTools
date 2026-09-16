// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Net;
using System.Net.WebSockets;

namespace CrestronHomeDevTools;

/// <summary>How an explicitly authorized lifecycle operation restarts the processor.</summary>
public enum DriverRebootMode
	{
	ProcessorManaged, ExplicitAfterOperation
	}

public sealed record DriverRebootRequest (string Operation, int? DeviceId, string Model, string Version, DriverRebootMode Mode)
	{
	public DriverSwapResult? SwapCompletion
		{
		get; init;
		}
	}

/// <summary>Caller authorization, durable evidence, and fresh authenticated connection after restart.</summary>
public sealed record DriverRebootHandler (
	 Func<DriverRebootRequest, CancellationToken, Task> BeforeSubmitAsync,
	 Func<DriverRebootRequest, ConfigurationClient, CancellationToken, Task<ConfigurationClient>> RecoverAsync)
	{
	// Initial installation/removal reboot policy is separate from the advertised update requirement.
	// Enable these only for a known driver/firmware combination; neither is inferred from a timeout.
	public bool RebootAfterInstall
		{
		get; init;
		}
	public bool RebootAfterRemoval
		{
		get; init;
		}
	// Explicitly reviewed existing instances that share this V1 driver's reload scope.
	// These instances are preserved, never selected for removal.
	public int[] AdditionalRemovalRebootDeviceIds { get; init; } = [];
	}

public static class ProcessorRestartRecovery
	{
	/// <summary>Wait for the old event connection to end, then authenticate a new connection. Never submits a mutation.</summary>
	public static async Task<ConfigurationClient> WaitAsync (ConfigurationClient previous,
		 Func<CancellationToken, Task<ConfigurationClient>> connect, TimeSpan timeout, CancellationToken cancellationToken = default)
		{
		ArgumentNullException.ThrowIfNull (previous);
		ArgumentNullException.ThrowIfNull (connect);
		if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromHours (1))
			throw new ArgumentOutOfRangeException (nameof (timeout));
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource (cancellationToken);
		deadline.CancelAfter (timeout);
		try
			{
			await previous.WaitForDisconnectAsync (deadline.Token).ConfigureAwait (false);
			await previous.DisposeAsync ().ConfigureAwait (false);
			while (true)
				{
				deadline.Token.ThrowIfCancellationRequested ();
				ConfigurationClient? current = null;
				var currentReady = false;
				try
					{
					current = await connect (deadline.Token).ConfigureAwait (false);
					await current.GetDevicesAsync (deadline.Token).ConfigureAwait (false);
					currentReady = true;
					return current;
					}
				catch (Exception exception) when (IsReadinessFailure (exception) && !deadline.IsCancellationRequested)
					{
					// Read-only reconnects may be retried; commands and test executions are never replayed.
					}
				finally
					{
					// Successful return transfers ownership; failure paths dispose below via the flag.
					if (current != null && !currentReady)
						await current.DisposeAsync ().ConfigureAwait (false);
					}
				await Task.Delay (1000, deadline.Token).ConfigureAwait (false);
				}
			}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
			throw new TimeoutException ("Processor restart/reconnection was not confirmed before the deadline. No command was retried.");
			}
		}

	// Only the bounded restart readiness loop treats transient server read failures as startup.
	// Authentication errors and command rejections remain fatal; no mutations are retried.
	internal static bool IsReadinessFailure (Exception exception) => IsTransportFailure (exception)
		 || exception is ProcessorApiException { StatusCode: HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout };

	internal static bool IsTransportFailure (Exception exception) => exception is IOException or HttpRequestException or WebSocketException or TimeoutException or OperationCanceledException;
	}