// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License.
using System.Security.Cryptography;
using System.Text.Json;
using NUnit.Framework;
namespace CrestronHomeDevTools.Tests;
[TestFixture]
public sealed class WorkerExecutionPathsTests {
    string root = null!, input = null!; WorkerPathBinding pin = null!;
    [SetUp] public void Setup() {
        root = Directory.CreateTempSubdirectory("worker-path-contract-").FullName;
        input = Path.Combine(root,"input.json"); File.WriteAllText(input,"{\"fixture\":true}"); byte[] b=File.ReadAllBytes(input);
        pin=new("cleanup-input",input,WorkerPathRole.WorkerInput,b.Length,Convert.ToHexString(SHA256.HashData(b)));
    }
    [TearDown] public void Cleanup() { if(Directory.Exists(root))Directory.Delete(root,true); }
    WorkerExecutionPaths Paths(params WorkerPathBinding[] extra) => new(Environment.MachineName,root,@"\\controller\project",new[]{pin}.Concat(extra).ToArray());
    WorkerPathBinding Output(string name="terminal")=>new(name,Path.Combine(root,name+".json"),WorkerPathRole.NewWorkerFile);
    [Test] public void RealAccessProbeLeavesNoSentinelAndDoesNotCreateResults() {
        var output=Output();Paths(output).InspectAccess(Environment.MachineName);
        Assert.That(Directory.GetFiles(root),Is.EqualTo(new[]{input})); Assert.That(File.Exists(output.Path),Is.False);
    }
    [Test] public void WrongHostCannotReadInputDirectlyAndCallerCannotMutateTheContract() {
        Assert.Throws<InvalidDataException>(()=>new WorkerExecutionPaths("different-worker",root,null,new[]{pin}).ReadInput(pin.Name));
        var original=new List<WorkerPathBinding>{pin};var paths=new WorkerExecutionPaths(Environment.MachineName,root,null,original);original.Clear();Assert.That(paths.ReadInput(pin.Name),Is.EqualTo(File.ReadAllBytes(input)));
    }
    [Test] public void WrongHostFailsBeforeAnyOutputProbe() {
        bool called=false;Assert.Throws<InvalidDataException>(()=>Paths(Output()).InspectAccess("a-different-worker",_=>called=true));Assert.That(called,Is.False);
    }
    [Test] public void ExplicitPinnedPrivateStoreInputRootNeverPermitsOutputsThere() {
        var extra=Directory.CreateTempSubdirectory("worker-input-only-");
        try {
            string p=Path.Combine(extra.FullName,"encrypted-entry.json");File.WriteAllText(p,"synthetic encrypted entry");byte[] bytes=File.ReadAllBytes(p);
            var external=new WorkerPathBinding("private-store",p,WorkerPathRole.WorkerInput,bytes.Length,Convert.ToHexString(SHA256.HashData(bytes)));
            Assert.Throws<InvalidDataException>(()=>Paths(external).ValidateLayout(Environment.MachineName));
            external=external with{WorkerInputRoot=extra.FullName};Paths(external,Output()).InspectAccess(Environment.MachineName);
            Assert.Throws<InvalidDataException>(()=>Paths(new WorkerPathBinding("output",Path.Combine(extra.FullName,"wrong.json"),WorkerPathRole.NewWorkerFile){WorkerInputRoot=extra.FullName}).ValidateLayout(Environment.MachineName));
        }finally{extra.Delete(true);}
    }
    [Test] public void NestedControllerLocalCredentialCannotBeUsedAsSharedInput() {
        var bad=new WorkerPathBinding("nested-credential",input,WorkerPathRole.ControllerSharedInput,pin.Length,pin.Sha256);
        Assert.Throws<InvalidDataException>(()=>new WorkerExecutionPaths(Environment.MachineName,root,@"\\controller\project",new[]{bad}).ValidateLayout(Environment.MachineName));
    }
    [Test] public void ReadableSharedPathCannotBecomeWorkerOutput() {
        var bad=new WorkerPathBinding("host-before",@"\\controller\project\receipt.json",WorkerPathRole.NewWorkerFile);
        Assert.Throws<InvalidDataException>(()=>Paths(bad).ValidateLayout(Environment.MachineName));
    }
    [Test] public void InaccessibleOutputIsFailureNotAbsenceAndNoOperationStarts() {
        bool started=false;Assert.Throws<UnauthorizedAccessException>(()=>{Paths(Output()).InspectAccess(Environment.MachineName,_=>throw new UnauthorizedAccessException("synthetic read-only parent"));started=true;});Assert.That(started,Is.False);
    }
    [Test] public void MissingOrExclusiveCleanupInputFailsBeforeProbe() {
        bool probed=false;using(var held=new FileStream(input,FileMode.Open,FileAccess.ReadWrite,FileShare.None)) Assert.Throws<IOException>(()=>Paths(Output()).InspectAccess(Environment.MachineName,_=>probed=true));
        File.Delete(input);Assert.Throws<FileNotFoundException>(()=>Paths(Output()).InspectAccess(Environment.MachineName,_=>probed=true));Assert.That(probed,Is.False);
    }
    [Test] public void ChangedInputDigestFailsClosed() {
        File.AppendAllText(input," ");Assert.Throws<InvalidDataException>(()=>Paths(Output()).InspectAccess(Environment.MachineName));
    }
    [Test] public void ExistingOutputIsPreserved() {
        var output=Output();File.WriteAllText(output.Path,"original");Assert.Throws<IOException>(()=>Paths(output).InspectAccess(Environment.MachineName));Assert.That(File.ReadAllText(output.Path),Is.EqualTo("original"));
    }
    [Test] public void DuplicateCaseInsensitiveAndFileAncestorBindingsAreRejected() {
        var output=Output();Assert.Throws<InvalidDataException>(()=>Paths(output,output with{Name="other",Path=output.Path.ToUpperInvariant()}).ValidateLayout(Environment.MachineName));
        Assert.Throws<InvalidDataException>(()=>Paths(output,new("child",output.Path+"/child",WorkerPathRole.NewWorkerFile)).ValidateLayout(Environment.MachineName));
    }
    [Test] public void StartupAndCleanupFailuresAreBothRetainedPrivately() {
        var paths=Paths(Output());var original=new FileNotFoundException("original startup failure");var cleanup=new IOException("cleanup failure");
        var result=paths.RetainTerminal("terminal","startup",null,original,new[]{cleanup});
        Assert.That(result.Persisted,Is.True);Assert.That(result.Diagnostic.PrivatePrimary,Does.Contain("original startup failure"));Assert.That(result.Diagnostic.PrivateCleanupErrors.Single(),Does.Contain("cleanup failure"));
        Assert.That(result.Diagnostic.WorkerPid,Is.EqualTo(Environment.ProcessId));Assert.That(result.Diagnostic.NativeStartUtcTicks,Is.GreaterThan(0));Assert.That(result.Diagnostic.WorkerHost,Is.EqualTo(Environment.MachineName));
        using var json=JsonDocument.Parse(File.ReadAllBytes(Output().Path));Assert.That(json.RootElement.GetProperty("Settlement").GetString(),Does.StartWith("Unconfirmed"));
    }
    [Test] public void DuplicateTerminalDoesNotOverwriteOrLoseOriginalFailure() {
        var paths=Paths(Output());paths.WriteJson("terminal",new{Old=true});byte[] before=File.ReadAllBytes(Output().Path);
        var result=paths.RetainTerminal("terminal","startup",null,new IOException("primary"),new[]{new IOException("cleanup")});
        Assert.That(result.Persisted,Is.False);Assert.That(result.PrivateWriteError,Is.Not.Null);Assert.That(result.Diagnostic.PrivatePrimary,Does.Contain("primary"));Assert.That(result.Diagnostic.PrivateCleanupErrors.Single(),Does.Contain("cleanup"));Assert.That(File.ReadAllBytes(Output().Path),Is.EqualTo(before));
    }
    [Test] public void InaccessibleTerminalRetainsBothFailuresInReturnedDiagnostic() {
        string parent=Path.Combine(root,"not-a-directory");File.WriteAllText(parent,"preserve");
        var paths=Paths(new WorkerPathBinding("terminal",Path.Combine(parent,"terminal.json"),WorkerPathRole.NewWorkerFile));
        var result=paths.RetainTerminal("terminal","startup",null,new IOException("startup"),new[]{new IOException("cleanup")});
        Assert.That(result.Persisted,Is.False);Assert.That(result.Diagnostic.PrivatePrimary,Does.Contain("startup"));Assert.That(result.Diagnostic.PrivateCleanupErrors.Single(),Does.Contain("cleanup"));Assert.That(File.ReadAllText(parent),Is.EqualTo("preserve"));
    }
    [Test] public void ReadOnlyCaptureFactoryKeepsEveryCaptureAndHostReceiptOnWorker() {
        string run=Path.Combine(root,"capture-run");var paths=WorkerExecutionPaths.ReadOnlyCapture(Environment.MachineName,root,run,new[]{pin});
        paths.InspectAccess(Environment.MachineName);paths.WriteJson("host-before",new{Host=Environment.MachineName});
        foreach(string name in new[]{"acquisition","native-inventory","locations","catalogue","root-1","child-2","native-after","release","result"})paths.WriteJson("capture",new{Synthetic=true},name+".json");
        paths.WriteJson("host-after",new{Preserved=true});paths.RetainTerminal("terminal","completed",0,null,[]);
        var manifest=paths.CollectionManifest();Assert.That(manifest.Files.Length,Is.EqualTo(12));Assert.That(manifest.MissingDeclaredFiles,Is.Empty);WorkerExecutionPaths.VerifyCollected(manifest,root);
        Assert.Throws<InvalidDataException>(()=>WorkerExecutionPaths.ReadOnlyCapture(Environment.MachineName,root,@"\\controller\project\run",new[]{pin},@"\\controller\project").ValidateLayout(Environment.MachineName));
    }
    [Test] public void DirectoryWriterRejectsTraversalAndCollectionDetectsChangedOriginal() {
        var paths=Paths(new("capture",Path.Combine(root,"capture"),WorkerPathRole.NewWorkerDirectory),Output());
        Assert.Throws<InvalidDataException>(()=>paths.WriteJson("capture",new{Wrong=true},"../escape.json"));
        paths.WriteJson("capture",new{Native=true},"native.json");paths.WriteJson("terminal",new{Exit=1});
        var manifest=paths.CollectionManifest();Assert.That(manifest.MissingDeclaredFiles,Is.Empty);Assert.That(manifest.Files.Length,Is.EqualTo(2));
        WorkerExecutionPaths.VerifyCollected(manifest,root);File.AppendAllText(Path.Combine(root,"capture","native.json")," ");Assert.Throws<InvalidDataException>(()=>WorkerExecutionPaths.VerifyCollected(manifest,root));
    }
    [Test] public void ManifestDoesNotSuppressMissingTerminalAndRejectsFalseCollectionPaths() {
        var paths=Paths(Output());Assert.That(paths.CollectionManifest().MissingDeclaredFiles,Is.EqualTo(new[]{"terminal"}));
        Assert.Throws<InvalidDataException>(()=>WorkerExecutionPaths.VerifyCollected(paths.CollectionManifest(),root));
        Assert.Throws<InvalidDataException>(()=>WorkerExecutionPaths.VerifyCollected(new(new[]{new WorkerFileReceipt("../escape",0,new('a',64))},[]),root));
    }
}
