using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using Visions.Core;

namespace Visions.Windows;

// Runs in an owned child process. A blocked third-party UIA provider cannot freeze the launcher.
internal static class Observer
{
    public static int Run(int pid, long startTicks, string directory)
    {
        using var cancellation = new CancellationTokenSource();
        _ = Task.Run(() => { Console.In.ReadLine(); cancellation.Cancel(); });
        var result = 2;
        var thread = new Thread(() =>
        {
            try { result = Capture(pid, startTicks, directory, cancellation.Token); }
            catch { result = 2; } // Never print provider exception text, titles or paths.
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        return thread.Join(TimeSpan.FromMinutes(16)) ? result : 3;
    }
    private static int Capture(int pid, long startTicks, string directory, CancellationToken stop)
    {
        using var target = Process.GetProcessById(pid);
        using var self = Process.GetCurrentProcess();
        if (target.StartTime.ToUniversalTime().Ticks != startTicks || target.SessionId != self.SessionId)
            return 2;
        using var writer = new CaptureWriter(directory, "selected-process");
        using var automation = new UIA3Automation
        {
            TransactionTimeout = TimeSpan.FromSeconds(2),
            ConnectionTimeout = TimeSpan.FromSeconds(2)
        };
        var key = RandomNumberGenerator.GetBytes(32);
        var gate = new object();
        var failed = false;
        var outside = true;
        void Observe(AutomationElement element)
        {
            lock (gate)
            {
                if (failed || stop.IsCancellationRequested) return;
                try
                {
                    if (element.Properties.ProcessId.Value != pid)
                    {
                        if (!outside) writer.Gap("unavailable");
                        outside = true;
                        return;
                    }
                    outside = false;
                    if (element.Properties.IsPassword.ValueOrDefault)
                    { writer.Gap("unavailable"); return; }
                    // Only numeric runtime identity and a finite control role are read. No Name/Value/Text.
                    var runtime = element.Properties.RuntimeId.Value;
                    var identity = Encoding.UTF8.GetBytes(string.Join(",", runtime));
                    var pseudonym = Convert.ToHexString(HMACSHA256.HashData(key, identity)).ToLowerInvariant()[..32];
                    var role = element.ControlType switch
                    {
                        ControlType.Button => "button",
                        ControlType.Edit or ControlType.ComboBox => "input",
                        ControlType.List or ControlType.ListItem or ControlType.Tree or ControlType.TreeItem => "list",
                        ControlType.DataGrid or ControlType.DataItem => "grid",
                        ControlType.Window or ControlType.Pane => "window",
                        _ => "other"
                    };
                    writer.Observe(pseudonym, role);
                }
                catch { failed = true; }
            }
        }
        try
        {
            automation.RegisterFocusChangedEvent(Observe);
            Console.WriteLine(System.IO.Path.GetFileName(writer.Path));
            Console.Out.Flush();
            var duration = Stopwatch.StartNew();
            while (!stop.IsCancellationRequested && !target.HasExited && duration.Elapsed < TimeSpan.FromMinutes(15))
            {
                lock (gate) { if (failed) break; }
                Thread.Sleep(100);
            }
            lock (gate)
            {
                var reason = failed ? "unavailable" : target.HasExited ? "target-exited" :
                    stop.IsCancellationRequested ? "user" : "limit";
                if (failed) { try { writer.Gap("unavailable"); } catch (IOException) { } }
                writer.End(reason);
            }
            return failed ? 2 : 0;
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
}
