using System.Diagnostics;
using Visions.Core;
using Visions.Viewer;

namespace Visions.Windows;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 4 && args[0] == "--record" && int.TryParse(args[1], out var pid) && long.TryParse(args[2], out var ticks))
            return Observer.Run(pid, ticks, args[3]);
        if (args.Length == 2 && args[0] == "--smoke")
        {
            try { Smoke(args[1]).GetAwaiter().GetResult(); return 0; }
            catch { return 2; }
        }
        if (args.Length != 0) return 2;
        ApplicationConfiguration.Initialize();
        Application.Run(new Launcher());
        return 0;
    }
    internal static ProcessStartInfo Child(params string[] args)
    {
        var info = new ProcessStartInfo(Environment.ProcessPath!)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        if (System.IO.Path.GetFileNameWithoutExtension(info.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add(typeof(Program).Assembly.Location);
        foreach (var arg in args) info.ArgumentList.Add(arg);
        return info;
    }
    private static async Task Smoke(string output)
    {
        Directory.CreateDirectory(output);
        var path = Demo.Create(System.IO.Path.Combine(output, "captures"));
        await using var viewer = await LocalViewer.StartAsync(() => path);
        using var client = new HttpClient();
        var model = await client.GetStringAsync(viewer.Url + "model");
        if (!model.Contains(Projection.Version, StringComparison.Ordinal)) throw new InvalidDataException("smoke_failed");
        await File.WriteAllTextAsync(System.IO.Path.Combine(output, "smoke-ok.txt"), "published exe: local viewer and journal passed\n");
    }
}
