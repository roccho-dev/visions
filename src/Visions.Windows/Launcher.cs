using System.Diagnostics;
using Visions.Core;
using Visions.Viewer;

namespace Visions.Windows;

internal sealed class Launcher : Form
{
    private readonly string workspace = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "visions");
    private readonly ComboBox targets = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 380 };
    private readonly ComboBox captures = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 380 };
    private readonly CheckBox consent = new() { Text = "対象アプリを観測する許可があります（最大15分）", AutoSize = true };
    private readonly Button start = new() { Text = "記録開始", AutoSize = true };
    private readonly Button stop = new() { Text = "停止", AutoSize = true, Enabled = false };
    private readonly Label status = new() { Text = "記録していません。", AutoSize = true, MaximumSize = new Size(620, 0) };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 500 };
    private Process? worker;
    private LocalViewer? viewer;
    private string? selected;
    private bool closing;
    private bool starting;
    private string CaptureDirectory => System.IO.Path.Combine(workspace, "captures");
    private sealed record Target(int Pid, long Ticks, string Display) { public override string ToString() => Display; }
    private sealed record CaptureItem(string Path) { public override string ToString() => System.IO.Path.GetFileNameWithoutExtension(Path)[..8]; }

    public Launcher()
    {
        Text = "visions — ローカル観測 / プレビュー";
        ClientSize = new Size(700, 410);
        MinimumSize = new Size(650, 450);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Yu Gothic UI", 10);
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(24), AutoScroll = true };
        Controls.Add(layout);
        layout.Controls.Add(new Label { Text = "流れ・時間・根拠を、端末内だけで。", Font = new Font(Font.FontFamily, 17), AutoSize = true });
        layout.Controls.Add(new Label { Text = "フォーカス遷移だけを記録します。文字・画面・患者名は取りません。\nすべての操作を捉えるものではありません。", AutoSize = true, Margin = new Padding(0, 12, 0, 12) });
        var choose = new FlowLayoutPanel { AutoSize = true }; choose.Controls.Add(targets);
        AddButton(choose, "対象を再読込", RefreshTargets); layout.Controls.Add(choose);
        layout.Controls.Add(consent);
        var controls = new FlowLayoutPanel { AutoSize = true }; controls.Controls.Add(start); controls.Controls.Add(stop);
        AddButton(controls, "出力ファイルを取り込む", ImportFile);
        AddButton(controls, "デモ", () => { try { SelectCapture(Demo.Create(CaptureDirectory)); } catch { status.Text = "デモを保存できません。"; } });
        layout.Controls.Add(controls); layout.Controls.Add(status);
        var view = new FlowLayoutPanel { AutoSize = true }; view.Controls.Add(captures);
        AddButton(view, "可視化を開く", OpenViewer); layout.Controls.Add(view);
        AddButton(layout, "保存場所を開く", () => Process.Start(new ProcessStartInfo(workspace) { UseShellExecute = true }));
        layout.Controls.Add(new Label { Text = "CSV等の取り込みは原本をコピーします。本体の機微情報は除去されません。\n保存期限の自動削除は未実装です。試用後は保存場所から削除してください。", AutoSize = true, MaximumSize = new Size(620, 0), Margin = new Padding(0, 12, 0, 0) });
        start.Click += async (_, _) => await StartCapture();
        stop.Click += async (_, _) => await StopCapture();
        captures.SelectedIndexChanged += (_, _) => { if (captures.SelectedItem is CaptureItem item) Volatile.Write(ref selected, item.Path); };
        timer.Tick += (_, _) =>
        {
            if (!starting && worker is { HasExited: true })
            { var code = worker.ExitCode; worker.Dispose(); worker = null; stop.Enabled = false; start.Enabled = true; status.Text = code == 0 ? "記録を終了しました。" : "取得できない情報があります。可視化の不明・欠損を確認してください。"; }
        };
        Shown += async (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(CaptureDirectory);
                RefreshTargets(); RefreshCaptures();
                viewer = await LocalViewer.StartAsync(() => Volatile.Read(ref selected));
                timer.Start();
            }
            catch { status.Text = "初期化できません。保存権限と端末の制限を確認してください。"; start.Enabled = false; }
        };
        FormClosing += async (_, e) =>
        {
            if (closing) return;
            e.Cancel = true;
            await StopCapture();
            timer.Stop();
            if (viewer is not null) { try { await viewer.DisposeAsync(); } catch { } }
            closing = true; Close();
        };
    }
    private static void AddButton(Control parent, string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += (_, _) => { try { action(); } catch { MessageBox.Show("操作できませんでした。端末の権限・保存先を確認してください。", "visions"); } };
        parent.Controls.Add(button);
    }
    private void RefreshTargets()
    {
        targets.Items.Clear();
        using var self = Process.GetCurrentProcess();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id == Environment.ProcessId || process.SessionId != self.SessionId || process.MainWindowHandle == IntPtr.Zero) continue;
                    // The process label is shown locally, never written into facts.
                    targets.Items.Add(new Target(process.Id, process.StartTime.ToUniversalTime().Ticks, $"{process.ProcessName} / PID {process.Id}"));
                }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { }
            }
        }
        if (targets.Items.Count > 0) targets.SelectedIndex = 0;
    }
    private void RefreshCaptures()
    {
        captures.Items.Clear();
        foreach (var path in Directory.EnumerateFiles(CaptureDirectory, "*.jsonl").OrderByDescending(File.GetLastWriteTimeUtc).Take(100))
            if (Contract.Hex(System.IO.Path.GetFileNameWithoutExtension(path), 32)) captures.Items.Add(new CaptureItem(path));
        if (captures.Items.Count > 0) captures.SelectedIndex = 0;
    }
    private void SelectCapture(string path)
    {
        RefreshCaptures();
        for (var i = 0; i < captures.Items.Count; i++)
            if (captures.Items[i] is CaptureItem item && item.Path == path) { captures.SelectedIndex = i; break; }
        Volatile.Write(ref selected, path);
        status.Text = "可視化を開き、再読込してください。";
    }
    private async Task StartCapture()
    {
        if (worker is not null || !consent.Checked || targets.SelectedItem is not Target target)
        { status.Text = "対象と観測の許可を確認してください。"; return; }
        starting = true; start.Enabled = false; stop.Enabled = true;
        try
        {
            worker = Process.Start(Program.Child("--record", target.Pid.ToString(System.Globalization.CultureInfo.InvariantCulture), target.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture), CaptureDirectory));
            if (worker is null) throw new InvalidOperationException();
            var filename = await worker.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            if (filename is null || System.IO.Path.GetExtension(filename) != ".jsonl" || !Contract.Hex(System.IO.Path.GetFileNameWithoutExtension(filename), 32) || System.IO.Path.GetFileName(filename) != filename)
                throw new InvalidDataException();
            SelectCapture(System.IO.Path.Combine(CaptureDirectory, filename));
            status.Text = "● 記録中 — 対象アプリを普段どおり操作してください。停止、または15分で終了します。";
        }
        catch { await StopCapture(); status.Text = "記録を開始できません。権限・対象アプリのUIA対応を確認してください。"; }
        finally { starting = false; }
    }
    private async Task StopCapture()
    {
        var process = worker;
        if (process is null) return;
        worker = null;
        try
        {
            if (!process.HasExited)
            {
                await process.StandardInput.WriteLineAsync("stop"); await process.StandardInput.FlushAsync();
                try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
                catch (TimeoutException) { process.Kill(); await process.WaitForExitAsync(); }
            }
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
        finally { process.Dispose(); stop.Enabled = false; start.Enabled = true; status.Text = "記録を停止しました。強制終了した区間は未完了として表示されます。"; }
    }
    private void ImportFile()
    {
        using var dialog = new OpenFileDialog { Title = "取り込みを許可された既存の出力ファイル", Filter = "業務出力|*.csv;*.json;*.xml;*.txt|すべてのファイル|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (MessageBox.Show(this, "選んだファイル全体をこの端末の保存場所へコピーします。\n内容の個人情報は除去しません。許可を受けたデータだけを選んでください。", "取り込みの確認", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
        try { SelectCapture(FileExtractor.Extract(dialog.FileName, workspace)); }
        catch { status.Text = "取り込めません。ファイルの権限と32 MiBの上限を確認してください。"; }
    }
    private void OpenViewer()
    {
        if (viewer is null) { status.Text = "可視化の初期化が終わっていません。"; return; }
        Process.Start(new ProcessStartInfo(viewer.Url) { UseShellExecute = true });
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) timer.Dispose();
        base.Dispose(disposing);
    }
}
