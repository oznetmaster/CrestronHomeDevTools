// Copyright (c) 2026 Neil Colvin. MIT license.
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CrestronHomeDevTools;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace KasaTapoConfigurationRepair;

internal sealed class ProcessorAccess(ProcessorIdentity identity,NetworkCredential credentials):IRepairProcessor
{
    private ProcessorOperationLease? lease;
    internal static async Task<ProcessorIdentity> Probe(string host)
    {
        if(Uri.CheckHostName(host)==UriHostNameType.Unknown||host.Contains(':'))throw new ArgumentException("Use a processor IPv4 address or DNS name.");
        string? sshPin=null,certificate=null;
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using(var probe=new SftpClient(host,"host-key-probe",""))
        {
            probe.ConnectionInfo.Timeout=TimeSpan.FromSeconds(10);
            probe.HostKeyReceived+=(_,e)=>{sshPin=e.FingerPrintSHA256;e.CanTrust=false;};
            try{await probe.ConnectAsync(timeout.Token);}catch(SshConnectionException)when(sshPin!=null){}
        }
        using var handler=new HttpClientHandler{AllowAutoRedirect=false,ServerCertificateCustomValidationCallback=(_,cert,_,_)=>
        {if(cert is null)return false;certificate=Convert.ToHexString(SHA256.HashData(cert.RawData));return true;}};
        using var http=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(10)};
        using var response=await http.GetAsync($"https://{host}/cws/api/v2",HttpCompletionOption.ResponseHeadersRead,timeout.Token);
        if(sshPin is null||certificate is null)throw new IOException("Processor identities unavailable.");
        return new(host,certificate,sshPin);
    }
    private HttpClient Http()=>new(new HttpClientHandler{AllowAutoRedirect=false,
        ServerCertificateCustomValidationCallback=(_,cert,_,_)=>cert!=null&&Convert.ToHexString(SHA256.HashData(cert.RawData))==identity.Certificate}){Timeout=TimeSpan.FromSeconds(12)};
    private async Task<SftpClient> Files()
    {
        var client=new SftpClient(identity.Host,credentials.UserName,credentials.Password);
        client.ConnectionInfo.Timeout=TimeSpan.FromSeconds(10);client.OperationTimeout=TimeSpan.FromSeconds(20);
        client.HostKeyReceived+=(_,e)=>e.CanTrust=e.FingerPrintSHA256==identity.SshFingerprint;
        try{using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(15));await client.ConnectAsync(deadline.Token);return client;}
        catch{client.Dispose();throw;}
    }
    public async Task Acquire(string owner,bool resume)
    {
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(20));
        lease=resume?await ProcessorOperationLease.ResumeAsync(identity.Host,credentials,identity.SshFingerprint,owner,deadline.Token):
            await ProcessorOperationLease.AcquireAsync(identity.Host,credentials,identity.SshFingerprint,owner,deadline.Token);
    }
    public async Task Release(){using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));await lease!.ReleaseAsync(timeout.Token);}
    public async Task<byte[]?> Read(string path)
    {
        using var client=await Files();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(25));
        if(!await client.ExistsAsync(path,timeout.Token))return null;
        var info=await client.GetAttributesAsync(path,timeout.Token);
        if(!info.IsRegularFile||info.Size>16*1024*1024)throw new InvalidDataException("File type or size unsupported.");
        using var memory=new MemoryStream();
        // OpenRead avoids an unbounded growing download if the source changes while being read.
        using(var input=client.OpenRead(path))
        {
            byte[] buffer=new byte[32768];int count;
            while((count=await input.ReadAsync(buffer,timeout.Token))>0){if(memory.Length+count>16*1024*1024)throw new InvalidDataException("File grew past its limit.");memory.Write(buffer,0,count);}
        }
        if(memory.Length!=info.Size||(await client.GetAttributesAsync(path,timeout.Token)).Size!=info.Size)throw new IOException("File changed while reading.");
        return memory.ToArray();
    }
    public async Task Upload(string path,byte[] bytes)
    {
        using var client=await Files();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(25));using var data=new MemoryStream(bytes,false);
        await client.UploadFileAsync(data,path,false,null,timeout.Token);
    }
    public async Task Rename(string source,string destination)
    {
        using var client=await Files();
        if(client.Exists(destination))throw new IOException("Rename target already exists.");
        client.RenameFile(source,destination);
    }
    public Task<ProcessorUptimeSnapshot> Uptime()=>ProcessorUptime.ReadAsync(identity.Host,credentials,identity.SshFingerprint,TimeSpan.FromSeconds(12));
    public async Task<Snapshot> Snapshot()
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(75));
        using var http=Http();using var response=await http.GetAsync($"https://{identity.Host}/cws/api/v2",HttpCompletionOption.ResponseHeadersRead,timeout.Token);
        response.EnsureSuccessStatusCode();await using var stream=await response.Content.ReadAsStreamAsync(timeout.Token);
        using var metadata=await JsonDocument.ParseAsync(stream,cancellationToken:timeout.Token);
        string home=metadata.RootElement.GetProperty("ProgramVersion").GetString()!;
        await using var client=await ConfigurationClient.ConnectAsync(new(){Host=identity.Host,CertificateSha256=identity.Certificate,RequestTimeout=TimeSpan.FromSeconds(12)},credentials,timeout.Token);
        var devices=(await client.GetDevicesAsync(timeout.Token)).ToArray();var rooms=(await client.GetLocationsAsync(timeout.Token)).ToArray();
        byte[] manifest=await Read(RepairModel.ManifestPath)??throw new IOException("No manifest.");
        byte[] cache=await Read(RepairModel.CachePath)??throw new IOException("No cache.");
        return new(home,manifest,cache,devices,rooms);
    }
    public async Task StopConfirmed()
    {
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(65));
        using var ssh=new SshClient(identity.Host,credentials.UserName,credentials.Password);ssh.ConnectionInfo.Timeout=TimeSpan.FromSeconds(10);
        ssh.HostKeyReceived+=(_,e)=>e.CanTrust=e.FingerPrintSHA256==identity.SshFingerprint;await ssh.ConnectAsync(deadline.Token);
        using var shell=ssh.CreateShellStream("xterm",100,24,800,600,65536);
        async Task<string> Prompt()
        {
            var text=new StringBuilder();
            while(true)
            {
                deadline.Token.ThrowIfCancellationRequested();if(shell.DataAvailable)text.Append(shell.Read());
                if(text.Length>2*1024*1024)throw new IOException("Console output exceeded limit.");
                if(Regex.IsMatch(text.ToString(),@"(?:^|[\r\n])[A-Za-z0-9][A-Za-z0-9_.:-]{0,80}>"))return text.ToString();
                if(!ssh.IsConnected)throw new IOException("Console disconnected.");await Task.Delay(75,deadline.Token);
            }
        }
        await Prompt();shell.WriteLine("enableprogramcmd");string enabled=await Prompt();
        if(Regex.IsMatch(enabled,"Bad or Incomplete|Invalid command|denied",RegexOptions.IgnoreCase))throw new IOException("Program commands unavailable.");
        shell.WriteLine("stopprog -p:0");string stopped=await Prompt();
        if(Regex.IsMatch(stopped,"Bad or Incomplete|Invalid command|denied",RegexOptions.IgnoreCase))throw new IOException("Home stop rejected.");
        using var http=Http();
        while(true)
        {
            deadline.Token.ThrowIfCancellationRequested();using var response=await http.GetAsync($"https://{identity.Host}/cws/api/v2",deadline.Token);
            if(response.StatusCode==HttpStatusCode.NotFound)return;
            await Task.Delay(500,deadline.Token);
        }
    }
    public async Task Reboot()
    {
        // No retry after submission, including an unconfirmed acknowledgement.
        var result=await ProcessorReboot.RequestAsync(new(identity.Host),credentials,identity.SshFingerprint,(_,_)=>Task.FromResult(true),TimeSpan.FromSeconds(15));
        if(result.Status==ProcessorRebootStatus.Cancelled)throw new IOException("Reboot not requested.");
        // Accepted and Unconfirmed both require independent uptime/startup verification.
    }
    public async Task VerifyStartup(RepairState state,bool restored)
    {
        using var limit=new CancellationTokenSource(TimeSpan.FromMinutes(8));
        while(!limit.IsCancellationRequested)
        {
            try
            {
                var uptime=await Uptime();
                if(state.Boot is null||uptime.EarliestStartUtc<=state.Boot.LatestStartUtc)throw new IOException("Full reboot not confirmed.");
                var now=await Snapshot();
                if(now.HomeVersion!=RepairModel.HomeVersion||!RepairModel.IdentitiesMatch(state.Baseline.Devices,now.Devices))throw new IOException("Device identities differ.");
                if(state.Original is not null)
                {
                    if(restored)
                    {if(!RepairModel.PersistedMatches(state.Original,now.Manifest,state.Review.Children.Select(c=>c.DeviceId)))throw new IOException("Original saved configuration differs.");}
                    else
                    {
                        if(state.Candidate is null||!RepairModel.PersistedMatches(state.Candidate,now.Manifest,state.Review.Children.Select(c=>c.DeviceId)))throw new IOException("Saved candidate differs after reboot.");
                    }
                }
                var root=state.Review.Children.First().ParentId;
                var before=DriverReadiness.Inspect(state.Baseline.Devices,root,"KasaTapoPlatform",RepairModel.DriverVersion);
                var after=DriverReadiness.Inspect(now.Devices,root,"KasaTapoPlatform",RepairModel.DriverVersion);
                if(after.Issues.Any(i=>!before.Issues.Any(b=>b.DeviceId==i.DeviceId&&b.Reason==i.Reason)))throw new IOException("New readiness issues after startup.");
                if(!restored)
                {
                    var repaired=state.Review.Children.Select(c=>c.DeviceId).ToHashSet();
                    if(after.Issues.Any(i=>repaired.Contains(i.DeviceId)))throw new IOException("Readiness pending.");
                    foreach(var selected in state.Review.Children)
                    {
                        var loads=now.Devices.Where(d=>d.ParentDeviceId==selected.DeviceId&&d.PropertyValues.TryGetValue("lightType:variant",out var p)&&p.GetString()=="load").ToArray();
                        if(loads.Length!=1||after.Issues.Any(i=>i.DeviceId==loads[0].Id))throw new IOException("Native light control unavailable.");
                    }
                }
                return;
            }
            catch(Exception ex)when(ex is IOException or HttpRequestException or System.Net.WebSockets.WebSocketException or ProcessorApiException or OperationCanceledException or SshException or InvalidDataException)
            {await Task.Delay(3000,limit.Token);}
        }
        throw new TimeoutException("Startup verification incomplete.");
    }
    public ValueTask DisposeAsync(){lease?.Dispose();return ValueTask.CompletedTask;}
}
