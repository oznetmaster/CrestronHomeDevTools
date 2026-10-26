// Copyright (c) 2026 Neil Colvin. MIT license.
using System.Text.Json;

namespace CrestronHomeDevTools;

/// <summary>A processor-reported backup failure. The diagnostic event can contain private data.</summary>
public sealed class CloudBackupOperationException : IOException
{
    public string OperationId { get; }
    /// <summary>Exact failure event, including any backend Message/SubsystemCode; retain privately.</summary>
    public JsonElement DiagnosticEvent { get; }
    internal CloudBackupOperationException(JsonElement diagnosticEvent)
        :base("The processor reported a cloud backup failure. The backup may still have taken effect; inspect the retained diagnostic event and cloud list before another request.")
    {
        OperationId=diagnosticEvent.GetProperty("OperationId").GetString()!;
        DiagnosticEvent=diagnosticEvent.Clone();
    }
}

/// <summary>Metadata returned by the processor. Date is retained verbatim: no timezone is inferred.</summary>
public sealed record CloudConfigurationBackup(string ConfigurationId,string Filename,string? Date,
    string? Version,string? VerifiedStatus,string? Type);
public sealed record CloudConfigurationBackupList(int ProcessorDeviceId,string OperationId,
    IReadOnlyList<CloudConfigurationBackup> Configurations);
public enum CloudConfigurationBackupKind { Normal, Golden }
/// <summary>BackendSucceeded is separate from observing exactly one new/updated matching list entry.</summary>
public sealed record CloudConfigurationBackupSave(int ProcessorDeviceId,string OperationId,
    CloudConfigurationBackupKind Kind,bool BackendSucceeded,IReadOnlyList<CloudConfigurationBackup> NewOrUpdatedEntries)
{
    public bool NewEntryVerified=>BackendSucceeded&&NewOrUpdatedEntries.Count==1;
}

/// <summary>Correlated backup events; not a general event subscription or file-download interface.</summary>
public interface ICloudBackupEventSource
{
    Task<IReadOnlyList<CloudConfigurationBackup>> WaitForCloudConfigurationListAsync(string operationId,TimeSpan timeout,CancellationToken token=default);
    Task WaitForCloudConfigurationSaveAsync(string operationId,TimeSpan timeout,CancellationToken token=default);
}

/// <summary>Lists saved cloud configuration metadata and requests a current configuration cloud backup.</summary>
/// <remarks>Uses the authenticated processor and its own backup context. Never restores, downloads files,
/// generates deploy codes or automatically retries a submitted backup. Metadata is not a local/full backup.</remarks>
public static class ProcessorCloudBackups
{
    internal const string ListCommand="cp.processorBackupRestore:beginConfigurationListRetrieval";
    internal const string SaveCommand="cp.processorBackupRestore:beginBackupOperation";

    public static async Task<CloudConfigurationBackupList> ListAsync(IConfigurationConnection connection,int processorDeviceId,
        TimeSpan? timeout=null,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        TimeSpan limit=Validate(processorDeviceId,timeout??TimeSpan.FromMinutes(1));
        var events=connection as ICloudBackupEventSource??throw new NotSupportedException("This connection cannot observe cloud backup events.");
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);deadline.CancelAfter(limit);
        await RequireCommand(connection,processorDeviceId,ListCommand,deadline.Token).ConfigureAwait(false);
        string id=await Submit(connection,processorDeviceId,ListCommand,new{deployCode=""},deadline.Token).ConfigureAwait(false);
        var configurations=await events.WaitForCloudConfigurationListAsync(id,limit,deadline.Token).ConfigureAwait(false);
        return new(processorDeviceId,id,Array.AsReadOnly(configurations.ToArray()));
    }

    /// <summary>Creates a normal backup, or explicitly replaces/saves the golden configuration.</summary>
    /// <remarks>A golden backup is never selected by default. Calling this method writes to cloud history.
    /// An acknowledged request or Ended event is not success. Requires dedicated Succeeded and Ended events,
    /// then reads the backup list again. NewEntryVerified is false for absent or ambiguous new entries.
    /// Timeout/transport failure can leave a submitted backup running; inspect before another request.</remarks>
    public static async Task<CloudConfigurationBackupSave> SaveAsync(IConfigurationConnection connection,int processorDeviceId,
        CloudConfigurationBackupKind kind,TimeSpan? timeout=null,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if(!Enum.IsDefined(kind))throw new ArgumentOutOfRangeException(nameof(kind));
        TimeSpan limit=Validate(processorDeviceId,timeout??TimeSpan.FromMinutes(5));
        var events=connection as ICloudBackupEventSource??throw new NotSupportedException("This connection cannot observe cloud backup events.");
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);deadline.CancelAfter(limit);
        // Check both capabilities before submitting a save, including the verification-list capability.
        await RequireCommand(connection,processorDeviceId,SaveCommand,deadline.Token).ConfigureAwait(false);
        var before=await ListAsync(connection,processorDeviceId,limit,deadline.Token).ConfigureAwait(false);
        string operation=kind==CloudConfigurationBackupKind.Normal?"backupConfiguration":"backupGoldenConfiguration";
        string id=await Submit(connection,processorDeviceId,SaveCommand,new{operation},deadline.Token).ConfigureAwait(false);
        await events.WaitForCloudConfigurationSaveAsync(id,limit,deadline.Token).ConfigureAwait(false);
        var after=await ListAsync(connection,processorDeviceId,limit,deadline.Token).ConfigureAwait(false);
        await events.WaitForCloudConfigurationSaveAsync(id,limit,deadline.Token).ConfigureAwait(false);
        string expectedType=kind==CloudConfigurationBackupKind.Normal?"Normal":"Golden";
        string expectedNumber=kind==CloudConfigurationBackupKind.Normal?"0":"1";
        var changes=after.Configurations.Where(now=>(string.Equals(now.Type,expectedType,StringComparison.OrdinalIgnoreCase)||now.Type==expectedNumber)&&
            // ConfigurationId was observed to be renumbered across listings. It is not a stable backup identity.
            !before.Configurations.Any(old=>old.Filename==now.Filename&&old.Date==now.Date&&old.Version==now.Version&&old.Type==now.Type)).ToArray();
        return new(processorDeviceId,id,kind,true,Array.AsReadOnly(changes));
    }
    private static TimeSpan Validate(int id,TimeSpan timeout)
    {
        if(id<=0)throw new ArgumentOutOfRangeException(nameof(id));
        if(timeout<=TimeSpan.Zero||timeout>TimeSpan.FromMinutes(30))throw new ArgumentOutOfRangeException(nameof(timeout));
        return timeout;
    }
    private static async Task RequireCommand(IConfigurationConnection connection,int id,string command,CancellationToken token)
    {
        var device=await connection.GetAsync<DeviceInfo>($"v2/Devices/{id}",token).ConfigureAwait(false);
        if(device?.Id!=id||!device.Commands.Contains(command))throw new NotSupportedException("The selected processor does not advertise the requested cloud backup capability.");
    }
    private static async Task<string> Submit(IConfigurationConnection connection,int id,string command,object parameters,CancellationToken token)
    {
        string? operation=await connection.ExecuteAsync<string>(id,command,parameters,token).ConfigureAwait(false);
        if(string.IsNullOrEmpty(operation)||operation.Length>256)throw new IOException("The cloud request returned no usable operation identity. It was not retried.");
        return operation;
    }
}
