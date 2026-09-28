// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License.
using System.Net;
using System.Text.Json;
using CrestronHomeDevTools.Automation;
using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class GitHubStoredCredentialTests
{
 private string root=null!;
 private const string Secret="SYNTHETIC-GITHUB-SECRET";
 [SetUp]public void Setup(){root=Path.Combine(TestContext.CurrentContext.WorkDirectory,"github-store-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);}
 [TearDown]public void Cleanup()=>Directory.Delete(root,true);
 private string Bind(string host="api.github.com",int port=443,DevToolsCredentialPurpose purpose=DevToolsCredentialPurpose.GitHub,string secret=Secret) {
  if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();
  var store=DevToolsPrivateStore.Create(Path.Combine(root,"store"));
  store.SaveCredential("release-api",new(purpose,host,"synthetic-account",secret,port));
  string path=Path.Combine(root,"bindings.json");
  File.WriteAllText(path,JsonSerializer.Serialize(new DevToolsCredentialBindings(store.DirectoryPath){GitHub="release-api"}));
  return path;
 }
 private sealed class Handler : HttpMessageHandler {
  public string? Token;public int Requests;
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Requests++;Token=request.Headers.Authorization?.Parameter;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("[]")});}
 }
 [Test]public async Task WatcherClientUsesEncryptedNamedEntryAndDoesNotSerializeTokenIntoProfiles() {
  if(!OperatingSystem.IsWindows()){Assert.Ignore("Windows encrypted credential store.");return;}
  string bindings=Bind();var profiles=new SubmissionAutomationReleaseProfiles(1,[]){CredentialBindings=bindings};
  using var handler=new Handler();using var client=AutomationReleaseDiscovery.CreateClient(profiles,handler);
  Assert.That(await new GitHubSubmissionRelease(client).ListPublishedAsync("example/driver",DateTimeOffset.UtcNow),Is.Empty);
  Assert.That(handler.Token,Is.EqualTo(Secret));
  Assert.That(JsonSerializer.Serialize(profiles)+File.ReadAllText(bindings),Does.Not.Contain(Secret));
  Assert.That(System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(root,"store","release-api.private"))),Does.Not.Contain(Secret));
 }
 [Test]public async Task PublicAccessRemainsOptional() {
  using var handler=new Handler();using var client=AutomationReleaseDiscovery.CreateClient(new(1,[]),handler);
  await new GitHubSubmissionRelease(client).ListPublishedAsync("example/driver",DateTimeOffset.UtcNow);
  Assert.That(handler.Token,Is.Null);Assert.That(handler.Requests,Is.EqualTo(1));
 }
 [TestCase("host")][TestCase("port")][TestCase("purpose")][TestCase("control")][TestCase("missing")]
 public void WrongOrInvalidStoredCredentialsCannotSendARequest(string mismatch) {
  if(!OperatingSystem.IsWindows()){Assert.Ignore("Windows encrypted credential store.");return;}
  string path=Bind(mismatch=="host"?"other.invalid":"api.github.com",mismatch=="port"?80:443,
   mismatch=="purpose"?DevToolsCredentialPurpose.Smtp:DevToolsCredentialPurpose.GitHub,mismatch=="control"?Secret+"\ninvalid":Secret);
  if(mismatch=="missing")File.Delete(Path.Combine(root,"store","release-api.private"));
  using var handler=new Handler();using var client=new HttpClient(handler);
  Assert.Catch(()=>DevToolsGitHubAuthentication.ApplyStoredCredential(client,path));
  Assert.That(handler.Requests,Is.Zero);Assert.That(client.DefaultRequestHeaders.Authorization,Is.Null);
 }
 [Test]public async Task IntakeRejectsAmbiguousCredentialSourcesBeforeSending() {
  string path=Path.Combine(root,"settings.json");File.WriteAllText(path,"{}");
  using var h=new Handler();using var client=new HttpClient(h);using var output=new StringWriter();using var error=new StringWriter();
  int code=await SubmissionReleaseIntakeCommand.RunAsync(["--settings",path,"--credentials",Path.Combine(root,"absent.json"),"--token-stdin","true"],new StringReader(Secret),output,error,default,client);
  Assert.That(code,Is.EqualTo(2));Assert.That(h.Requests,Is.Zero);Assert.That(output.ToString()+error,Does.Not.Contain(Secret));
 }
}
