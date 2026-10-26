// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License. See LICENSE.
using System.Text.Json;
using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class ConfigurationRestorationTests
{
    private string _folder=null!;
    [SetUp] public void Setup()=>_folder=Path.Combine(Path.GetTempPath(),"devtools-restore-test-"+Guid.NewGuid().ToString("N"));
    [TearDown] public void Cleanup()
    {
        if(Path.GetFullPath(_folder).StartsWith(Path.Combine(Path.GetTempPath(),"devtools-restore-test-"),StringComparison.OrdinalIgnoreCase)&&Directory.Exists(_folder))Directory.Delete(_folder,true);
    }
    private static IReadOnlyList<ConfigurationRestorationWrite> Writes()
    {
        var entity=new{Id=50,EntityType="Device"};var eventInfo=new{EventSource="pressed"};var gesture=new{Trigger="Default",Gesture="Default",ToggleState="None"};
        var modeEntry=new ConfigurationCaptureEntry("0001.json","v2/Programming","cp.programming:getAvailableModes",JsonSerializer.SerializeToElement(new{programmableEntity=entity,eventInfo}),0,"");
        var copyEntry=new ConfigurationCaptureEntry("0002.json","v2/Programming","cp.programming:copyProgramming",JsonSerializer.SerializeToElement(new{sourceEntity=entity,sourceEvent=eventInfo,sourceGesture=gesture}),0,"");
        var identity=new ConfigurationCaptureIdentity("192.0.2.1",new string('A',64),1,"MC4-R","4.012.0194");
        var manifest=new ConfigurationCaptureManifest(1,identity,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,"Test",[modeEntry,copyEntry]);
        var capture=new ValidatedConfigurationCapture(manifest,"",[
            new(modeEntry,JsonSerializer.SerializeToElement(new[]{new{Type="None"},new{Type="Sequence"}})),
            new(copyEntry,ManagedChildReplacementTests.Copy(100,101,21))]);
        return ConfigurationRestoration.Plan(capture,new Dictionary<int,int>{{21,41}});
    }
    [Test]
    public async Task ExistingSequenceIsClearedBeforePasteSoReferencesAreReplacedNotAppended()
    {
        var wire=new Connection();await using var client=new ConfigurationClient(wire);
        var writes=Writes();Assert.That(writes.Count,Is.EqualTo(1));
        await ConfigurationRestoration.ApplyAsync(client,writes,_folder);
        Assert.That(wire.Pasted,Is.True);
        Assert.That(wire.Commands.IndexOf("cp.programming:setMode"),Is.LessThan(wire.Commands.IndexOf("cp.programming:pasteProgramming")));
        Assert.That(File.Exists(Path.Combine(_folder,"0001-clear-intent.json")),Is.True);
        Assert.That(File.Exists(Path.Combine(_folder,"complete.json")),Is.True);
    }
    [Test]
    public async Task FailedClearReadbackStopsBeforePaste()
    {
        var wire=new Connection{ClearWorks=false};await using var client=new ConfigurationClient(wire);
        await Assert.ThrowsAsync<InvalidDataException>(async()=>await ConfigurationRestoration.ApplyAsync(client,Writes(),_folder));
        Assert.That(wire.Pasted,Is.False);Assert.That(File.Exists(Path.Combine(_folder,"complete.json")),Is.False);
    }
    private sealed class Connection:IConfigurationConnection
    {
        internal readonly List<string> Commands=[];internal bool ClearWorks=true,Pasted;private bool _cleared;
        public Task<T?> ExecuteResourceCommandAsync<T>(string path,string command,object? parameters=null,CancellationToken cancellationToken=default)
        {
            Commands.Add(command);object? response;
            switch(command)
            {
                case "cp.programming:getAvailableModes":response=new[]{new{Type="None"},new{Type="Sequence"}};break;
                case "cp.programming:setMode":_cleared=ClearWorks;response=null;break;
                case "cp.programming:getMode":response=new{Type=_cleared?"None":"Sequence"};break;
                case "cp.programming:pasteProgramming":if(!_cleared)throw new InvalidDataException("Paste would append to an existing sequence.");Pasted=true;response=Array.Empty<object>();break;
                case "cp.programming:copyProgramming":response=ManagedChildReplacementTests.Copy(500,501,41);break;
                default:throw new InvalidDataException("Unexpected command.");
            }
            return Task.FromResult(response==null?default:JsonSerializer.SerializeToElement(response).Deserialize<T>());
        }
        public Task<T?> GetAsync<T>(string path,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public Task<T?> ExecuteAsync<T>(int deviceId,string commandName,object? parameters=null,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public Task<OperationResult> WaitForOperationAsync(string operationId,TimeSpan timeout,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
}
