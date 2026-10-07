// Copyright (c) 2026 Neil Colvin. MIT licensed.
namespace CrestronHomeDevTools;

/// <summary>Uses the shared local rehearsal archive and SMTP path while retaining the full qualified-review identity.</summary>
public sealed class SubmissionRehearsalReviewMailTransport : ISubmissionReviewDeliveryTransport
{
 private readonly SubmissionRehearsalTransport archive;
 private readonly SubmissionSmtpMailer mailer;
 private readonly string digest;
 public SubmissionDeliveryEnvironment Environment => SubmissionDeliveryEnvironment.Rehearsal;
 public SubmissionRehearsalReviewMailTransport(string privateDirectory, SubmissionReviewDeliveryPlan plan, SubmissionSmtpMailer mailer)
 {
  ArgumentNullException.ThrowIfNull(mailer);
  digest=SubmissionDelivery.ReviewPlanDigest(plan);
  if(plan.Environment!=Environment || !plan.SendRehearsalEmail)throw new InvalidDataException("Select explicit qualified rehearsal test-mail.");
  archive=new(privateDirectory,SubmissionDelivery.ReviewArtifactPlan(plan),true,digest);
  this.mailer=mailer;
 }
 public Task<SubmissionUploadReceipt> UploadAsync(Stream package,string filename,CancellationToken token)=>archive.UploadAsync(package,filename,token);
 public async Task<SubmissionMailReceipt> SendReviewAsync(SubmissionReviewDeliveryPlan plan,SubmissionUploadReceipt upload,Stream attachment,string messageId,CancellationToken token)
 {
  if(SubmissionDelivery.ReviewPlanDigest(plan)!=digest)throw new InvalidDataException("The qualified rehearsal plan changed.");
  using var package=archive.OpenPackageForMail(digest,upload,messageId);
  return await mailer.SendRehearsalReviewAsync(plan,upload,package,attachment,messageId,token).ConfigureAwait(false);
 }
}
