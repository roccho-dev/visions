using System.Diagnostics;
using Visions.Core;
using Visions.Viewer;

namespace Visions.Windows;

// Opt-in integration test only. Changes focus inside its own synthetic form, never in a business app.
internal static class Probe
{
    public static int Run(string directory)
    {
        Directory.CreateDirectory(directory);
        var result = 2;
        using var form = new Form { Text = "visions synthetic UIA probe", Width = 420, Height = 180 };
        var input = new TextBox { Text = "PRIVATE_TEST_MARKER", Left = 20, Top = 20, Width = 300 };
        var button = new Button { Text = "PRIVATE_TEST_MARKER", Left = 20, Top = 60, Width = 300 };
        form.Controls.AddRange([input, button]);
        form.Shown += async (_, _) =>
        {
            Process? observer = null;
            try
            {
                using var self = Process.GetCurrentProcess();
                observer = Process.Start(Program.Child("--record", self.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    self.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture), directory));
                if (observer is null) throw new InvalidOperationException();
                var filename = await observer.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
                if (filename is null || System.IO.Path.GetFileName(filename) != filename || !Contract.Hex(System.IO.Path.GetFileNameWithoutExtension(filename), 32))
                    throw new InvalidDataException();
                for (var i = 0; i < 10; i++)
                {
                    if (i % 2 == 0) input.Focus(); else button.Focus();
                    await Task.Delay(200);
                }
                await observer.StandardInput.WriteLineAsync("stop");
                await observer.StandardInput.FlushAsync();
                await observer.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
                if (observer.ExitCode != 0) throw new InvalidOperationException();
                var file = System.IO.Path.Combine(directory, filename);
                var snapshot = Journal.Read(file);
                var model = Projection.Build(snapshot);
                if (model.Steps.Count < 2 || model.Steps.Select(s => s.Target).Distinct().Count() < 2 ||
                    model.Issues.Count != 0 || File.ReadAllText(file).Contains("PRIVATE_TEST_MARKER", StringComparison.Ordinal))
                    throw new InvalidDataException();
                await File.WriteAllTextAsync(System.IO.Path.Combine(directory, "uia-probe-ok.txt"),
                    $"synthetic WinForms UIA focus: {model.Steps.Count} observations; sensitive text absent; normal stop passed\n");
                result = 0;
            }
            catch { result = 2; }
            finally
            {
                if (observer is not null)
                {
                    try { if (!observer.HasExited) observer.Kill(); } catch (InvalidOperationException) { }
                    observer.Dispose();
                }
                form.Close();
            }
        };
        Application.Run(form);
        return result;
    }
}
