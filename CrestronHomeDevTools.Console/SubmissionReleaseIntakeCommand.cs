// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License.
using System.Text.Json;
using System.Text.Json.Serialization;
using CrestronHomeDevTools;

internal static class SubmissionReleaseIntakeCommand
{
 private static readonly JsonSerializerOptions Json = new() {
  PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
  AllowDuplicateProperties = false, RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true,
  Converters = { new JsonStringEnumConverter(allowIntegerValues: false) } };
 internal static async Task<int> RunAsync(string[] args, TextReader input, TextWriter output, TextWriter error,
  CancellationToken token, HttpClient? httpClient = null)
 {
  if (args.SequenceEqual(["--help"]))
  {
   output.WriteLine("""
submission-release-intake --settings PRIVATE_JSON [--token-stdin true]
Retain and verify a published GitHub package, then open or resume its workflow checkpoint.
Optional GitHub token: protected standard input only, plain text (never an argument or output).
Exit 0: candidate retained; 4: awaiting package; 5: release not selected; 2: inspect inputs/failure.
Source preview: this command does not yet start tests or automatically submit a driver.
"""); return 0;
  }
  try
  {
   var options = new Dictionary<string,string>(StringComparer.Ordinal);
   for (int i=0; i<args.Length; i+=2)
    if (i+1>=args.Length || args[i] is not ("--settings" or "--token-stdin") || !options.TryAdd(args[i],args[i+1]))
     throw new ArgumentException();
   if (!options.TryGetValue("--settings",out var path) || !Path.IsPathFullyQualified(path) ||
    new FileInfo(path).Length > 1024*1024 || options.TryGetValue("--token-stdin",out var mode) && mode != "true")
    throw new ArgumentException();
   var settings = JsonSerializer.Deserialize<SubmissionReleaseIntakeSettings>(File.ReadAllBytes(path),Json)
    ?? throw new InvalidDataException();
   using var owned = httpClient == null ? new HttpClient() : null;
   var client = httpClient ?? owned!;
   if (options.ContainsKey("--token-stdin"))
   {
    char[] buffer = new char[65537]; int total=0, read;
    while (total<buffer.Length && (read=await input.ReadAsync(buffer.AsMemory(total),token))!=0) total+=read;
    if (total>65536) throw new ArgumentException();
    string secret = new string(buffer,0,total).Trim(); Array.Clear(buffer);
    if (secret.Length==0 || secret.Any(char.IsControl)) throw new ArgumentException();
    client.DefaultRequestHeaders.Authorization = new("Bearer",secret);
   }
   var result = await SubmissionReleaseIntake.PrepareAsync(settings,new GitHubSubmissionRelease(client),token);
   output.WriteLine(JsonSerializer.Serialize(result,Json));
   return result.Availability switch { SubmissionReleaseAvailability.Ready=>0, SubmissionReleaseAvailability.AwaitingPackage=>4, _=>5 };
  }
  catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or JsonException or HttpRequestException or OperationCanceledException or InvalidOperationException or KeyNotFoundException)
  {
   error.WriteLine("Release intake could not finish. Verify the private settings, frozen input digests, GitHub access and existing run. No tests or delivery were started; existing evidence was not reset.");
   return 2;
  }
 }
}
