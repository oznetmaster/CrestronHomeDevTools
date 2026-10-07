// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License.
namespace CrestronHomeDevTools;

/// <summary>One registered generation. Closed means its owner has verified that work and cleanup have ended.
/// Dependency keys identify original generations still needed to interpret this generation's evidence.</summary>
public sealed record SubmissionRetentionGeneration(string Key, string Repository, long Sequence,
 bool Closed, bool Submitted, bool Pinned, IReadOnlyList<string> Dependencies);
public sealed record SubmissionRetentionPlan(string Current, string? Previous,
 IReadOnlyList<string> Keep, IReadOnlyList<string> Remove);

/// <summary>Selects current plus one previous routine generation per driver, preserving active work,
/// submitted records and their transitive evidence dependencies. Selection alone deletes no files.</summary>
public static class SubmissionRunRetention
{
 public static SubmissionRetentionPlan Plan(string current, IReadOnlyList<SubmissionRetentionGeneration> generations)
 {
  ArgumentNullException.ThrowIfNull(generations);
  var byKey=new Dictionary<string,SubmissionRetentionGeneration>(StringComparer.Ordinal);
  foreach(var item in generations) {
   if(item==null || !ValidKey(item.Key) || item.Sequence<1 || !ValidRepository(item.Repository) ||
    item.Dependencies==null || item.Dependencies.Any(key=>!ValidKey(key)) ||
    item.Dependencies.Distinct(StringComparer.Ordinal).Count()!=item.Dependencies.Count || !byKey.TryAdd(item.Key,item))
    throw new ArgumentException("Retention requires unique registered generations and valid dependency identities.");
  }
  if(!ValidKey(current) || !byKey.TryGetValue(current,out var selected))
   throw new ArgumentException("The current generation must be registered.");
  if(generations.Any(item=>item.Dependencies.Any(key=>!byKey.ContainsKey(key))))
   throw new InvalidDataException("A retained generation has an unresolved evidence dependency.");
  var driver=generations.Where(item=>string.Equals(item.Repository,selected.Repository,StringComparison.OrdinalIgnoreCase)).ToArray();
  if(driver.Select(item=>item.Sequence).Distinct().Count()!=driver.Length || driver.Any(item=>item.Sequence>selected.Sequence))
   throw new InvalidDataException("Rotate only when opening the newest registered generation for this driver; resume does not rotate.");
  // Submitted/pinned generations have their own lifetime and do not consume the routine archive slot.
  var previous=driver.Where(item=>item.Key!=current && item.Closed && !item.Submitted && !item.Pinned)
   .OrderByDescending(item=>item.Sequence).FirstOrDefault();
  var keep=new HashSet<string>(StringComparer.Ordinal){current};
  foreach(var item in generations)
   if(!string.Equals(item.Repository,selected.Repository,StringComparison.OrdinalIgnoreCase) ||
    !item.Closed || item.Submitted || item.Pinned)keep.Add(item.Key);
  if(previous!=null)keep.Add(previous.Key);
  var pending=new Stack<string>(keep);
  while(pending.TryPop(out var key))
   foreach(var dependency in byKey[key].Dependencies)if(keep.Add(dependency))pending.Push(dependency);
  var order=generations.OrderBy(item=>item.Repository,StringComparer.OrdinalIgnoreCase).ThenBy(item=>item.Sequence).ThenBy(item=>item.Key,StringComparer.Ordinal).ToArray();
  return new(current,previous?.Key,order.Where(item=>keep.Contains(item.Key)).Select(item=>item.Key).ToArray(),
   order.Where(item=>!keep.Contains(item.Key)).Select(item=>item.Key).ToArray());
 }
 private static bool ValidKey(string? key)=>key is {Length:64} && key.All(c=>char.IsAsciiDigit(c)||c is >= 'a' and <= 'f');
 private static bool ValidRepository(string? repository) {
  var parts=repository?.Split('/')??[];
  return parts.Length==2 && parts.All(part=>part.Length>0 && part!="." && part!=".." &&
   part.All(c=>char.IsAsciiLetterOrDigit(c)||c is '-' or '_' or '.'));
 }
}
