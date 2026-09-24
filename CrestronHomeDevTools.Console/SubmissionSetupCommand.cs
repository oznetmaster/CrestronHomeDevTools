// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License.
using System.Text.Json;
using CrestronHomeDevTools;

internal static class SubmissionSetupCommand
{
 internal static int Run (string[] args, TextWriter output, TextWriter error)
 {
  if (args.Length == 0 || args.SequenceEqual (["--help"]))
  {
   output.WriteLine ("""
submission-setup list --kind developer|driver|run|snapshot [--store DIRECTORY]
submission-setup check --run NAME [--purpose submission|rehearsal] [--store DIRECTORY]
submission-setup snapshot --run NAME --name SNAPSHOT [--purpose submission|rehearsal] [--store DIRECTORY]
submission-setup prepare --snapshot NAME [--store DIRECTORY]
Prepare writes review drafts and operation defaults inside the protected store, and returns their paths.
Pass the returned encrypted CredentialsPath directly to existing commands using --credentials.
No passwords or signature image bytes are printed or exported. Preparing inputs does not execute any submission stage.
""");
   return 0;
  }
  try
  {
   if (!OperatingSystem.IsWindows ()) throw new PlatformNotSupportedException ();
   var options = new Dictionary<string,string> (StringComparer.Ordinal);
   for (int i = 1; i < args.Length; i += 2)
    if (i + 1 >= args.Length || args[i] is not ("--store" or "--kind" or "--run" or "--name" or "--snapshot" or "--purpose") || !options.TryAdd (args[i], args[i+1])) throw new ArgumentException ();
   string Need (string key) => options.GetValueOrDefault (key) ?? throw new ArgumentException ();
   if (options.ContainsKey("--purpose") && args[0] is not ("check" or "snapshot")) throw new ArgumentException();
   var purpose = options.GetValueOrDefault("--purpose", "submission") switch {
    "submission" => SubmissionSetupPurpose.Submission, "rehearsal" => SubmissionSetupPurpose.Rehearsal, _ => throw new ArgumentException() };
   var store = DevToolsPrivateStore.Open (options.GetValueOrDefault ("--store"));
   switch (args[0])
   {
    case "list":
     IReadOnlyList<string> names = Need ("--kind") switch
     {
      "developer" => store.ListSetupProfiles<SubmissionDeveloperProfile> (),
      "driver" => store.ListSetupProfiles<SubmissionDriverProfile> (),
      "run" => store.ListSetupProfiles<SubmissionRunProfile> (),
      "snapshot" => store.ListSetupProfiles<SubmissionSetupSnapshot> (),
      _ => throw new ArgumentException ()
     };
     output.WriteLine (JsonSerializer.Serialize (names)); return 0;
    case "check":
     var errors = store.CheckSubmissionSetup (Need ("--run"), purpose);
     output.WriteLine (JsonSerializer.Serialize (new { InputsReady = errors.Count == 0, MissingOrInvalid = errors }));
     return errors.Count == 0 ? 0 : 2;
    case "snapshot":
     var saved = store.CreateSubmissionSetupSnapshot (Need ("--run"), Need ("--name"), purpose);
     output.WriteLine (JsonSerializer.Serialize (new { saved.Name, saved.Revision, saved.SavedUtc, Purpose = saved.Value.Purpose.ToString(), IsAuthorization = false })); return 0;
    case "prepare":
     output.WriteLine (JsonSerializer.Serialize (store.PrepareSubmissionSetupInputs (Need ("--snapshot")))); return 0;
    default: throw new ArgumentException ();
   }
  }
  catch (Exception e) when (e is ArgumentException or IOException or InvalidOperationException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or JsonException or PlatformNotSupportedException)
  {
   error.WriteLine ("Setup inputs could not be read or validated. Check the Windows account, store and profile names; no private values are included. Use submission-setup --help.");
   return 2;
  }
 }
}
