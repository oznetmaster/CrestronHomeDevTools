// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;
using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class ManagedDeviceCompletionTests
{
    private string _journal = null!;
    private static readonly ManagedDeviceRequest Request = new(17, "Platform", "1.0.0.1", "child", "Demo", "Light", 3);
    private static DriverConfiguration.Inputs Inputs => new(null,
        [new("Activation", new Dictionary<string,string>{{"ActivationMarker","true"}})]);

    [SetUp]
    public void Setup()
    {
        _journal = Path.Combine(TestContext.CurrentContext.WorkDirectory, "managed-configuration-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_journal);
        File.WriteAllText(Path.Combine(_journal,"request.json"),JsonSerializer.Serialize(Request));
        File.WriteAllText(Path.Combine(_journal,"created-child.json"),JsonSerializer.Serialize(new
            {DeviceId=18,Request.ParentId,Request.Name,Request.ChildModel,Request.ParentVersion,Request.LocationId}));
        File.WriteAllText(Path.Combine(_journal,"commission-response.json"),"{\"Response\":{\"Id\":18,\"CommissioningResult\":\"Success\"}}");
    }
    [TearDown] public void Cleanup()=>Directory.Delete(_journal,true);

    [TestCase(true)]
    [TestCase(false)]
    public async Task ConfigurationAcceptsVerifiedNativeConversionOrOrdinaryReadyChild(bool native)
    {
        var connection=new Connection{Native=native};
        var result=await ManagedDeviceCommissioning.ConfigureCreatedAsync(new(connection),_journal,Inputs,TimeSpan.FromSeconds(2));
        Assert.That(result,Is.EqualTo(new DriverConfigurationResult(18,true,true)));
        Assert.That(connection.Writes,Is.EqualTo(1));
        Assert.That(Directory.GetFiles(_journal).Length,Is.EqualTo(3),"Original commissioning evidence must remain unchanged.");
    }
    [TestCase("identity")]
    [TestCase("room")]
    [TestCase("ambiguous")]
    public async Task NativeConversionStillRejectsMismatchedLoads(string fault)
    {
        var connection=new Connection{Fault=fault};
        await Assert.ThrowsAsync<InvalidDataException>(async()=>await ManagedDeviceCommissioning.ConfigureCreatedAsync(new(connection),_journal,Inputs,TimeSpan.FromSeconds(2)));
        Assert.That(connection.Writes,Is.EqualTo(1));
    }
    [TestCase(true)]
    [TestCase(false)]
    public async Task OfflineCompletionTimesOutWithoutRepeatingConfiguration(bool native)
    {
        var connection=new Connection{Native=native,Fault="offline"};
        await Assert.ThrowsAsync<TimeoutException>(async()=>await ManagedDeviceCommissioning.ConfigureCreatedAsync(new(connection),_journal,Inputs,TimeSpan.FromMilliseconds(100)));
        Assert.That(connection.Writes,Is.EqualTo(1));
    }
    [Test]
    public async Task ContradictoryReceiptIsRejectedBeforeAnyWrite()
    {
        var connection=new Connection();
        File.WriteAllText(Path.Combine(_journal,"commission-response.json"),"{\"Response\":{\"Id\":99,\"CommissioningResult\":\"Success\"}}");
        await Assert.ThrowsAsync<InvalidDataException>(async()=>await ManagedDeviceCommissioning.ConfigureCreatedAsync(new(connection),_journal,Inputs,TimeSpan.FromSeconds(2)));
        Assert.That(connection.Writes,Is.Zero);
    }
    private sealed class Connection : IConfigurationConnection
    {
        public bool Native {get;init;}=true;
        public string? Fault {get;init;}
        public int Writes {get;private set;}
        private static JsonElement Json(object value)=>JsonSerializer.SerializeToElement(value);
        public Task<T?> GetAsync<T>(string path,CancellationToken cancellationToken=default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool applied=Writes>0, online=applied&&Fault!="offline";
            var parent=new DeviceInfo{Id=17,Model="Platform",PropertyValues=new(){["cp.driverInformation:version"]=Json("1.0.0.1")}};
            var child=new DeviceInfo{Id=18,ParentDeviceId=17,Name="Demo",Model="Light",LocationId=3,
                Commands=["cp.driverConfiguration:getFirstConfigurationStep","cp.driverConfiguration:applyConfigurationStep"],
                PropertyValues=new(){["cp.driverInformation:version"]=Json("1.0.0.1"),["cp.driverConfiguration:isConfigured"]=Json(applied),
                    ["onlineIndicator:isOnline"]=Json(online),["readyIndicator:isReady"]=Json(online)}};
            var inventory=new Dictionary<string,DeviceInfo>{{"17",parent},{"18",child}};
            if(applied&&Native)
            {
                child=child with{LocationId=null,Commands=[],PropertyValues=new(){
                    ["platform:managedDevices"]=Json(new[]{new{Id=Fault=="identity"?"other":"child"}}),
                    ["onlineIndicator:isOnline"]=Json(online),["cp.driverConfiguration:driverLoadingStatus"]=Json("Loaded")}};
                inventory["18"]=child;
                var load=new DeviceInfo{Id=19,ParentDeviceId=18,Name="Demo",Model="Light",LocationId=Fault=="room"?4:3,
                    Commands=online?["lightDimmer:setLevel"]:[],PropertyValues=new(){["lightType:variant"]=Json("load"),["lightDimmer:level"]=Json(0.0)}};
                inventory["19"]=load;
                if(Fault=="ambiguous")inventory["20"]=load with{Id=20};
            }
            object result=path=="v2/Devices"?inventory:child;
            return Task.FromResult(JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(result)));
        }
        public Task<T?> ExecuteAsync<T>(int id,string command,object? parameters=null,CancellationToken cancellationToken=default)
        {
            Assert.That(id,Is.EqualTo(18));
            object? result;
            if(command=="cp.driverConfiguration:getFirstConfigurationStep")
                result=new{Id="Activation",ConfigurationErrors=(object?)null,Items=new[]{new{Id="ActivationMarker",Value=new{ReadOnly=false}}}};
            else
            {
                Assert.That(command,Is.EqualTo("cp.driverConfiguration:applyConfigurationStep"));
                Writes++;result=null;
            }
            return Task.FromResult(JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(result)));
        }
        public Task<OperationResult> WaitForOperationAsync(string operationId,TimeSpan timeout,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
}
