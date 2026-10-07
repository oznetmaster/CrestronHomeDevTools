// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using CrestronHomeDevTools.Automation;
using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

public sealed class AutomationAssemblyIdentityTests
{
 [Test]
 public void InstalledWorkerIdentityUsesExactAssemblyBytes() {
  var assembly=typeof(SubmissionAutomationMode).Assembly;
  string expected=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assembly.Location)));
  Assert.That(AutomationFiles.AssemblyHash(assembly),Is.EqualTo(expected));
 }
 [Test]
 public void AssemblyWithoutFileCannotCreateRecoveryEvidence() {
  var assembly=AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("MemoryOnlyEvidenceProbe"),AssemblyBuilderAccess.Run);
  Assert.Throws<InvalidOperationException>(()=>AutomationFiles.AssemblyHash(assembly));
 }
}
