// Copyright (c) 2026 Neil Colvin. MIT licensed.
namespace CrestronHomeDevTools;

/// <summary>Explicit non-secret projection for setup inspection. Never serializes a credential or signature image.</summary>
public sealed record SubmissionDeveloperMetadata(string DeveloperName, string SenderEmail, string SmtpHost,
    string SmtpPort, string SignatureEntry, string SupportWebsite, string SupportEmail, string SupportPhone)
{
    public static SubmissionDeveloperMetadata From(SubmissionDeveloperProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return new(profile.DeveloperName, profile.SenderEmail, profile.SmtpHost, profile.SmtpPort,
            profile.SignatureEntry, profile.SupportWebsite, profile.SupportEmail, profile.SupportPhone);
    }
}
