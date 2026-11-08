// Copyright (c) 2026 Neil Colvin. MIT licensed.
namespace CrestronHomeDevTools;

/// <summary>Read-only post-update convergence. This never updates a driver, changes settings,
/// issues controls or proves a measured outage-recovery deadline.</summary>
public sealed record DriverReadinessWaitResult(bool Ready, bool TimedOut, DriverReadinessReport? LastObservation);

public static class DriverReadinessWait
{
    /// <summary>Retain every observation before evaluating it. Use a fresh client per observation
    /// after a human wait or processor interruption. Exceptions are not silently retried.</summary>
    public static async Task<DriverReadinessWaitResult> WaitAsync(
        Func<CancellationToken, Task<DriverReadinessReport>> inspect,
        Action<DriverReadinessReport> retain, TimeSpan timeout, TimeSpan interval,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inspect);
        ArgumentNullException.ThrowIfNull(retain);
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(10)) throw new ArgumentOutOfRangeException(nameof(timeout));
        if (interval <= TimeSpan.Zero || interval > timeout) throw new ArgumentOutOfRangeException(nameof(interval));
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(timeout);
        DriverReadinessReport? last = null;
        try
        {
            while (true)
            {
                budget.Token.ThrowIfCancellationRequested();
                last = await inspect(budget.Token).ConfigureAwait(false);
                retain(last); // Capture initial Offline/NotConfigured observations before any assertion.
                budget.Token.ThrowIfCancellationRequested();
                if (last.Ready) return new(true, false, last);
                // A different installed identity is not startup convergence.
                if (last.Issues.Any(i => i.Reason is "root-model-changed" or "root-missing" or
                    "driver-version-changed-or-unconfirmed")) return new(false, false, last);
                await Task.Delay(interval, budget.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return new(false, true, last);
        }
    }
}
