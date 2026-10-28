using System.Text;
using System.Text.Json;

namespace Visions.Core;

public sealed class CaptureWriter : IDisposable
{
    private readonly object gate = new();
    private readonly FileStream file;
    private readonly StreamWriter writer;
    private readonly string scope;
    private readonly string source = Contract.Id();
    private long seq;
    private bool ended;
    public string CaptureId { get; } = Contract.Id();
    public string Path { get; }

    public CaptureWriter(string directory, string scope)
    {
        this.scope = scope;
        // Validate before creating any file.
        Contract.Validate(new Fact(1, Contract.Id(), "capture", DateTimeOffset.UtcNow, source, 1,
            CaptureId, Boundary("start", "")));
        Directory.CreateDirectory(directory);
        Path = System.IO.Path.Combine(directory, CaptureId + ".jsonl");
        file = new FileStream(Path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        writer = new StreamWriter(file, new UTF8Encoding(false), leaveOpen: true) { NewLine = "\n" };
        Add("capture", Boundary("start", ""));
    }
    private JsonElement Boundary(string phase, string reason) => Contract.Data(new
    { phase, scope, coverage = scope == "selected-process" ? "partial" : "bounded", reason });
    private void Add(string kind, JsonElement data)
    {
        var fact = new Fact(1, Contract.Id(), kind, DateTimeOffset.UtcNow, source, seq + 1, CaptureId, data);
        var json = Contract.Encode(fact);
        if (kind != "capture" && (seq >= Contract.MaxRecords - 1 || file.Position > Contract.MaxBytes - 8192))
            throw new IOException("capture_limit");
        writer.WriteLine(json);
        writer.Flush();
        file.Flush(true);
        seq++;
    }
    public void Observe(string target, string role)
    {
        lock (gate)
        {
            if (ended) return;
            Add("observation", Contract.Data(new { @event = "ui.focus", target, role }));
        }
    }
    public void Gap(string reason)
    {
        lock (gate)
        {
            if (!ended) Add("observation", Contract.Data(new { @event = "gap", reason }));
        }
    }
    public void Artifact(string reference, string digest, long bytes)
    {
        lock (gate)
        {
            if (ended) throw new InvalidOperationException("capture_closed");
            Add("artifact", Contract.Data(new { @ref = reference, type = "export-file", digest, bytes }));
        }
    }
    public void End(string reason = "user")
    {
        lock (gate)
        {
            if (ended) return;
            Add("capture", Boundary("end", reason));
            ended = true;
        }
    }
    public void Dispose()
    {
        lock (gate) { ended = true; writer.Dispose(); file.Dispose(); }
        // No manufactured end record on a crash or failed operation.
    }
}

public sealed record ReadIssue(int Line, string Code);
public sealed record Snapshot(IReadOnlyList<Fact> Facts, IReadOnlyList<ReadIssue> Issues);

public static class Journal
{
    public static Snapshot Read(string? path)
    {
        if (path is null || !File.Exists(path)) return new([], [new(0, "no_capture")]);
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length > Contract.MaxBytes) return new([], [new(0, "file_limit")]);
            var bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
            return Parse(new UTF8Encoding(false, true).GetString(bytes));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DecoderFallbackException)
        { return new([], [new(0, "read_unavailable")]); }
    }
    public static Snapshot Parse(string text)
    {
        List<Fact> facts = [];
        List<ReadIssue> issues = [];
        HashSet<string> ids = [];
        if (Encoding.UTF8.GetByteCount(text) > Contract.MaxBytes) return new([], [new(0, "file_limit")]);
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i == lines.Length - 1)
            {
                if (lines[i].Length != 0) issues.Add(new(i + 1, "incomplete_tail"));
                break;
            }
            if (facts.Count >= Contract.MaxRecords) { issues.Add(new(i + 1, "record_limit")); break; }
            try
            {
                var fact = Contract.Parse(lines[i].TrimEnd('\r'));
                if (!ids.Add(fact.Id)) throw new InvalidDataException("duplicate_id");
                facts.Add(fact);
            }
            catch (Exception e) when (e is JsonException or InvalidDataException or ArgumentException)
            {
                issues.Add(new(i + 1, "invalid_record"));
                break; // Never bridge a rejected row or expose its untrusted text.
            }
        }
        return new(facts, issues);
    }
}
