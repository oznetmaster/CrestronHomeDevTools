// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;
using NUnit.Framework;
namespace CrestronHomeDevTools.Tests;
public sealed class SubmissionDeveloperMetadataTests
{
    [Test] public void InspectionProjectionNeverSerializesCredentialReferencesOrUnrelatedPrivateDetails()
    {
        var profile=new SubmissionDeveloperProfile {DeveloperName="Example", SenderEmail="sender@example.test",
            SmtpCredential="private-smtp-marker",UploaderCredential="private-uploader-marker",PostalAddress="private-address-marker"};
        string json=JsonSerializer.Serialize(SubmissionDeveloperMetadata.From(profile));
        Assert.That(json,Does.Contain("sender@example.test"));
        Assert.That(json,Does.Not.Contain("private-"));
        Assert.That(json,Does.Not.Contain("Credential"));
    }
}
