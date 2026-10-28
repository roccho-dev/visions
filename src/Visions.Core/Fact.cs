using System.Text.Json;
using System.Text.Json.Serialization;

namespace Visions.Core;

public sealed record Fact(int V, string Id, string Kind, DateTimeOffset At,
    string Source, long Seq, string CaptureId, JsonElement Data);

// A closed vocabulary is intentional: no free-form UI names, paths or values enter the journal.
public static class Contract
{
    public const int MaxBytes = 4 * 1024 * 1024;
    public const int MaxRecords = 5000;
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8
    };
    public static string Id() => Guid.NewGuid().ToString("N");
    public static JsonElement Data(object value) => JsonSerializer.SerializeToElement(value, Json);
    public static bool Hex(string? text, int length) => text is not null && text.Length == length &&
        text.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    public static string Text(JsonElement data, string key) => data.TryGetProperty(key, out var p) &&
        p.ValueKind == JsonValueKind.String ? p.GetString()! : "";
    private static void Require(bool valid) { if (!valid) throw new InvalidDataException("contract_rejected"); }
    private static void Keys(JsonElement data, params string[] keys)
    {
        Require(data.ValueKind == JsonValueKind.Object);
        var names = data.EnumerateObject().Select(p => p.Name).ToArray();
        Require(names.Length == names.Distinct().Count() && names.All(keys.Contains));
    }
    public static Fact Parse(string json)
    {
        Require(json.Length <= 4096);
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
        Keys(doc.RootElement, "v", "id", "kind", "at", "source", "seq", "capture_id", "data");
        Require(doc.RootElement.EnumerateObject().Count() == 8);
        var fact = JsonSerializer.Deserialize<Fact>(json, Json) ?? throw new InvalidDataException("contract_rejected");
        Validate(fact);
        return fact;
    }
    public static void Validate(Fact f)
    {
        Require(f.V == 1 && Hex(f.Id, 32) && Hex(f.Source, 32) && Hex(f.CaptureId, 32));
        Require(f.Seq > 0 && f.At.Offset == TimeSpan.Zero && f.At != default);
        var d = f.Data;
        switch (f.Kind)
        {
            case "capture":
                Keys(d, "phase", "scope", "coverage", "reason");
                Require(Text(d, "phase") is "start" or "end");
                Require(Text(d, "scope") is "selected-process" or "selected-file" or "synthetic");
                Require(Text(d, "coverage") is "partial" or "bounded");
                Require(Text(d, "reason") is "" or "user" or "completed" or "limit" or "target-exited" or "unavailable");
                break;
            case "observation":
                Keys(d, "event", "target", "role", "reason");
                var ev = Text(d, "event");
                Require(ev is "ui.focus" or "gap");
                if (ev == "gap")
                {
                    Require(Text(d, "reason") is "unavailable" or "limit");
                    Require(Text(d, "target") == "" && Text(d, "role") == "");
                }
                else
                {
                    Require(Hex(Text(d, "target"), 32));
                    Require(Text(d, "role") is "button" or "input" or "list" or "grid" or "window" or "other");
                    Require(Text(d, "reason") == "");
                }
                break;
            case "artifact":
                Keys(d, "ref", "type", "digest", "bytes");
                Require(Hex(Text(d, "ref"), 32) && Text(d, "type") == "export-file" && Hex(Text(d, "digest"), 64));
                Require(d.TryGetProperty("bytes", out var n) && n.TryGetInt64(out var bytes) && bytes >= 0 && bytes <= FileExtractor.MaxBytes);
                break;
            default: throw new InvalidDataException("contract_rejected");
        }
    }
    public static string Encode(Fact f) { Validate(f); return JsonSerializer.Serialize(f, Json); }
}
