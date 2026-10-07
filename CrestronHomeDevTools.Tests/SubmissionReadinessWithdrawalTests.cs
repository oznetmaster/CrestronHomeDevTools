using System.Text.Json;
using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class SubmissionReadinessWithdrawalTests
{
 private string _root=null!;
 [SetUp] public void Setup()=>_root=Path.Combine(TestContext.CurrentContext.WorkDirectory,"withdrawal-"+Guid.NewGuid().ToString("N"));
 [TearDown] public void Cleanup(){if(Directory.Exists(_root))Directory.Delete(_root,true);}
 private SubmissionOperatorHandle Ready()=>SubmissionOperatorStep.GetOrCreateReadiness(_root,new('a',64),"network.ready","Test processor","Ready for network-only test?");
 [Test] public async Task WithdrawalReleasesWaitingWorkerWithoutImpersonatingOperator()
 {
  var handle=Ready();var waiting=SubmissionOperatorStep.WaitAsync(handle);
  var response=SubmissionOperatorStep.WithdrawReadiness(handle,"Harness timing needs repair; no physical action requested.");
  Assert.That((await waiting.WaitAsync(TimeSpan.FromSeconds(5))).Outcome,Is.EqualTo(SubmissionOperatorOutcome.Cancelled));
  Assert.That(response.Reason,Is.Null,"Workflow explanation is separate from an operator response.");
  using var audit=JsonDocument.Parse(File.ReadAllText(Path.Combine(handle.Directory,"workflow-withdrawal.json")));
  Assert.That(audit.RootElement.GetProperty("requestSha256").GetString(),Is.EqualTo(handle.RequestSha256));
  Assert.That(audit.RootElement.GetProperty("reason").GetString(),Does.Contain("Harness timing"));
  Assert.That(SubmissionOperatorStep.Pending(_root,new('a',64)),Is.Empty);
 }
 [Test] public void ExistingOperatorReplyCannotBeWithdrawn()
 {
  var handle=Ready();var first=SubmissionOperatorStep.Respond(handle,SubmissionOperatorOutcome.Done);
  Assert.Throws<InvalidOperationException>(()=>SubmissionOperatorStep.WithdrawReadiness(handle,"Repair"));
  Assert.That(SubmissionOperatorStep.Read(handle).Response,Is.EqualTo(first));
  Assert.That(File.Exists(Path.Combine(handle.Directory,"workflow-withdrawal.json")),Is.False);
 }
 [Test] public void PhysicalActionPromptCannotBeWithdrawn()
 {
  var handle=SubmissionOperatorStep.Create(_root,new('a',64),"disconnect","Processor","Disconnect",TimeSpan.FromMinutes(2));
  Assert.Throws<InvalidOperationException>(()=>SubmissionOperatorStep.WithdrawReadiness(handle,"Repair"));
  Assert.That(SubmissionOperatorStep.Read(handle).Waiting,Is.True);
  Assert.That(File.Exists(Path.Combine(handle.Directory,"workflow-withdrawal.json")),Is.False);
 }
 [Test] public void RetryPreservesOriginalAuditAndResponse()
 {
  var handle=Ready();var first=SubmissionOperatorStep.WithdrawReadiness(handle,"Repair");
  Assert.That(SubmissionOperatorStep.WithdrawReadiness(handle,"Repair"),Is.EqualTo(first));
  Assert.Throws<InvalidDataException>(()=>SubmissionOperatorStep.WithdrawReadiness(handle,"Different repair"));
  Assert.That(SubmissionOperatorStep.Read(handle).Response,Is.EqualTo(first));
 }
 [Test] public void InterruptedPublicationCanFinishWithoutChangingAudit()
 {
  var handle=Ready();var first=SubmissionOperatorStep.WithdrawReadiness(handle,"Repair");
  File.Delete(Path.Combine(handle.Directory,"response.json"));
  Assert.That(SubmissionOperatorStep.WithdrawReadiness(handle,"Repair"),Is.EqualTo(first));
 }
 [Test] public void ChangedRequestAndEmptyReasonAreRejected()
 {
  var handle=Ready();Assert.Throws<ArgumentException>(()=>SubmissionOperatorStep.WithdrawReadiness(handle," "));
  File.AppendAllText(Path.Combine(handle.Directory,"request.json")," ");
  Assert.Throws<InvalidDataException>(()=>SubmissionOperatorStep.WithdrawReadiness(handle,"Repair"));
 }
}
