// Copyright (c) 2026 Neil Colvin. MIT license.
using System.Text.Json;
using System.Security.Cryptography;
using CrestronHomeDevTools;

namespace KasaTapoConfigurationRepair;

internal sealed record ProcessorIdentity(string Host,string Certificate,string SshFingerprint);
internal sealed class RepairState
{
    public required ProcessorIdentity Processor {get;init;}
    public required string Owner {get;init;}
    public required Review Review {get;init;}
    public required Snapshot Baseline {get;set;}
    public byte[]? Original {get;set;}
    public byte[]? Candidate {get;set;}
    public string Stage {get;set;}="Prepared";
    public ProcessorUptimeSnapshot? Boot {get;set;}
    public string RemoteOriginal=>"/user/KasaTapoRepair-"+Owner+".original.cfg";
    public string RemoteCandidate=>"/user/KasaTapoRepair-"+Owner+".candidate.cfg";
}
internal interface IJournal
{
    void Save(RepairState state,string stage);
}
internal sealed class PrivateJournal(string folder):IJournal
{
    internal string Folder=>folder;
    private static readonly byte[] Entropy=System.Text.Encoding.UTF8.GetBytes("KasaTapoConfigurationRepair/1");
    public void Save(RepairState state,string stage)
    {
        state.Stage=stage;Directory.CreateDirectory(folder);
        int index=Directory.GetFiles(folder,"*.dpapi").Length+1;
        byte[] plaintext=JsonSerializer.SerializeToUtf8Bytes(state);
        try
        {
            byte[] encrypted=ProtectedData.Protect(plaintext,Entropy,DataProtectionScope.CurrentUser);
            string path=Path.Combine(folder,index.ToString("D5")+".dpapi");
            using var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read);stream.Write(encrypted);stream.Flush(true);
        }
        finally{CryptographicOperations.ZeroMemory(plaintext);}
    }
    internal RepairState Load()
    {
        string path=Directory.GetFiles(folder,"*.dpapi").Order(StringComparer.Ordinal).Last();
        byte[] plain=ProtectedData.Unprotect(File.ReadAllBytes(path),Entropy,DataProtectionScope.CurrentUser);
        try
        {
            var state=JsonSerializer.Deserialize<RepairState>(plain)??throw new InvalidDataException("No recovery state.");
            if(!Guid.TryParseExact(state.Owner,"N",out _)||state.Processor.Host.Length>253)throw new InvalidDataException("Invalid recovery state.");
            return state;
        }
        finally{CryptographicOperations.ZeroMemory(plain);}
    }
    internal void Report(RepairState state,string outcome,string? errorType=null)
    {
        // Deliberately omit raw responses, configuration, credentials, addresses and pin values.
        var report=new{Tool="KasaTapo Configuration Repair",Version="1.0.0",Utc=DateTimeOffset.UtcNow,Outcome=outcome,
            Stage=state.Stage,ErrorType=errorType,HomeVersion=RepairModel.HomeVersion,DriverVersion=RepairModel.DriverVersion,
            Devices=state.Review.Children.Select(c=>new{c.DeviceId,c.Name,c.Model,c.RoomId}),
            OriginalSha256=state.Original is null?null:RepairModel.Hash(state.Original),
            CandidateSha256=state.Candidate is null?null:RepairModel.Hash(state.Candidate)};
        File.WriteAllText(Path.Combine(folder,"Support-report.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
    }
}
internal interface IRepairProcessor:IAsyncDisposable
{
    Task Acquire(string owner,bool resume);
    Task Release();
    Task<Snapshot> Snapshot();
    Task<ProcessorUptimeSnapshot> Uptime();
    Task StopConfirmed();
    Task<byte[]?> Read(string path);
    Task Upload(string path,byte[] bytes);
    Task Rename(string source,string destination);
    Task Reboot();
    Task VerifyStartup(RepairState state,bool restored);
}
internal static class RepairWorkflow
{
    internal static async Task Apply(IRepairProcessor processor,IJournal journal,RepairState state,Action<string> status)
    {
        if(state.Stage!="Prepared"||state.Review.Children.Length==0)throw new InvalidOperationException("A new nonempty review is required.");
        journal.Save(state,"AcquireIntent");await processor.Acquire(state.Owner,false);journal.Save(state,"LeaseAcquired");
        // Read-only refresh under reservation. Retain review identities, but replan from the stopped file below.
        status("Checking the reviewed devices again...");Snapshot fresh;
        try
        {
            fresh=await processor.Snapshot();_=RepairModel.Candidate(fresh,state.Review);
            state.Baseline=fresh;state.Boot=await processor.Uptime();
        }
        catch
        {
            journal.Save(state,"UnchangedReleaseIntent");await processor.Release();journal.Save(state,"Unchanged");throw;
        }
        journal.Save(state,"StopIntent");status("Stopping Crestron Home. Please keep this utility open...");await processor.StopConfirmed();journal.Save(state,"Stopped");
        var original=await processor.Read(RepairModel.ManifestPath)??throw new IOException("No stopped manifest.");
        var cache=await processor.Read(RepairModel.CachePath)??throw new IOException("No stopped cache.");
        var stopped=fresh with{Manifest=original,Cache=cache};
        state.Original=original;state.Candidate=RepairModel.Candidate(stopped,state.Review);
        journal.Save(state,"BackupSaved");
        if(await processor.Read(state.RemoteCandidate)!=null||await processor.Read(state.RemoteOriginal)!=null)throw new IOException("Recovery paths already exist.");
        journal.Save(state,"UploadIntent");await processor.Upload(state.RemoteCandidate,state.Candidate);
        RequireHash(await processor.Read(state.RemoteCandidate),state.Candidate);
        RequireHash(await processor.Read(RepairModel.ManifestPath),state.Original);
        journal.Save(state,"PreserveOriginalIntent");await processor.Rename(RepairModel.ManifestPath,state.RemoteOriginal);
        journal.Save(state,"InstallCandidateIntent");await processor.Rename(state.RemoteCandidate,RepairModel.ManifestPath);
        RequireHash(await processor.Read(RepairModel.ManifestPath),state.Candidate);
        RequireHash(await processor.Read(state.RemoteOriginal),state.Original);
        journal.Save(state,"RebootIntent");status("Records repaired. Rebooting the processor and waiting for Home...");await processor.Reboot();
        journal.Save(state,"StartupPending");await processor.VerifyStartup(state,false);
        journal.Save(state,"Verified");await processor.Release();journal.Save(state,"Complete");status("Repair verified. Test the lights in Crestron Home.");
    }
    internal static void RequireHash(byte[]? observed,byte[] expected)
    {if(observed is null||RepairModel.Hash(observed)!=RepairModel.Hash(expected))throw new InvalidDataException("File verification failed.");}
    // Pure decision function: recovery never overwrites an unrecognised active file.
    internal static bool RecoveryNeedsRestore(byte[]? active,RepairState state)
    {
        if(state.Original is null){if(active is null)throw new InvalidDataException("No original backup.");return false;}
        if(active!=null&&(RepairModel.Hash(active)==RepairModel.Hash(state.Original)||
            RepairModel.PersistedMatches(state.Original,active,state.Review.Children.Select(c=>c.DeviceId))))return false;
        if(active is null||state.Candidate!=null&&(RepairModel.Hash(active)==RepairModel.Hash(state.Candidate)||
            RepairModel.PersistedMatches(state.Candidate,active,state.Review.Children.Select(c=>c.DeviceId))))return true;
        throw new InvalidDataException("Active configuration changed outside this repair; support must inspect it.");
    }
    internal static async Task Recover(IRepairProcessor processor,IJournal journal,RepairState state,Action<string> status)
    {
        if(state.Stage is "Complete" or "Restored" or "Unchanged")throw new InvalidOperationException("This run is already closed.");
        await processor.Acquire(state.Owner,true);
        // Detect external edits before causing another Home interruption, then repeat the check after stop.
        _=RecoveryNeedsRestore(await processor.Read(RepairModel.ManifestPath),state);
        status("Recovery: stopping Home and checking retained hashes...");journal.Save(state,"RecoveryStopIntent");
        await processor.StopConfirmed();journal.Save(state,"RecoveryStopped");
        var active=await processor.Read(RepairModel.ManifestPath);
        if(RecoveryNeedsRestore(active,state))
        {
            RequireHash(await processor.Read(state.RemoteOriginal),state.Original!);
            string staging="/user/KasaTapoRepair-"+state.Owner+".restore.cfg";
            var existing=await processor.Read(staging);
            if(existing is null){journal.Save(state,"RecoveryUploadIntent");await processor.Upload(staging,state.Original!);}
            RequireHash(await processor.Read(staging),state.Original!);
            if(active!=null)
            {
                string retained="/user/KasaTapoRepair-"+state.Owner+".replaced.cfg";
                if(await processor.Read(retained)!=null)throw new InvalidDataException("Recovery retention path already exists.");
                journal.Save(state,"RecoveryRetainCandidateIntent");await processor.Rename(RepairModel.ManifestPath,retained);
            }
            journal.Save(state,"RecoveryInstallIntent");await processor.Rename(staging,RepairModel.ManifestPath);
            RequireHash(await processor.Read(RepairModel.ManifestPath),state.Original!);
        }
        state.Boot=await processor.Uptime();journal.Save(state,"RecoveryRebootIntent");
        status("Original records restored. Rebooting and checking Home...");await processor.Reboot();
        await processor.VerifyStartup(state,true);journal.Save(state,"RecoveryVerified");await processor.Release();journal.Save(state,"Restored");
        status("Original configuration restored and Home verified. Send the support report before trying another repair.");
    }
}
