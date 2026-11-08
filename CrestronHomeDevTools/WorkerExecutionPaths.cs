// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License.
using System.Security.Cryptography;
using System.Text.Json;

namespace CrestronHomeDevTools;

public enum WorkerPathRole { WorkerInput, ControllerSharedInput, NewWorkerFile, NewWorkerDirectory, ControllerSharedReadDirectory, ControllerSharedSignalFile, WorkerReadDirectory }
public sealed record WorkerPathBinding(string Name, string Path, WorkerPathRole Role, long? Length = null, string? Sha256 = null) {
    // A pinned tool/private-store input may be outside mutable worker output storage. This never permits output there.
    public string? WorkerInputRoot { get; init; }
}
public sealed record WorkerFileReceipt(string RelativePath, long Length, string Sha256);
public sealed record WorkerCollectionManifest(WorkerFileReceipt[] Files, string[] MissingDeclaredFiles);
public sealed record WorkerTerminalDiagnostic(string Phase, int? ExitCode, string? PrimaryType, string? PrivatePrimary,
    string[] PrivateCleanupErrors, string Settlement = "Unconfirmed; verify owned process and resources independently") {
    public string WorkerHost { get; init; } = "";
    public int WorkerPid { get; init; }
    public long NativeStartUtcTicks { get; init; }
    public DateTimeOffset RecordedUtc { get; init; }
}
public sealed record WorkerTerminalRetention(WorkerTerminalDiagnostic Diagnostic, bool Persisted, string? PrivateWriteError);

