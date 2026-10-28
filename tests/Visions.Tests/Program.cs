using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Visions.Core;
using Visions.Viewer;

var passed = new List<string>();
var root = Path.Combine(Path.GetTempPath(), "visions-tests-" + Contract.Id());
Directory.CreateDirectory(root);
void Check(string name, bool condition)
{
    if (!condition) throw new Exception("FAILED: " + name);
    passed.Add(name); Console.WriteLine("PASS " + name);
}
void Reject(string name, Action action)
{
    try { action(); } catch (Exception e) when (e is InvalidDataException or JsonException or IOException)
    { Check(name, true); return; }
    throw new Exception("FAILED (accepted): " + name);
}
string Id(int n) => n.ToString("x32");
Fact Make(int seq, string kind, object data, int capture = 200, int source = 100) =>
    new(1, Id(capture * 1000000 + source * 1000 + seq), kind, DateTimeOffset.Parse("2026-01-01T00:00:00Z").AddSeconds(seq),
        Id(source), seq, Id(capture), Contract.Data(data));
Fact Start(int capture = 200, int source = 100) => Make(1, "capture", new { phase = "start", scope = "synthetic", coverage = "bounded" }, capture, source);
Fact Focus(int seq, int target = 400, int capture = 200, int source = 100) => Make(seq, "observation", new { @event = "ui.focus", target = Id(target), role = "button" }, capture, source);
Fact End(int seq, int capture = 200, int source = 100) => Make(seq, "capture", new { phase = "end", scope = "synthetic", coverage = "bounded", reason = "completed" }, capture, source);
string Lines(params Fact[] facts) => string.Join("\n", facts.Select(Contract.Encode)) + "\n";
ViewModel View(params Fact[] facts) => Projection.Build(Journal.Parse(Lines(facts)));
try
{
    var start = Start();
    Check("contract round trip", Contract.Parse(Contract.Encode(start)).Id == start.Id);
    Reject("reject version", () => Contract.Encode(start with { V = 2 }));
    Reject("reject source text", () => Contract.Encode(start with { Source = "SENSITIVE_MARKER" }));
    Reject("reject target text", () => Contract.Encode(Focus(2) with { Data = Contract.Data(new { @event = "ui.focus", target = "PATIENT_NAME", role = "button" }) }));
    Reject("reject unknown data", () => Contract.Encode(start with { Data = Contract.Data(new { phase = "start", scope = "synthetic", coverage = "bounded", name = "SENSITIVE_MARKER" }) }));
    Reject("reject wrong data type", () => Contract.Encode(Focus(2) with { Data = Contract.Data(new { @event = "ui.focus", target = Id(400), role = "button", reason = 123456789 }) }));
    Reject("reject timezone ambiguity", () => Contract.Encode(start with { At = start.At.ToOffset(TimeSpan.FromHours(9)) }));
    Reject("reject zero sequence", () => Contract.Encode(start with { Seq = 0 }));
    Reject("reject unknown event", () => Contract.Encode(Focus(2) with { Data = Contract.Data(new { @event = "key.text", target = Id(400), role = "input" }) }));
    var json = Contract.Encode(start);
    Reject("reject unknown envelope", () => Contract.Parse(json[..^1] + ",\"password\":\"SECRET\"}"));
    Reject("reject duplicate keys", () => Contract.Parse(json[..^1] + ",\"id\":\"" + Id(3) + "\"}"));
    var read = Journal.Parse(Lines(Start(), Focus(2), Focus(3, 401), End(4)));
    Check("read valid journal", read.Facts.Count == 4 && read.Issues.Count == 0);
    var torn = Journal.Parse(Lines(Start(), Focus(2)) + Contract.Encode(Focus(3)));
    Check("incomplete tail excluded", torn.Facts.Count == 2 && torn.Issues.Single().Code == "incomplete_tail");
    var invalid = Journal.Parse(Lines(Start()) + "{\"private\":\"SECRET\"}\n" + Lines(Focus(3)));
    Check("invalid middle stops replay", invalid.Facts.Count == 1 && invalid.Issues.Single().Code == "invalid_record");
    Check("invalid payload absent from projection", !JsonSerializer.Serialize(Projection.Build(invalid), Contract.Json).Contains("SECRET"));
    Check("duplicate id rejected", Journal.Parse(Lines(Start(), Start())).Issues.Count == 1);
    Check("file size bounded", Journal.Parse(new string('x', Contract.MaxBytes + 1)).Issues.Single().Code == "file_limit");
    var view = Projection.Build(read);
    Check("one adjacent transition", view.Steps.Count == 2 && view.Transitions.Count == 1);
    Check("elapsed is timestamp difference", view.Transitions.Single().ElapsedMs == 1000);
    Check("evidence ids resolve", view.Steps.All(s => read.Facts.Any(f => f.Id == s.Id)) && view.Transitions.All(t => view.Steps.Any(s => s.Id == t.FromId) && view.Steps.Any(s => s.Id == t.ToId)));
    Check("deterministic projection", JsonSerializer.Serialize(view, Contract.Json) == JsonSerializer.Serialize(Projection.Build(read), Contract.Json));
    var gap = View(Start(), Focus(2), Focus(4), End(5));
    Check("no edge across sequence gap", gap.Transitions.Count == 0 && gap.Issues.Any(i => i.StartsWith("sequence_gap:")));
    var reported = View(Start(), Focus(2), Make(3, "observation", new { @event = "gap", reason = "unavailable" }), Focus(4), End(5));
    Check("no edge across explicit gap", reported.Transitions.Count == 0);
    var clock = View(Start(), Focus(2), Focus(3) with { At = Start().At }, End(4));
    Check("clock regression is unknown", clock.Transitions.Count == 0 && clock.Issues.Any(i => i.StartsWith("clock_reversed:")));
    var captures = View(Start(), Focus(2), End(3), Start(201), Focus(2, capture: 201), End(3, 201));
    Check("no edge across captures", captures.Steps.Count == 2 && captures.Transitions.Count == 0);
    var sources = View(Start(), Focus(2), Start(source: 101), Focus(2, source: 101));
    Check("no edge across sources", sources.Steps.Count == 2 && sources.Transitions.Count == 0);
    Check("missing end remains incomplete", View(Start(), Focus(2)).Issues.Any(i => i.StartsWith("open_or_incomplete:")));
    Check("no invented start", View(Focus(2), End(3)).Steps.Count == 0);
    Check("no step after end", View(Start(), Focus(2), End(3), Focus(4)).Steps.Count == 1);
    Check("coverage never claims completeness", view.Coverage.StartsWith("partial"));
    var demo = Demo.Create(Path.Combine(root, "captures"));
    Check("demo replay", Projection.Build(Journal.Read(demo)).Steps.Count == 5);
    using (var writer = new CaptureWriter(Path.Combine(root, "captures"), "synthetic"))
    {
        writer.Observe(Id(1), "button"); writer.End(); writer.End(); writer.Observe(Id(2), "input");
        Check("end idempotent and callbacks ignored", Journal.Read(writer.Path).Facts.Count == 3);
    }
    var input = Path.Combine(root, "private-filename.csv");
    File.WriteAllText(input, "private-column\nSENSITIVE_MARKER\n");
    var imported = FileExtractor.Extract(input, root);
    var artifact = Journal.Read(imported).Facts.Single(f => f.Kind == "artifact");
    var body = File.ReadAllBytes(Path.Combine(root, "artifacts", Contract.Text(artifact.Data, "ref") + ".bin"));
    Check("file extraction preserves selected bytes", body.SequenceEqual(File.ReadAllBytes(input)));
    Check("digest verifies stored bytes", Contract.Text(artifact.Data, "digest") == Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant());
    Check("facts omit content and source filename", !File.ReadAllText(imported).Contains("SENSITIVE_MARKER") && !File.ReadAllText(imported).Contains("private-filename"));
    Reject("reject network path before IO", () => FileExtractor.Extract("//server/share/file.csv", root));
    Reject("reject relative path before IO", () => FileExtractor.Extract("relative.csv", root));
    var big = Path.Combine(root, "large.csv");
    using (var file = File.Create(big)) file.SetLength(FileExtractor.MaxBytes + 1);
    var before = Directory.GetFiles(Path.Combine(root, "artifacts")).Length;
    Reject("reject oversized artifact", () => FileExtractor.Extract(big, root));
    Check("failure leaves no copied artifact", Directory.GetFiles(Path.Combine(root, "artifacts")).Length == before);
    var env = "Kestrel__Endpoints__External__Url";
    var oldEnvironment = Environment.GetEnvironmentVariable(env);
    Environment.SetEnvironmentVariable(env, "http://0.0.0.0:40177");
    try
    {
        await using var server = await LocalViewer.StartAsync(() => imported);
        using var client = new HttpClient();
        Check("host environment cannot add listeners", server.Url.StartsWith("http://127.0.0.1:", StringComparison.Ordinal));
        var page = await client.GetAsync(server.Url);
        Check("local viewer opens", page.StatusCode == HttpStatusCode.OK);
        var lensPage = await client.GetStringAsync(server.Url + "process-lens.html");
        var worldFixture = await client.GetStringAsync(server.Url + "process-world.json");
        var lensScript = await client.GetStringAsync(server.Url + "lens.mjs");
        Check("lens endpoints are bundled on loopback", lensPage.Contains("process-graph") && lensScript.Contains("projectWorld"));
        Check("synthetic World is explicitly labeled", worldFixture.Contains("synthetic-fixture") && worldFixture.Contains("authority"));
        Check("CSP blocks embedded foreign content", page.Headers.GetValues("Content-Security-Policy").Single().Contains("frame-ancestors 'none'"));
        var model = await client.GetStringAsync(server.Url + "model");
        Check("viewer never serves extracted body", !model.Contains("SENSITIVE_MARKER") && model.Contains("artifact"));
        Check("capability URL required", (await client.GetAsync(new Uri(server.Url).GetLeftPart(UriPartial.Authority) + "/")).StatusCode == HttpStatusCode.NotFound);
        Check("viewer is read only", (await client.PostAsync(server.Url + "model", null)).StatusCode == HttpStatusCode.MethodNotAllowed);
        using var cross = new HttpRequestMessage(HttpMethod.Get, server.Url + "model"); cross.Headers.Add("Origin", "https://example.invalid");
        Check("cross origin rejected", (await client.SendAsync(cross)).StatusCode == HttpStatusCode.Forbidden);
        using var rebound = new HttpRequestMessage(HttpMethod.Get, server.Url); rebound.Headers.Host = "example.invalid";
        Check("DNS rebinding host rejected", (await client.SendAsync(rebound)).StatusCode == HttpStatusCode.Forbidden);
        using var foreign = new HttpRequestMessage(HttpMethod.Get, server.Url); foreign.Headers.Add("Sec-Fetch-Site", "cross-site");
        Check("cross site navigation rejected", (await client.SendAsync(foreign)).StatusCode == HttpStatusCode.Forbidden);
        Check("local model does not cache", (await client.GetAsync(server.Url + "model")).Headers.CacheControl?.NoStore == true);
    }
    finally { Environment.SetEnvironmentVariable(env, oldEnvironment); }
    Console.WriteLine($"RESULT {passed.Count}/{passed.Count} PASS");
    return 0;
}
finally { Directory.Delete(root, true); }
