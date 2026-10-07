// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Security.Cryptography;

namespace CrestronHomeDevTools;

/// <summary>Verifies a recipient-accessible rehearsal download without sending credentials or uploading files.</summary>
internal static class SubmissionPackageLink
{
 internal static Uri Validate(string url)
 {
  if (url.Length > 8192 || url.Any(char.IsWhiteSpace) || url.Any(char.IsControl) ||
   !Uri.TryCreate(url,UriKind.Absolute,out var uri) || uri.Scheme != "https" ||
   uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || uri.Host.EndsWith(".invalid",StringComparison.OrdinalIgnoreCase))
   throw new ArgumentException("Use an absolute HTTPS package download without credentials, fragments or placeholders.");
  return uri;
 }
 internal static async Task VerifyAsync(string url,string expected,CancellationToken token)
 {
  using var handler=new HttpClientHandler { AllowAutoRedirect=false, UseCookies=false };
  using var client=new HttpClient(handler);
  await VerifyAsync(client,url,expected,token).ConfigureAwait(false);
 }
 internal static async Task VerifyAsync(HttpClient client,string url,string expected,CancellationToken token)
 {
  using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);
  deadline.CancelAfter(TimeSpan.FromSeconds(90));
  Uri current=Validate(url);
  for(int redirects=0;redirects<=5;redirects++)
  {
   using var response=await client.GetAsync(current,HttpCompletionOption.ResponseHeadersRead,deadline.Token).ConfigureAwait(false);
   if((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
   {
    if(redirects==5 || response.Headers.Location==null)throw new InvalidDataException("Package download redirect limit or missing location.");
    current=Validate(new Uri(current,response.Headers.Location).AbsoluteUri);
    continue;
   }
   response.EnsureSuccessStatusCode();
   const long limit=64*1024*1024;
   if(response.Content.Headers.ContentLength>limit)throw new InvalidDataException("Package download exceeds 64 MiB.");
   using var stream=await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
   using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
   byte[] buffer=new byte[81920];long length=0;int count;
   while((count=await stream.ReadAsync(buffer,deadline.Token).ConfigureAwait(false))!=0)
   {
    length+=count;
    if(length>limit)throw new InvalidDataException("Package download exceeds 64 MiB.");
    hash.AppendData(buffer,0,count);
   }
   if(length==0 || Convert.ToHexStringLower(hash.GetHashAndReset())!=expected)
    throw new InvalidDataException("Package download differs from the approved package.");
   return;
  }
  throw new InvalidDataException("Package download was not verified.");
 }
}
