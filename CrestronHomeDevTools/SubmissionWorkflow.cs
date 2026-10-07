// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License.
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestronHomeDevTools;

/// <summary>Identity verified by the trusted release intake. Contains no secrets and grants no operation authority.</summary>
public sealed record SubmissionWorkflowRelease(string Repository, long ReleaseId, string Tag,
 string SourceCommit, string PackageSha256, string ProfileSnapshotSha256, string ToolingSha256);

public enum SubmissionWorkflowStage { ValidateCandidate, WindowsTests, ProcessorTests, AppTests, Endurance, PrepareReview, SignReview, Deliver, Retain, FinalizeTests }
public enum SubmissionWorkflowStatus { Ready, Running, Waiting, NeedsInput, Failed, OutcomeUnknown, Completed }
public sealed record SubmissionWorkflowReceipt(string RelativePath, string Sha256);
public sealed record SubmissionWorkflowStepResult(SubmissionWorkflowStatus Status, SubmissionWorkflowReceipt? Receipt = null, string? ReasonCode = null);
public sealed record SubmissionWorkflowCheckpoint(int SchemaVersion, string InputSha256, SubmissionWorkflowRelease Release,
 SubmissionWorkflowStage Stage, SubmissionWorkflowStatus Status, string? OperationId, string? ReasonCode,
 Dictionary<SubmissionWorkflowStage, SubmissionWorkflowReceipt> CompletedStages, DateTimeOffset UpdatedUtc);
public sealed record SubmissionWorkflowStepContext(string RunDirectory, SubmissionWorkflowCheckpoint Checkpoint);

/// <summary>Trusted adapters must validate real receipts, equipment scope and exact operation authorizations.
/// Recovery must inspect the existing operation journal; it must not blindly repeat a mutation or send.</summary>
public interface ISubmissionWorkflowSteps
{
 Task<SubmissionWorkflowStepResult> ExecuteAsync(SubmissionWorkflowStepContext context, CancellationToken cancellationToken);
 Task<SubmissionWorkflowStepResult> RecoverAsync(SubmissionWorkflowStepContext context, CancellationToken cancellationToken);
}

