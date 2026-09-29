// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Globalization;

namespace CrestronHomeDevTools;

/// <summary>Human-facing timestamps only; evidence retains its original UTC precision.</summary>
public static class SubmissionDisplayTime
{
 public static string Utc(DateTimeOffset value) =>
  value.ToUniversalTime().ToString("dd MMM yyyy, HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);

 public static string Local(DateTimeOffset value) => InTimeZone(value, TimeZoneInfo.Local);

 public static string InTimeZone(DateTimeOffset value, TimeZoneInfo zone) {
  ArgumentNullException.ThrowIfNull(zone);
  var local=TimeZoneInfo.ConvertTime(value,zone);
  return local.ToString("dd MMM yyyy, HH:mm:ss '(UTC'zzz')'", CultureInfo.InvariantCulture);
 }
}
