// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License.
using System.Text.Json;
using CrestronHomeDevTools;

internal static class SubmissionOperatorCommand
{
 internal static int Run(string[] args,TextWriter output,TextWriter error)
 {
  if(args.Length==0 || args.SequenceEqual(["--help"])) {
   output.WriteLine("""
submission-operator show --request-directory DIRECTORY --request-sha256 SHA256
submission-operator respond --request-directory DIRECTORY --request-sha256 SHA256 --outcome done|unable [--reason TEXT]
Use the exact directory and hash supplied by the waiting fixture. Read its target and instructions before responding.
Done acknowledges the requested action only; the fixture must independently verify the result.
This does not approve signing, upload or email. Expired requests cannot be completed.
Readiness requests do not expire. Done means ready, not that the physical action occurred. Unable requires a reason for readiness.
""");return 0;
  }
  try {
   if(args[0] is not ("show" or "respond"))throw new ArgumentException();
   var options=new Dictionary<string,string>(StringComparer.Ordinal);
   for(int i=1;i<args.Length;i+=2)
    if(i+1>=args.Length || args[i] is not ("--request-directory" or "--request-sha256" or "--outcome" or "--reason") ||
     !options.TryAdd(args[i],args[i+1]))throw new ArgumentException();
   string Need(string name)=>options.GetValueOrDefault(name)??throw new ArgumentException();
   var handle=new SubmissionOperatorHandle(Need("--request-directory"),Need("--request-sha256"));
   if(args[0]=="show") {
    if(options.ContainsKey("--outcome") || options.ContainsKey("--reason"))throw new ArgumentException();
    var status=SubmissionOperatorStep.Read(handle);
    output.WriteLine(JsonSerializer.Serialize(new { status.Request,status.Response,
     IsReadiness=status.Request.IsReadiness,CanRespond=status.Waiting && !status.Request.IsExpired(DateTimeOffset.UtcNow) && SubmissionOperatorStep.IsRecorderAvailable(handle) }));return 0;
   }
   var outcome=Need("--outcome") switch {"done"=>SubmissionOperatorOutcome.Done,"unable"=>SubmissionOperatorOutcome.Unable,_=>throw new ArgumentException()};
   var response=SubmissionOperatorStep.Respond(handle,outcome,options.GetValueOrDefault("--reason"));
   output.WriteLine(JsonSerializer.Serialize(response));
   return response.Outcome==SubmissionOperatorOutcome.Expired?3:0;
  } catch(Exception e) when(e is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException or JsonException) {
   error.WriteLine("Operator request could not be validated or answered. Check its path, hash, expiry and retained response. Use submission-operator --help.");return 2;
  }
 }
}