/// <summary>Persistent stage sequencing. This controller grants no authority, interprets no test results,
/// and does not turn declared gaps into passes. Stage adapters perform those domain-specific checks.</summary>
public static class SubmissionWorkflow
{
 // Enum values remain stable; sequencing is explicit so new phases do not renumber old receipts.
 internal static readonly SubmissionWorkflowStage[] CurrentStages=[SubmissionWorkflowStage.ValidateCandidate,
  SubmissionWorkflowStage.WindowsTests,SubmissionWorkflowStage.ProcessorTests,SubmissionWorkflowStage.AppTests,
  SubmissionWorkflowStage.Endurance,SubmissionWorkflowStage.FinalizeTests,SubmissionWorkflowStage.PrepareReview,
  SubmissionWorkflowStage.SignReview,SubmissionWorkflowStage.Deliver,SubmissionWorkflowStage.Retain];
 private static SubmissionWorkflowStage[] Stages(int schema)=>schema switch {
  1=>CurrentStages.Where(s=>s!=SubmissionWorkflowStage.FinalizeTests).ToArray(),
  2=>CurrentStages,
  _=>throw new InvalidDataException("Unsupported workflow checkpoint version.")};
 private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
  UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, AllowDuplicateProperties = false,
  RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true,
  Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }, WriteIndented = true };

 /// <summary>One directory per repository/release ID. Duplicate events reuse it; changed assets/profile/tooling are rejected.</summary>
 public static string RunKey(SubmissionWorkflowRelease release)
 {
  Validate(release);
  return Hash(JsonSerializer.SerializeToUtf8Bytes(new { repository = release.Repository.ToLowerInvariant(), release.ReleaseId }, Json));
 }

 public static SubmissionWorkflowCheckpoint Open(string privateRoot, SubmissionWorkflowRelease release)
 {
  string directory = DirectoryFor(privateRoot, release);
  using var archives=SubmissionRunArchive.Acquire(privateRoot);
  SubmissionRunArchive.RejectPruned(privateRoot,release);
  archives.RecoverPruning();
  Directory.CreateDirectory(directory);
  using var gate = Gate(directory);
  string digest = InputDigest(release);
  if (File.Exists(Path.Combine(directory, "state.json")))
  {
   var existing = Load(directory);
   if (existing.InputSha256 != digest) throw new InvalidDataException("The release, package or frozen inputs changed. Review the existing run instead of restarting it.");
   return existing;
  }
  archives.RejectMissingCheckpoint(release);
  var state = new SubmissionWorkflowCheckpoint(2, digest, release, SubmissionWorkflowStage.ValidateCandidate,
   SubmissionWorkflowStatus.Ready, null, null, new(), DateTimeOffset.UtcNow);
  Save(directory, state);
  archives.RegisterNew(release);
  archives.Rotate(release);
  return state;
 }

 public static SubmissionWorkflowCheckpoint Read(string privateRoot, SubmissionWorkflowRelease release)
  => WithVerifiedCheckpoint(privateRoot, release, context => context.Checkpoint);

 // Keep maintenance verification and writes under the same gate without reacquiring it.
 internal static T WithVerifiedCheckpoint<T>(string privateRoot, SubmissionWorkflowRelease release,
  Func<SubmissionWorkflowStepContext,T> action)
 {
  ArgumentNullException.ThrowIfNull(action);
  string directory=DirectoryFor(privateRoot,release);
  using var gate=Gate(directory);
  var state=Load(directory);
  MatchInput(state,release);
  VerifyReceipts(directory,state);
  return action(new(directory,state));
 }

 /// <summary>Run ready stages until waiting, attention or completion. Long operations must return Waiting promptly.
 /// Restarting a Running step calls RecoverAsync with the original operation ID, never ExecuteAsync again.</summary>
 public static Task<SubmissionWorkflowCheckpoint> AdvanceAsync(string privateRoot, SubmissionWorkflowRelease release,
  ISubmissionWorkflowSteps steps, CancellationToken cancellationToken = default)
  =>AdvanceCoreAsync(privateRoot,release,steps,null,false,cancellationToken);

 /// <summary>Execute or recover only the selected stage. Completed stages are verified and reused.
 /// Missing prerequisites are rejected; selecting a test never runs other stages or delivery.</summary>
 public static Task<SubmissionWorkflowCheckpoint> AdvanceStageAsync(string privateRoot,SubmissionWorkflowRelease release,
  ISubmissionWorkflowSteps steps,SubmissionWorkflowStage stage,CancellationToken cancellationToken=default)
 {
  if(!Enum.IsDefined(stage))throw new ArgumentOutOfRangeException(nameof(stage));
  return AdvanceCoreAsync(privateRoot,release,steps,stage,false,cancellationToken);
 }
 /// <summary>Advance only document preparation, signing, delivery and retention after all test stages completed.
 /// Missing or changed test receipts are rejected before checkpoint mutation or adapter execution.
 /// Domain adapters still verify the actual test evidence and exact signing/delivery authority.</summary>
 public static Task<SubmissionWorkflowCheckpoint> AdvancePhaseThreeAsync(string privateRoot,SubmissionWorkflowRelease release,
  ISubmissionWorkflowSteps steps,CancellationToken cancellationToken=default)
  =>AdvanceCoreAsync(privateRoot,release,steps,null,true,cancellationToken);

 private static async Task<SubmissionWorkflowCheckpoint> AdvanceCoreAsync(string privateRoot,SubmissionWorkflowRelease release,
  ISubmissionWorkflowSteps steps,SubmissionWorkflowStage? selected,bool phaseThreeOnly,CancellationToken cancellationToken)
 {
  ArgumentNullException.ThrowIfNull(steps);
  string directory = DirectoryFor(privateRoot, release);
  using var gate = Gate(directory);
  SubmissionRunArchive.RejectClosed(directory);
  var state = Load(directory);
  MatchInput(state, release);
  VerifyReceipts(directory, state);
  if(state.SchemaVersion!=2)throw new InvalidDataException("This retained run uses the earlier test/review boundary. Keep its pinned worker; do not replay or silently migrate it.");
  if(phaseThreeOnly && (CurrentStages.TakeWhile(s=>s!=SubmissionWorkflowStage.PrepareReview)
    .Any(s=>!state.CompletedStages.ContainsKey(s)) ||
    state.Stage is not (SubmissionWorkflowStage.PrepareReview or SubmissionWorkflowStage.SignReview or SubmissionWorkflowStage.Deliver or SubmissionWorkflowStage.Retain)))
   throw new InvalidOperationException("Phase three requires completed test stages, including final test assessment. Run the defined test suite first.");
  if(selected is {} requested) {
   if(state.CompletedStages.ContainsKey(requested))return state;
   if(state.Stage!=requested)throw new InvalidOperationException("Complete the preceding stages before selecting this stage.");
  }
  while (state.Status is SubmissionWorkflowStatus.Ready or SubmissionWorkflowStatus.Running or SubmissionWorkflowStatus.Waiting)
  {
   cancellationToken.ThrowIfCancellationRequested();
   bool recover = state.Status != SubmissionWorkflowStatus.Ready;
   if (!recover)
   {
    state = state with { Status = SubmissionWorkflowStatus.Running, OperationId = Guid.NewGuid().ToString("N"), ReasonCode = null, UpdatedUtc = DateTimeOffset.UtcNow };
    Save(directory, state); // Durable intent precedes any adapter side effect.
   }
   SubmissionWorkflowStepResult result;
   try
   {
    var context = new SubmissionWorkflowStepContext(directory, state);
    result = recover ? await steps.RecoverAsync(context, cancellationToken).ConfigureAwait(false)
     : await steps.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
   }
   catch
   {
    // The durable Running state is intentionally retained. The next invocation must reconcile it.
    throw;
   }
   if (result.Status is SubmissionWorkflowStatus.Ready or SubmissionWorkflowStatus.Running || !Enum.IsDefined(result.Status))
    throw new InvalidDataException("A step must report completion, waiting or an explicit attention state.");
   if (result.Status == SubmissionWorkflowStatus.Completed)
   {
    if (result.Receipt == null || result.ReasonCode != null) throw new InvalidDataException("Completed steps require a retained receipt.");
    VerifyReceipt(directory, result.Receipt);
    var completed = new Dictionary<SubmissionWorkflowStage, SubmissionWorkflowReceipt>(state.CompletedStages) { [state.Stage] = result.Receipt };
    bool last = state.Stage == SubmissionWorkflowStage.Retain;
    state = state with { CompletedStages = completed, Stage = last ? state.Stage : CurrentStages[Array.IndexOf(CurrentStages,state.Stage)+1],
     Status = last ? SubmissionWorkflowStatus.Completed : SubmissionWorkflowStatus.Ready,
     OperationId = null, ReasonCode = null, UpdatedUtc = DateTimeOffset.UtcNow };
   }
   else
   {
    if (result.Receipt != null || string.IsNullOrWhiteSpace(result.ReasonCode) || result.ReasonCode.Length > 128 ||
     result.ReasonCode.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_')))
     throw new InvalidDataException("Waiting or attention requires a short non-secret reason code, not a completion receipt.");
    if (state.Status == result.Status && state.ReasonCode == result.ReasonCode) return state; // No growing history for identical healthy ticks.
    state = state with { Status = result.Status, ReasonCode = result.ReasonCode, UpdatedUtc = DateTimeOffset.UtcNow };
   }
   Save(directory, state);
   if (selected.HasValue || state.Status != SubmissionWorkflowStatus.Ready) return state;
  }
  return state;
 }

 /// <summary>After separately resolving the reported condition, request recovery of the same operation.
 /// The caller must pin the state it reviewed. This does not approve a signature, retry a provider, or mark a step passed.</summary>
 public static SubmissionWorkflowCheckpoint RequestRecovery(string privateRoot, SubmissionWorkflowRelease release, string expectedStateSha256)
 {
  string directory = DirectoryFor(privateRoot, release);
  using var gate = Gate(directory);
  SubmissionRunArchive.RejectClosed(directory);
  var state = Load(directory);
  MatchInput(state, release);
  if (Hash(File.ReadAllBytes(Path.Combine(directory, "state.json"))) != expectedStateSha256 ||
   state.Status is not (SubmissionWorkflowStatus.NeedsInput or SubmissionWorkflowStatus.Failed or SubmissionWorkflowStatus.OutcomeUnknown))
   throw new InvalidDataException("Review the exact attention state before requesting recovery.");
  state = state with { Status = SubmissionWorkflowStatus.Waiting, ReasonCode = "recovery-requested", UpdatedUtc = DateTimeOffset.UtcNow };
  Save(directory, state);
  return state;
 }

 /// <summary>Explicitly move an uncompleted legacy review operation to the final-test boundary.
 /// A trusted adapter must verify quiescence and that document/sign/delivery work has not begun.
 /// Preserves the original checkpoint bytes, receipts, operation, status and failure reason;
 /// grants no retry or delivery authority and never executes equipment callbacks.</summary>
 public static SubmissionWorkflowCheckpoint MigrateTestBoundary(string privateRoot, SubmissionWorkflowRelease release,
  string expectedStateSha256, Action<SubmissionWorkflowStepContext> verifyBoundary)
 {
  ArgumentNullException.ThrowIfNull(verifyBoundary);
  if(expectedStateSha256 is not {Length:64} || expectedStateSha256.Any(c=>!(char.IsAsciiDigit(c)||c is >= 'a' and <= 'f')))
   throw new ArgumentException("Pin the exact legacy checkpoint by lowercase SHA256.",nameof(expectedStateSha256));
  string directory=DirectoryFor(privateRoot,release);
  using var gate=Gate(directory);
  SubmissionRunArchive.RejectClosed(directory);
  var state=Load(directory);MatchInput(state,release);VerifyReceipts(directory,state);
  string originalPath=Path.Combine(directory,"legacy-boundary-"+expectedStateSha256+".json");
  string statePath=Path.Combine(directory,"state.json");
  byte[] original;
  if(state.SchemaVersion==1) {
   original=File.ReadAllBytes(statePath);
   if(Hash(original)!=expectedStateSha256 || state.Stage!=SubmissionWorkflowStage.PrepareReview ||
    state.Status is not (SubmissionWorkflowStatus.Ready or SubmissionWorkflowStatus.Failed or SubmissionWorkflowStatus.NeedsInput or SubmissionWorkflowStatus.OutcomeUnknown))
    throw new InvalidDataException("Migration requires the exact stopped legacy test/review boundary.");
  } else {
   // A lost acknowledgement may be reconciled only at the exact resulting boundary.
   if(!File.Exists(originalPath))throw new InvalidDataException("No matching legacy boundary migration exists.");
   VerifyReceipt(directory,new(Path.GetFileName(originalPath),expectedStateSha256));
   original=File.ReadAllBytes(originalPath);
   var prior=JsonSerializer.Deserialize<SubmissionWorkflowCheckpoint>(original,Json)!;
   var expected=prior with {SchemaVersion=2,Stage=SubmissionWorkflowStage.FinalizeTests,UpdatedUtc=state.UpdatedUtc};
   if(prior.SchemaVersion!=1 || prior.Stage!=SubmissionWorkflowStage.PrepareReview ||
    !JsonSerializer.SerializeToUtf8Bytes(state,Json).AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(expected,Json)))
    throw new InvalidDataException("The run has advanced or changed since boundary migration.");
  }
  verifyBoundary(new(directory,state)); // Under the gate, before any checkpoint or archive mutation.
  if(state.SchemaVersion==2)return state;
  if(File.Exists(originalPath))VerifyReceipt(directory,new(Path.GetFileName(originalPath),expectedStateSha256));
  else {
   string temporary=Path.Combine(directory,"legacy-boundary-"+Guid.NewGuid().ToString("N")+".tmp");
   try {
    using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(original);stream.Flush(true);}
    SubmissionJournalFile.Replace(temporary,originalPath,overwrite:false);
   } finally {if(File.Exists(temporary))File.Delete(temporary);}
  }
  var migrated=state with {SchemaVersion=2,Stage=SubmissionWorkflowStage.FinalizeTests,UpdatedUtc=DateTimeOffset.UtcNow};
  Save(directory,migrated);
  return migrated;
 }

 private static string DirectoryFor(string root, SubmissionWorkflowRelease release)
 {
  if (!Path.IsPathFullyQualified(root) || !Directory.Exists(root)) throw new ArgumentException("Supply an existing protected absolute workflow root.");
  return Path.Combine(root, RunKey(release));
 }
 private static FileStream Gate(string directory)
 {
  if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Run directories cannot be links.");
  return new FileStream(Path.Combine(directory, "run.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
 }
 private static void MatchInput(SubmissionWorkflowCheckpoint state, SubmissionWorkflowRelease release)
 {
  if (state.InputSha256 != InputDigest(release)) throw new InvalidDataException("Frozen release inputs changed.");
 }
 private static string InputDigest(SubmissionWorkflowRelease release) { Validate(release); return Hash(JsonSerializer.SerializeToUtf8Bytes(release, Json)); }
 private static void Validate(SubmissionWorkflowRelease release)
 {
  ArgumentNullException.ThrowIfNull(release);
  var parts = release.Repository?.Split('/') ?? [];
  if (parts.Length != 2 || parts.Any(p => p.Length == 0 || p.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))) ||
   release.ReleaseId <= 0 || string.IsNullOrWhiteSpace(release.Tag) || release.Tag.Any(char.IsControl) || release.Tag.Length > 256)
   throw new ArgumentException("Supply an identified repository and published release.");
  foreach (var pair in new[] { (release.SourceCommit, 40), (release.PackageSha256, 64), (release.ProfileSnapshotSha256, 64), (release.ToolingSha256, 64) })
   if (pair.Item1 == null || pair.Item1.Length != pair.Item2 || pair.Item1.Any(c => !(char.IsAsciiDigit(c) || c is >= 'a' and <= 'f')))
    throw new ArgumentException("Freeze the source commit, package, setup snapshot and tooling by lowercase digest.");
 }
 private static SubmissionWorkflowCheckpoint Load(string directory)
 {
  string path = Path.Combine(directory, "state.json");
  if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("Workflow state exceeds its limit.");
  var state = JsonSerializer.Deserialize<SubmissionWorkflowCheckpoint>(File.ReadAllBytes(path), Json) ?? throw new InvalidDataException("Missing workflow state.");
  if (state.SchemaVersion is not (1 or 2) || !Enum.IsDefined(state.Stage) || !Enum.IsDefined(state.Status) || state.InputSha256 != InputDigest(state.Release) || state.UpdatedUtc == default)
   throw new InvalidDataException("Invalid workflow checkpoint.");
  bool idle = state.Status is SubmissionWorkflowStatus.Ready or SubmissionWorkflowStatus.Completed;
  if (idle ? state.OperationId != null : state.OperationId == null || !Guid.TryParseExact(state.OperationId, "N", out _))
   throw new InvalidDataException("Invalid operation identity.");
  var order=Stages(state.SchemaVersion);int index=Array.IndexOf(order,state.Stage);
  int expected=state.Status==SubmissionWorkflowStatus.Completed?order.Length:index;
  if (index<0 || state.CompletedStages.Count != expected || order.Take(expected).Any(s=>!state.CompletedStages.ContainsKey(s)) ||
   state.Status == SubmissionWorkflowStatus.Completed && state.Stage != SubmissionWorkflowStage.Retain)
   throw new InvalidDataException("Workflow stage history is incomplete.");
  return state;
 }
 private static void VerifyReceipts(string directory, SubmissionWorkflowCheckpoint state) { foreach (var r in state.CompletedStages.Values) VerifyReceipt(directory, r); }
 private static void VerifyReceipt(string directory, SubmissionWorkflowReceipt receipt)
 {
  if (string.IsNullOrWhiteSpace(receipt.RelativePath) || Path.IsPathFullyQualified(receipt.RelativePath)) throw new InvalidDataException("Receipt paths must remain inside the run.");
  string full = Path.GetFullPath(receipt.RelativePath, directory);
  if (!Path.GetRelativePath(directory, full).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).All(p => p != ".."))
   throw new InvalidDataException("Receipt path escapes the run.");
  string boundary = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
  var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
  for (string? part = full; part != null && !string.Equals(part, boundary, comparison); part = Path.GetDirectoryName(part))
   if ((File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Receipt paths cannot be redirected.");
  if (new FileInfo(full).Length > 16 * 1024 * 1024 || Hash(File.ReadAllBytes(full)) != receipt.Sha256)
   throw new InvalidDataException("A completed step's receipt changed or is too large.");
 }
 private static void Save(string directory, SubmissionWorkflowCheckpoint state)
 {
  string temporary = Path.Combine(directory, "state-" + Guid.NewGuid().ToString("N") + ".tmp");
  try
  {
   using (var f = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { JsonSerializer.Serialize(f, state, Json); f.Flush(true); }
   SubmissionJournalFile.Replace(temporary, Path.Combine(directory, "state.json"));
  }
  finally { if (File.Exists(temporary)) File.Delete(temporary); }
 }
 private static string Hash(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));
}
