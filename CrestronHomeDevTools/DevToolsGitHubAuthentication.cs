// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License.
namespace CrestronHomeDevTools;

/// <summary>Configure an API-only GitHub client from an explicitly selected encrypted entry.
/// The caller owns the client; never reuse it for other services or log its headers.</summary>
public static class DevToolsGitHubAuthentication
{
 public static void ApplyStoredCredential(HttpClient client, string bindingsPath)
 {
  ArgumentNullException.ThrowIfNull(client);
  if(!Path.IsPathFullyQualified(bindingsPath))throw new ArgumentException("Use an absolute credential bindings path.");
  if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Stored GitHub access requires the provisioned Windows identity.");
  var saved=DevToolsCredentialBindings.Read(bindingsPath).Resolve(DevToolsCredentialPurpose.GitHub,"api.github.com",443);
  string secret=saved.Password;
  if(string.IsNullOrWhiteSpace(secret) || secret.Length>65536 || secret.Any(char.IsWhiteSpace) || secret.Any(char.IsControl))
   throw new InvalidDataException("Invalid saved GitHub API credential.");
  client.DefaultRequestHeaders.Authorization=new("Bearer",secret);
 }
}
