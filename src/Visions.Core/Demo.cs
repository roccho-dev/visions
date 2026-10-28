namespace Visions.Core;

public static class Demo
{
    public static string Create(string directory)
    {
        using var writer = new CaptureWriter(directory, "synthetic");
        var input = Contract.Id();
        var button = Contract.Id();
        var grid = Contract.Id();
        writer.Observe(input, "input");
        writer.Observe(button, "button");
        writer.Observe(grid, "grid");
        writer.Observe(input, "input");
        writer.Gap("unavailable");
        writer.Observe(button, "button");
        writer.End("completed");
        return writer.Path;
    }
}