/// <summary>Host-specific file access and evidence contract. Does not launch, acquire resources, connect or mark tests passed.</summary>
public sealed class WorkerExecutionPaths(string workerHost, string workerRoot, string? controllerSharedRoot,
    IReadOnlyList<WorkerPathBinding> bindings)
{
    readonly IReadOnlyList<WorkerPathBinding> entries = bindings?.ToArray() ?? throw new ArgumentNullException(nameof(bindings));
    public static WorkerExecutionPaths ReadOnlyCapture(string workerHost, string workerRoot, string runDirectory,
        IReadOnlyList<WorkerPathBinding> inputs, string? controllerSharedRoot = null) {
        if (inputs.Any(x => x.Role is not (WorkerPathRole.WorkerInput or WorkerPathRole.ControllerSharedInput)))
            throw new InvalidDataException("Capture factory accepts immutable inputs only.");
        return new(workerHost, workerRoot, controllerSharedRoot, inputs.Concat(new[] {
            new WorkerPathBinding("host-before",Path.Combine(runDirectory,"host-before.original.json"),WorkerPathRole.NewWorkerFile),
            new WorkerPathBinding("host-after",Path.Combine(runDirectory,"host-after.original.json"),WorkerPathRole.NewWorkerFile),
            new WorkerPathBinding("capture",Path.Combine(runDirectory,"capture"),WorkerPathRole.NewWorkerDirectory),
            new WorkerPathBinding("terminal",Path.Combine(runDirectory,"terminal.private.original.json"),WorkerPathRole.NewWorkerFile)
        }).ToArray());
    }
    static readonly StringComparison Comparison = StringComparison.OrdinalIgnoreCase;
    static bool Shared(string p) => p.Replace('/', '\\').StartsWith(@"\\", StringComparison.Ordinal);
    static string Full(string p) {
        if (string.IsNullOrWhiteSpace(p) || !Path.IsPathFullyQualified(p) ||
            p.Replace('\\','/').Split('/').Any(x => x is "." or "..") || p.StartsWith(@"\\?\", StringComparison.Ordinal))
            throw new InvalidDataException("An ordinary absolute path without traversal is required.");
        return Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
    static bool Within(string p, string root) => p.StartsWith(root + Path.DirectorySeparatorChar, Comparison);
    static void OrdinaryAncestors(string p) {
        for (string? at = Path.GetFullPath(p); at != null; at = Path.GetDirectoryName(at)) {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(at); }
            catch (FileNotFoundException) { continue; }
            catch (DirectoryNotFoundException) { continue; }
            // Access errors are deliberately not classified as absence.
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Reparse path refused.");
        }
    }
    WorkerPathBinding Binding(string name) => entries.SingleOrDefault(x => x.Name == name)
        ?? throw new InvalidDataException("Undeclared worker path.");
    public void ValidateLayout(string actualWorker) {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows worker paths required.");
        if (string.IsNullOrWhiteSpace(workerHost) || !workerHost.Equals(actualWorker, Comparison)) throw new InvalidDataException("Wrong execution host.");
        string root = Full(workerRoot);
        if (Shared(root) || Path.GetPathRoot(root)?.Length != 3) throw new InvalidDataException("Worker root must be local drive storage.");
        string? shared = controllerSharedRoot == null ? null : Full(controllerSharedRoot);
        if (shared != null && !Shared(shared)) throw new InvalidDataException("Controller inputs require an explicit UNC root.");
        if (entries.Count == 0 || entries.Any(x => x == null || string.IsNullOrWhiteSpace(x.Name)) ||
            entries.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Count)
            throw new InvalidDataException("Complete unique named path bindings required.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in entries) {
            string p = Full(b.Path);
            if (!Enum.IsDefined(b.Role) || !seen.Add(p)) throw new InvalidDataException("Unknown role or duplicate path.");
            bool input = b.Role is WorkerPathRole.WorkerInput or WorkerPathRole.ControllerSharedInput;
            bool channel = b.Role is WorkerPathRole.ControllerSharedReadDirectory or WorkerPathRole.ControllerSharedSignalFile;
            string allowed = b.Role == WorkerPathRole.ControllerSharedInput || channel ? shared ?? throw new InvalidDataException("Shared input root missing.") : root;
            if (b.WorkerInputRoot != null) {
                string extra = Full(b.WorkerInputRoot);
                if (b.Role != WorkerPathRole.WorkerInput || Shared(extra) || Path.GetPathRoot(extra)?.Length != 3)
                    throw new InvalidDataException("Additional local root is for pinned worker inputs only.");
                allowed = extra;
            }
            if (!Within(p, allowed) || (b.Role != WorkerPathRole.ControllerSharedInput && !channel && Shared(p))) throw new InvalidDataException("Wrong-host or escaped path binding.");
            if (input && (b.Length is < 0 || b.Sha256?.Length != 64 || !b.Sha256.All(char.IsAsciiHexDigit)))
                throw new InvalidDataException("Every immutable input, including cleanup inputs, needs its exact digest and any declared length.");
            if (!input && (b.Length != null || b.Sha256 != null)) throw new InvalidDataException("New output cannot be an input pin.");
            if (entries.Any(other => other != b && other.Role is not (WorkerPathRole.NewWorkerDirectory or WorkerPathRole.ControllerSharedReadDirectory or WorkerPathRole.WorkerReadDirectory) && Within(p, Full(other.Path))))
                throw new InvalidDataException("A file binding cannot be a path ancestor.");
        }
    }
    public byte[] ReadInput(string name) {
        ValidateLayout(Environment.MachineName);
        var b = Binding(name);
        if (b.Role is not (WorkerPathRole.WorkerInput or WorkerPathRole.ControllerSharedInput)) throw new InvalidDataException("Not an input.");
        OrdinaryAncestors(b.Path);
        byte[] bytes = File.ReadAllBytes(b.Path);
        if ((b.Length.HasValue && bytes.LongLength != b.Length) || !Convert.ToHexString(SHA256.HashData(bytes)).Equals(b.Sha256, Comparison)) throw new InvalidDataException("Pinned input changed.");
        return bytes;
    }
    /// <summary>Check actual account access before acquisition. Creates only uniquely named temporary probes and deletes only its own probes.</summary>
    public void InspectAccess(string actualWorker) => InspectAccess(actualWorker, ProbeWrite);
    internal void InspectAccess(string actualWorker, Action<string> probeWrite) {
        ValidateLayout(actualWorker);
        var parents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in entries) {
            if (b.Role is WorkerPathRole.WorkerInput or WorkerPathRole.ControllerSharedInput) { ReadInput(b.Name); continue; }
            if (b.Role is WorkerPathRole.ControllerSharedReadDirectory or WorkerPathRole.ControllerSharedSignalFile or WorkerPathRole.WorkerReadDirectory) {
                OrdinaryAncestors(b.Path);
                string channelParent = b.Role != WorkerPathRole.ControllerSharedSignalFile ? b.Path : Path.GetDirectoryName(b.Path)!;
                _ = Directory.GetFileSystemEntries(channelParent);
                if (b.Role == WorkerPathRole.ControllerSharedSignalFile && File.Exists(b.Path)) { using var signal = File.OpenRead(b.Path); }
                continue; // Dynamic messages remain subject to the existing scoped human/cancellation protocol. No worker writes to the controller.
            }
            OrdinaryAncestors(b.Path);
            // Enumerate accessible ancestors instead of treating Exists(false) as absence.
            string parent = Path.GetDirectoryName(Full(b.Path))!;
            while (true) {
                try { _ = Directory.GetFileSystemEntries(parent); break; }
                catch (DirectoryNotFoundException) { string? next = Path.GetDirectoryName(parent); if (next == null) throw; parent = next; }
            }
            if (File.Exists(b.Path) || Directory.Exists(b.Path)) throw new IOException("New worker output already exists.");
            parents.Add(parent);
        }
        foreach (string parent in parents) probeWrite(parent);
    }
    static void ProbeWrite(string parent) {
        string path = Path.Combine(parent, ".worker-access-" + Guid.NewGuid().ToString("N"));
        bool created = false;
        try {
            using (var f = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None)) {
                created = true; byte[] value = Guid.NewGuid().ToByteArray(); f.Write(value); f.Flush(true); f.Position = 0;
                byte[] read = new byte[value.Length]; f.ReadExactly(read);
                if (!value.AsSpan().SequenceEqual(read)) throw new IOException("Worker write/readback differed.");
            }
        } finally { if (created) File.Delete(path); }
    }
    string Output(string name, string? relative) {
        var b = Binding(name); string p = Full(b.Path);
        if (relative == null && b.Role == WorkerPathRole.NewWorkerFile) return p;
        if (relative == null || b.Role != WorkerPathRole.NewWorkerDirectory || Path.IsPathRooted(relative)) throw new InvalidDataException("Exact output file or relative directory entry required.");
        string child = Full(Path.Combine(p, relative));
        if (!Within(child, p)) throw new InvalidDataException("Output escaped its declared directory.");
        return child;
    }
    /// <summary>Write private originals on the worker. Never writes through a controller UNC, overwrites evidence or changes ACLs.</summary>
    public void WriteJson<T>(string name, T value, string? relative = null) {
        ValidateLayout(Environment.MachineName); string path = Output(name, relative); OrdinaryAncestors(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); OrdinaryAncestors(path);
        using var f = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        JsonSerializer.Serialize(f, value, new JsonSerializerOptions { WriteIndented = true }); f.Flush(true);
    }
    /// <summary>Retain both primary and cleanup errors; a receipt-write failure cannot replace either. Private chains must not be printed publicly.</summary>
    public WorkerTerminalRetention RetainTerminal(string name, string phase, int? exitCode, Exception? primary, IEnumerable<Exception> cleanup) {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var value = new WorkerTerminalDiagnostic(phase, exitCode, primary?.GetType().FullName, primary?.ToString(), cleanup.Select(x => x.ToString()).ToArray()) {
            WorkerHost=Environment.MachineName,WorkerPid=process.Id,NativeStartUtcTicks=process.StartTime.ToUniversalTime().Ticks,RecordedUtc=DateTimeOffset.UtcNow
        };
        try { WriteJson(name, value); return new(value, true, null); }
        catch (Exception error) when (error is not OutOfMemoryException) { return new(value, false, error.ToString()); }
    }
    /// <summary>Manifest for the existing authenticated original-file collection route. Hashes prove bytes, not test success or release.</summary>
    public WorkerCollectionManifest CollectionManifest() {
        ValidateLayout(Environment.MachineName); var files = new Dictionary<string, WorkerFileReceipt>(StringComparer.OrdinalIgnoreCase); var missing = new List<string>();
        foreach (var b in entries.Where(x => x.Role is WorkerPathRole.NewWorkerFile or WorkerPathRole.NewWorkerDirectory)) {
            OrdinaryAncestors(b.Path);
            if (b.Role == WorkerPathRole.NewWorkerFile) { if (File.Exists(b.Path)) Add(b.Path); else missing.Add(b.Name); }
            else if (Directory.Exists(b.Path)) Walk(b.Path);
        }
        return new(files.Values.OrderBy(x => x.RelativePath, StringComparer.Ordinal).ToArray(), missing.ToArray());
        void Walk(string dir) { OrdinaryAncestors(dir); foreach (var p in Directory.GetFileSystemEntries(dir)) { OrdinaryAncestors(p); if ((File.GetAttributes(p) & FileAttributes.Directory) != 0) Walk(p); else Add(p); } }
        void Add(string p) { string rel = Path.GetRelativePath(Full(workerRoot), Full(p)).Replace('\\','/'); byte[] bytes = File.ReadAllBytes(p); files[rel] = new(rel, bytes.LongLength, Convert.ToHexString(SHA256.HashData(bytes))); }
    }
    public static void VerifyCollected(WorkerCollectionManifest manifest, string collectionRoot, bool requireComplete = true) {
        if (requireComplete && manifest.MissingDeclaredFiles.Length != 0) throw new InvalidDataException("Collection retains missing declared outputs; completeness is unconfirmed.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); string root = Full(collectionRoot); OrdinaryAncestors(root);
        foreach (var f in manifest.Files) {
            if (Path.IsPathRooted(f.RelativePath)) throw new InvalidDataException("Collection path must be relative.");
            string path = Full(Path.Combine(root, f.RelativePath));
            if (!Within(path, root) || !seen.Add(path)) throw new InvalidDataException("Duplicate or escaped collection path.");
            OrdinaryAncestors(path); byte[] bytes = File.ReadAllBytes(path);
            if (bytes.LongLength != f.Length || !Convert.ToHexString(SHA256.HashData(bytes)).Equals(f.Sha256, Comparison)) throw new InvalidDataException("Collected original changed.");
        }
    }
}
