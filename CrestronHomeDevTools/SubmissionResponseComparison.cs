// Copyright (c) 2026 Neil Colvin. MIT licensed.
namespace CrestronHomeDevTools;

public sealed record SubmissionResponseMeasurement(string Id, DateTimeOffset InputUtc,
    DateTimeOffset ObservedUtc, double ElapsedMilliseconds);
public sealed record SubmissionResponseSeries(int SchemaVersion, SubmissionEvidenceIdentity Identity,
    string DeviceIdentity, string Method, SubmissionResponseMeasurement[] Measurements);
/// <summary>Explicit developer-reviewed limits, not thresholds prescribed by Crestron.</summary>
public sealed record SubmissionResponseLimits(double MaximumAfterMilliseconds,
    double MaximumIncreaseMilliseconds, double MaximumRatio, string Rationale);
public sealed record SubmissionResponseDifference(string Id, double BeforeMilliseconds,
    double AfterMilliseconds, double IncreaseMilliseconds, double Ratio, bool? WithinReviewedLimits);
public sealed record SubmissionResponseComparisonReport(SubmissionEvidenceIdentity Identity,
    string DeviceIdentity, string Method, SubmissionResponseLimits? Limits,
    SubmissionResponseDifference[] Differences)
{
    public bool? WithinReviewedLimits => Limits == null ? null : Differences.All(d => d.WithinReviewedLimits == true);
}

/// <summary>Compares matching observations; absent acceptance criteria never imply unchanged performance.</summary>
public static class SubmissionResponseComparison
{
    public static SubmissionResponseComparisonReport Compare(SubmissionResponseSeries before,
        SubmissionResponseSeries after, SubmissionEvidenceIdentity identity,
        DateTimeOffset enduranceStartedUtc, DateTimeOffset enduranceFinishedUtc,
        SubmissionResponseLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(before); ArgumentNullException.ThrowIfNull(after);
        if (enduranceStartedUtc == default || enduranceFinishedUtc <= enduranceStartedUtc)
            throw new InvalidDataException("A completed endurance interval is required.");
        ValidateLimits(limits);
        foreach (var series in new[] { before, after })
        {
            if (series.SchemaVersion != 1 || series.Identity != identity ||
                string.IsNullOrWhiteSpace(series.DeviceIdentity) || string.IsNullOrWhiteSpace(series.Method) ||
                series.Measurements is not { Length: > 0 and <= 256 } ||
                series.Measurements.Any(m => m == null || string.IsNullOrWhiteSpace(m.Id) || m.InputUtc == default ||
                    m.ObservedUtc < m.InputUtc || !double.IsFinite(m.ElapsedMilliseconds) || m.ElapsedMilliseconds <= 0) ||
                series.Measurements.Select(m => m.Id).Distinct(StringComparer.Ordinal).Count() != series.Measurements.Length)
                throw new InvalidDataException("Measurements require matching candidate identity, distinct controls, finite durations and ordered timestamps.");
        }
        if (before.DeviceIdentity != after.DeviceIdentity || before.Method != after.Method ||
            !before.Measurements.Select(m => m.Id).Order(StringComparer.Ordinal).SequenceEqual(after.Measurements.Select(m => m.Id).Order(StringComparer.Ordinal)) ||
            before.Measurements.Any(m => m.ObservedUtc > enduranceStartedUtc) ||
            after.Measurements.Any(m => m.InputUtc < enduranceFinishedUtc))
            throw new InvalidDataException("Compare the same device, method and requested controls, with observations on opposite sides of the completed interval.");
        var differences = before.Measurements.OrderBy(m => m.Id, StringComparer.Ordinal).Select(first =>
        {
            var last = after.Measurements.Single(m => m.Id == first.Id);
            double increase = last.ElapsedMilliseconds - first.ElapsedMilliseconds;
            double ratio = last.ElapsedMilliseconds / first.ElapsedMilliseconds;
            if (!double.IsFinite(ratio)) throw new InvalidDataException("Measurement ratio overflowed.");
            bool? within = limits == null ? null : last.ElapsedMilliseconds <= limits.MaximumAfterMilliseconds &&
                increase <= limits.MaximumIncreaseMilliseconds && ratio <= limits.MaximumRatio;
            return new SubmissionResponseDifference(first.Id, first.ElapsedMilliseconds, last.ElapsedMilliseconds, increase, ratio, within);
        }).ToArray();
        return new(identity, before.DeviceIdentity, before.Method, limits, differences);
    }

    public static void ValidateLimits(SubmissionResponseLimits? limits)
    {
        if (limits != null && (!double.IsFinite(limits.MaximumAfterMilliseconds) || limits.MaximumAfterMilliseconds <= 0 ||
            !double.IsFinite(limits.MaximumIncreaseMilliseconds) || limits.MaximumIncreaseMilliseconds < 0 ||
            !double.IsFinite(limits.MaximumRatio) || limits.MaximumRatio < 1 || string.IsNullOrWhiteSpace(limits.Rationale)))
            throw new InvalidDataException("Specify finite positive response limits, a ratio of at least one and their review rationale.");
    }
}
