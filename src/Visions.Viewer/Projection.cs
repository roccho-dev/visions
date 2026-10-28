using Visions.Core;

namespace Visions.Viewer;

public sealed record Step(string Id, string Source, string CaptureId, long Seq,
    DateTimeOffset At, string Event, string Target, string Role);
public sealed record Transition(string FromId, string ToId, double ElapsedMs);
public sealed record ViewModel(string RuleVersion, string Coverage, IReadOnlyList<Step> Steps,
    IReadOnlyList<Transition> Transitions, IReadOnlyList<Fact> Facts, IReadOnlyList<string> Issues);

public static class Projection
{
    public const string Version = "focus-sequence/1";
    public static ViewModel Build(Snapshot snapshot)
    {
        List<Step> steps = [];
        List<Transition> edges = [];
        List<string> issues = snapshot.Issues.Select(i => $"{i.Code}:{i.Line}").ToList();
        foreach (var group in snapshot.Facts.GroupBy(f => (f.Source, f.CaptureId)))
        {
            long seq = 0;
            bool started = false, ended = false;
            Step? previous = null;
            DateTimeOffset? lastAt = null;
            foreach (var f in group)
            {
                Contract.Validate(f);
                bool adjacent = f.Seq == seq + 1;
                if (!adjacent) { issues.Add("sequence_gap:" + f.Id); previous = null; }
                if (f.Seq <= seq) { issues.Add("sequence_reversed:" + f.Id); continue; }
                seq = f.Seq;
                if (lastAt is not null && f.At < lastAt)
                { issues.Add("clock_reversed:" + f.Id); previous = null; }
                lastAt = f.At;
                if (f.Kind == "capture")
                {
                    previous = null;
                    var phase = Contract.Text(f.Data, "phase");
                    if (phase == "start")
                    {
                        if (started || ended) issues.Add("boundary_conflict:" + f.Id);
                        else started = true;
                    }
                    else { if (!started || ended) issues.Add("boundary_conflict:" + f.Id); ended = true; }
                    continue;
                }
                if (!started || ended) { issues.Add("outside_capture:" + f.Id); previous = null; continue; }
                if (f.Kind != "observation") { previous = null; continue; }
                var ev = Contract.Text(f.Data, "event");
                if (ev == "gap") { issues.Add("observed_gap:" + f.Id); previous = null; continue; }
                var step = new Step(f.Id, f.Source, f.CaptureId, f.Seq, f.At, ev,
                    Contract.Text(f.Data, "target"), Contract.Text(f.Data, "role"));
                steps.Add(step);
                if (previous is not null && f.At >= previous.At)
                    edges.Add(new(previous.Id, step.Id, (f.At - previous.At).TotalMilliseconds));
                previous = step;
            }
            if (!started) issues.Add("missing_start:" + group.Key.CaptureId);
            if (!ended) issues.Add("open_or_incomplete:" + group.Key.CaptureId);
        }
        return new(Version, "partial — UI events are not a complete business process", steps, edges,
            snapshot.Facts, issues.Distinct().Order(StringComparer.Ordinal).ToArray());
    }
}
