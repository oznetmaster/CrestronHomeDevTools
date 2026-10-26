// Copyright (c) 2026 Neil Colvin. MIT license.
using System.Text.Json;

namespace CrestronHomeDevTools;

internal sealed class CloudBackupTracker
{
    private sealed class State
    {
        internal IReadOnlyList<CloudConfigurationBackup>? List;
        internal bool Succeeded,Ended;
        internal Exception? Failure;
        internal int Waiters;
        internal TaskCompletionSource Changed=new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private readonly object gate=new();
    private readonly Dictionary<string,State> operations=new(StringComparer.Ordinal);
    private Exception? failure;
    private State Get(string id)
    {
        if(operations.TryGetValue(id,out var state))return state;
        if(operations.Count>=256)
        {
            var removable=operations.FirstOrDefault(p=>p.Value.Waiters==0);
            if(removable.Key is null)throw new IOException("Too many outstanding cloud operations; outcome cannot be confirmed.");
            operations.Remove(removable.Key);
        }
        operations[id]=state=new();return state;
    }
    internal void Accept(JsonElement message)
    {
        if(message.ValueKind!=JsonValueKind.Object||!message.TryGetProperty("EventType",out var type)||type.ValueKind!=JsonValueKind.String||
            !message.TryGetProperty("OperationId",out var operation)||operation.ValueKind!=JsonValueKind.String)return;
        string? name=type.GetString(),id=operation.GetString();
        if(string.IsNullOrEmpty(id)||id.Length>256)return;
        bool list=name=="cp.processorBackupRestore:configurationListChanged",status=name=="cp.processorBackupRestore:backupStatusChanged";
        bool genericFailure=name=="cp.types:operationStatusChanged"&&message.TryGetProperty("LatestStatus",out var generic)&&generic.ToString() is "Failed" or "2";
        if(!list&&!status&&!genericFailure)return;
        lock(gate)
        {
            State state;
            try{state=Get(id);}catch(IOException error){FailLocked(error);return;}
            try
            {
                if(genericFailure)state.Failure??=new CloudBackupOperationException(message);
                if(list)state.List=ParseList(message.GetProperty("ConfigurationList"));
                if(status)
                {
                    string latest=message.GetProperty("LatestStatus").ToString();
                    // BackupRestoreStatus differs from cp.types operation status: 2 is Succeeded here.
                    switch(latest)
                    {
                        case "Failed":case "0":case "UnzipFailed":case "1":state.Failure??=new CloudBackupOperationException(message);break;
                        case "Succeeded":case "2":state.Succeeded=true;break;
                        case "Ended":case "4":state.Ended=true;break;
                        case "Started":case "3":case "Unpacked":case "5":break;
                        default:state.Failure??=new InvalidDataException("The cloud backup status is unsupported.");break;
                    }
                }
            }
            catch(Exception error)when(error is JsonException or InvalidOperationException or KeyNotFoundException or InvalidDataException)
            {state.Failure??=new InvalidDataException("The cloud backup response layout is unsupported.");}
            var changed=state.Changed;state.Changed=new(TaskCreationOptions.RunContinuationsAsynchronously);changed.TrySetResult();
        }
    }
    private static IReadOnlyList<CloudConfigurationBackup> ParseList(JsonElement value)
    {
        if(value.ValueKind!=JsonValueKind.Array||value.GetArrayLength()>2048)throw new InvalidDataException();
        var list=new List<CloudConfigurationBackup>();var identities=new HashSet<(string,string)>();
        foreach(var entry in value.EnumerateArray())
        {
            string? Text(string key,bool required=false)
            {
                if(!entry.TryGetProperty(key,out var field)||field.ValueKind==JsonValueKind.Null)
                {if(required)throw new InvalidDataException();return null;}
                if(field.ValueKind is not (JsonValueKind.String or JsonValueKind.Number))throw new InvalidDataException();
                string text=field.ValueKind==JsonValueKind.String?field.GetString()!:field.GetRawText();
                if(text.Length>2048||required&&text.Length==0)throw new InvalidDataException();return text;
            }
            var item=new CloudConfigurationBackup(Text("ConfigurationId",true)!,Text("Filename",true)!,Text("Date"),Text("Version"),Text("VerifiedStatus"),Text("Type"));
            if(!identities.Add((item.ConfigurationId,item.Filename)))throw new InvalidDataException();list.Add(item);
        }
        return list.AsReadOnly();
    }
    internal async Task<IReadOnlyList<CloudConfigurationBackup>> WaitForList(string id,TimeSpan timeout,CancellationToken token)
        =>(await Wait(id,timeout,false,token).ConfigureAwait(false))!;
    internal async Task WaitForSave(string id,TimeSpan timeout,CancellationToken token)
        =>_=await Wait(id,timeout,true,token).ConfigureAwait(false);
    private async Task<IReadOnlyList<CloudConfigurationBackup>?> Wait(string id,TimeSpan timeout,bool save,CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);if(id.Length>256||timeout<=TimeSpan.Zero||timeout>TimeSpan.FromMinutes(30))throw new ArgumentOutOfRangeException(nameof(timeout));
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(timeout);
        State state;lock(gate){state=Get(id);state.Waiters++;}
        try
        {
            while(true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                Task change;
                lock(gate)
                {
                    if(failure is not null)throw new IOException("Cloud event connection ended; outcome is unknown.",failure);
                    if(state.Failure is not null)throw state.Failure;
                    // A successful upload is followed by a list-refresh phase under the same operation ID.
                    // Wait for its dedicated Ended event before submitting an independent verification list.
                    if(save&&state.Succeeded&&state.Ended)return null;
                    if(!save&&state.List is not null&&(state.Ended||state.Succeeded))return state.List;
                    if(save&&state.Ended)throw new IOException("Cloud operation ended without the required success evidence; outcome is unconfirmed.");
                    change=state.Changed.Task;
                }
                await change.WaitAsync(deadline.Token).ConfigureAwait(false);
            }
        }
        finally{lock(gate)state.Waiters--;}
    }
    internal void Fail(Exception error){lock(gate)FailLocked(error);}
    private void FailLocked(Exception error)
    {failure??=error;foreach(var state in operations.Values)state.Changed.TrySetResult();}
}
