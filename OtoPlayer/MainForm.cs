using System.Runtime.InteropServices;
using System.Text.Json;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace OtoPlayer;

public sealed class Settings
{
    public List<string> Files { get; set; } = [];
    public string? DeviceId { get; set; }
    public int Volume { get; set; } = 70;
    public bool Loop { get; set; }
    public List<SoundShortcut> Shortcuts { get; set; } = [];
}

public sealed record SoundShortcut(string Path, uint Modifiers, Keys Key)
{
    public string Label => $"{((Modifiers & 2) != 0 ? "Ctrl + " : "")}{((Modifiers & 1) != 0 ? "Alt + " : "")}{((Modifiers & 4) != 0 ? "Shift + " : "")}{Key}";
}

public sealed class MainForm : Form
{
    private readonly ListBox files = new() { Dock = DockStyle.Fill, DisplayMember = "Name" };
    private readonly ComboBox devices = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Name" };
    private readonly TrackBar volume = new() { Minimum = 0, Maximum = 100, Value = 70, TickFrequency = 10, Dock = DockStyle.Fill };
    private readonly CheckBox loop = new() { Text = "リピート", AutoSize = true };
    private readonly Label status = new() { Text = "音声ファイルを追加してください。", Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly Label volumeLabel = new() { Text = "音量 70%", AutoSize = true };
    private readonly NotifyIcon tray;
    private readonly Icon appIcon;
    private readonly AppUpdates updates;
    private readonly System.Windows.Forms.Timer updateTimer = new() { Interval = 6 * 60 * 60 * 1000 };
    private readonly ToolStripMenuItem updateStatus = new("更新を確認していません") { Enabled = false };
    private readonly ToolStripMenuItem applyUpdate = new("更新を適用して再起動") { Enabled = false };
    private static readonly uint TaskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private readonly MMDeviceEnumerator enumerator = new();
    private WasapiOut? player;
    private AudioFileReader? reader;
    private MMDevice? activeDevice;
    private bool exiting;
    private bool loading = true;
    private bool hotkeysRegistered;
    private bool resourcesDisposed;
    private readonly Dictionary<int, SoundShortcut> soundHotkeys = [];
    private string? playingPath;
    private readonly string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OtoPlayer", "settings.json");
    private Settings settings = new();
    private sealed record SoundFile(string Path, string Shortcut = "未設定") { public string Name => $"[{Shortcut}]  {System.IO.Path.GetFileName(Path)}"; }
    private sealed record OutputDevice(string Id, string Name);

    public MainForm(AppUpdates updates)
    {
        this.updates = updates;
        using (var stream = typeof(MainForm).Assembly.GetManifestResourceStream("OtoPlayer.app.ico")!)
        using (var sourceIcon = new Icon(stream, new Size(32, 32))) appIcon = (Icon)sourceIcon.Clone();
        Icon = appIcon;
        Text = $"音ならしますよっ！ v{Application.ProductVersion.Split('+')[0]}";
        Size = new Size(850, 600); MinimumSize = new Size(820, 480);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Yu Gothic UI", 10);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 9 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.Controls.Add(new Label { Text = "音ならしますよっ！", Font = new Font(Font.FontFamily, 20, FontStyle.Bold), AutoSize = true }, 0, 0);
        layout.Controls.Add(new Label { Text = "出力先は CABLE Input ／ 通話アプリのマイクは CABLE Output\n× で常駐。Ctrl + Alt + F9: 再生／停止 ／ Ctrl + Alt + F10: 停止", Dock = DockStyle.Fill }, 0, 1);
        var outputRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        outputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); outputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        outputRow.Controls.Add(devices); outputRow.Controls.Add(Button("再読み込み", (_, _) => RefreshDevices()));
        layout.Controls.Add(outputRow, 0, 2);
        var fileButtons = new FlowLayoutPanel { Dock = DockStyle.Fill };
        fileButtons.Controls.Add(Button("＋ 音声を追加", AddFiles));
        fileButtons.Controls.Add(Button("一覧から削除", (_, _) => { if (files.SelectedItem is SoundFile sound) { Stop(); settings.Shortcuts.RemoveAll(s => SamePath(s.Path, sound.Path)); files.Items.RemoveAt(files.SelectedIndex); RebindSoundHotkeys(); Save(); } }));
        fileButtons.Controls.Add(Button("キーを設定", (_, _) => EditShortcut()));
        fileButtons.Controls.Add(Button("キーを解除", (_, _) => {
            if (files.SelectedItem is not SoundFile sound) return;
            settings.Shortcuts.RemoveAll(s => SamePath(s.Path, sound.Path));
            UpdateShortcutLabels(); RebindSoundHotkeys(); Save();
        }));
        layout.Controls.Add(fileButtons, 0, 3); layout.Controls.Add(files, 0, 4);
        var playback = new FlowLayoutPanel { Dock = DockStyle.Fill };
        playback.Controls.Add(Button("▶ 再生", (_, _) => Play())); playback.Controls.Add(Button("■ 停止", (_, _) => Stop())); playback.Controls.Add(loop);
        layout.Controls.Add(playback, 0, 5); layout.Controls.Add(volumeLabel, 0, 6); layout.Controls.Add(volume, 0, 7); layout.Controls.Add(status, 0, 8);
        Controls.Add(layout);
        var menu = new ContextMenuStrip();
        menu.Items.Add("画面を開く", null, (_, _) => Restore());
        menu.Items.Add("選択音を再生", null, (_, _) => Play());
        menu.Items.Add("再生／停止 (Ctrl + Alt + F9)", null, (_, _) => TogglePlayback());
        menu.Items.Add("停止", null, (_, _) => Stop());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(updateStatus);
        menu.Items.Add("更新を確認", null, async (_, _) => await updates.CheckAsync());
        menu.Items.Add(applyUpdate);
        applyUpdate.Click += (_, _) => {
            if (!updates.HasPendingUpdate) return;
            updates.RestartOnExit = true; exiting = true; Save(); Stop(); Close();
        };
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("終了", null, (_, _) => { exiting = true; Close(); });
        tray = new NotifyIcon { Icon = appIcon, Text = "音ならしますよっ！", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => Restore();
        updates.StatusChanged += UpdateStatusChanged;
        updates.UpdateReady += UpdateReady;
        updateTimer.Tick += async (_, _) => await updates.CheckAsync();
        Shown += async (_, _) => { updateTimer.Start(); await updates.CheckAsync(); };
        files.DoubleClick += (_, _) => Play();
        volume.ValueChanged += (_, _) => { volumeLabel.Text = $"音量 {volume.Value}%"; if (reader != null) reader.Volume = volume.Value / 100f; Save(); };
        loop.CheckedChanged += (_, _) => Save();
        devices.SelectedIndexChanged += (_, _) => { if (!loading) { Stop(); Save(); } };
        LoadSettings();
        loading = false;
        RebindSoundHotkeys();
        FormClosing += (_, e) => { if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); tray.ShowBalloonTip(2000, Text, "トレイに常駐しています。終了は右クリックメニューから。", ToolTipIcon.Info); } };
    }

    private static Button Button(string text, EventHandler action)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 32 }; button.Click += action; return button;
    }
    private void Restore() { Show(); WindowState = FormWindowState.Normal; Activate(); }
    private void UpdateStatusChanged(string message)
    {
        if (IsDisposed || Disposing) return;
        updateStatus.Text = message;
        applyUpdate.Enabled = updates.HasPendingUpdate;
    }
    private void UpdateReady()
    {
        if (IsDisposed || Disposing) return;
        applyUpdate.Enabled = true;
        tray.ShowBalloonTip(4000, "更新の準備ができました", "再生は続行できます。トレイから終了すると更新します。", ToolTipIcon.Info);
    }
    private static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private void UpdateShortcutLabels()
    {
        for (var i = 0; i < files.Items.Count; i++)
        {
            var sound = (SoundFile)files.Items[i];
            files.Items[i] = sound with { Shortcut = settings.Shortcuts.FirstOrDefault(s => SamePath(s.Path, sound.Path))?.Label ?? "未設定" };
        }
    }
    private void EditShortcut()
    {
        if (files.SelectedItem is not SoundFile sound) { status.Text = "キーを設定する音声を選択してください。"; return; }
        var previous = settings.Shortcuts.FirstOrDefault(s => SamePath(s.Path, sound.Path));
        using var dialog = new Form { Text = "ショートカット設定", ClientSize = new Size(440, 190), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, Font = Font };
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), FlowDirection = FlowDirection.TopDown };
        panel.Controls.Add(new Label { Text = Path.GetFileName(sound.Path), AutoSize = true, MaximumSize = new Size(400, 45) });
        var modifiers = new FlowLayoutPanel { AutoSize = true };
        var ctrl = new CheckBox { Text = "Ctrl", Checked = previous == null || (previous.Modifiers & 2) != 0, AutoSize = true };
        var alt = new CheckBox { Text = "Alt", Checked = previous == null || (previous.Modifiers & 1) != 0, AutoSize = true };
        var shift = new CheckBox { Text = "Shift", Checked = previous != null && (previous.Modifiers & 4) != 0, AutoSize = true };
        modifiers.Controls.AddRange([ctrl, alt, shift]); panel.Controls.Add(modifiers);
        var key = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
        var choices = Enumerable.Range((int)Keys.A, 26).Concat(Enumerable.Range((int)Keys.D0, 10)).Concat(Enumerable.Range((int)Keys.F1, 12));
        foreach (var code in choices) key.Items.Add((Keys)code);
        key.SelectedItem = previous?.Key ?? Keys.F1; panel.Controls.Add(key);
        var apply = Button("登録", (_, _) => {
            uint mods = (ctrl.Checked ? 2u : 0) | (alt.Checked ? 1u : 0) | (shift.Checked ? 4u : 0);
            var selectedKey = (Keys)key.SelectedItem!;
            if ((mods & 3) == 0) { MessageBox.Show(dialog, "Ctrl または Alt を含めてください。"); return; }
            if ((mods == 3 && selectedKey is Keys.F9 or Keys.F10) || settings.Shortcuts.Any(s => !SamePath(s.Path, sound.Path) && s.Modifiers == mods && s.Key == selectedKey))
            { MessageBox.Show(dialog, "そのキーは別の操作に登録済みです。"); return; }
            // Probe availability before replacing the existing registration.
            var unchanged = previous?.Modifiers == mods && previous.Key == selectedKey && soundHotkeys.Values.Any(s => s == previous);
            if (!unchanged && !RegisterHotKey(Handle, 9999, mods | 0x4000, (uint)selectedKey))
            { MessageBox.Show(dialog, "そのキーは他のアプリなどで使用中です。別のキーを選んでください。"); return; }
            if (!unchanged) UnregisterHotKey(Handle, 9999);
            settings.Shortcuts.RemoveAll(s => SamePath(s.Path, sound.Path));
            settings.Shortcuts.Add(new SoundShortcut(sound.Path, mods, selectedKey));
            UpdateShortcutLabels(); RebindSoundHotkeys(); Save(); dialog.Close();
        });
        panel.Controls.Add(apply); dialog.Controls.Add(panel); dialog.AcceptButton = apply; dialog.ShowDialog(this);
    }
    private void RebindSoundHotkeys()
    {
        if (!IsHandleCreated) return;
        foreach (var id in soundHotkeys.Keys) UnregisterHotKey(Handle, id);
        soundHotkeys.Clear();
        var failed = new List<string>();
        var nextId = 100;
        foreach (var shortcut in settings.Shortcuts)
        {
            if (!files.Items.Cast<SoundFile>().Any(f => SamePath(f.Path, shortcut.Path))) continue;
            if (shortcut.Modifiers is 0 or > 7 || (shortcut.Modifiers & 3) == 0 || (shortcut.Modifiers == 3 && shortcut.Key is Keys.F9 or Keys.F10)) { failed.Add(shortcut.Label); continue; }
            if (RegisterHotKey(Handle, nextId, shortcut.Modifiers | 0x4000, (uint)shortcut.Key)) soundHotkeys.Add(nextId, shortcut);
            else failed.Add(shortcut.Label);
            nextId++;
        }
        if (failed.Count > 0) status.Text = $"キーを登録できません（競合など）: {string.Join(", ", failed)}";
    }
    private void AddFiles(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog { Multiselect = true, Filter = "音声ファイル|*.wav;*.mp3;*.aiff;*.aif;*.wma;*.m4a;*.aac|すべてのファイル|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        foreach (var path in dialog.FileNames)
            if (!files.Items.Cast<SoundFile>().Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase))) files.Items.Add(new SoundFile(path));
        if (files.SelectedIndex < 0 && files.Items.Count > 0) files.SelectedIndex = 0;
        Save();
    }
    private void RefreshDevices()
    {
        var previous = (devices.SelectedItem as OutputDevice)?.Id ?? settings.DeviceId;
        Stop();
        var wasLoading = loading; loading = true;
        try
        {
            devices.Items.Clear();
            var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var device in endpoints)
            {
                using (device) devices.Items.Add(new OutputDevice(device.ID, device.FriendlyName));
            }
            var outputs = devices.Items.Cast<OutputDevice>().ToList();
            var selected = outputs.FindIndex(d => d.Id == previous);
            // Never silently switch to speakers when a saved output is missing.
            if (selected < 0 && previous == null) selected = outputs.FindIndex(d => d.Name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase));
            devices.SelectedIndex = selected;
            status.Text = selected >= 0 ? "出力先を確認して再生してください。" : "出力先が見つかりません。デバイスを選択してください。";
        }
        catch (Exception ex) { status.Text = $"デバイス取得失敗: {ex.Message}"; }
        finally { loading = wasLoading; }
    }
    private void TogglePlayback()
    {
        if (player?.PlaybackState == PlaybackState.Playing) Stop();
        else Play();
    }
    private void Play()
    {
        PlaySound(files.SelectedItem as SoundFile);
    }
    private void PlaySound(SoundFile? sound)
    {
        Stop();
        if (sound == null) { status.Text = "再生する音声を選択してください。"; return; }
        if (devices.SelectedItem is not OutputDevice output) { status.Text = "出力先を選択してください。"; return; }
        try
        {
            activeDevice = enumerator.GetDevice(output.Id);
            reader = new AudioFileReader(sound.Path) { Volume = volume.Value / 100f };
            player = new WasapiOut(activeDevice, AudioClientShareMode.Shared, true, 100);
            player.Init(reader);
            player.PlaybackStopped += PlaybackStopped;
            playingPath = sound.Path;
            player.Play(); status.Text = $"再生中: {sound.Name}";
        }
        catch (Exception ex) { Stop(); status.Text = $"再生できません: {ex.Message}"; }
    }
    private void PlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (IsDisposed || Disposing || !IsHandleCreated) return;
        BeginInvoke((Action)(() =>
        {
            if (sender != player) return;
            if (e.Exception != null) { Stop(); status.Text = $"出力が停止しました: {e.Exception.Message}"; }
            else if (loop.Checked && playingPath != null) PlaySound(new SoundFile(playingPath));
            else { Stop(); status.Text = "再生終了"; }
        }));
    }
    private void Stop()
    {
        var old = player; player = null;
        if (old != null) { old.PlaybackStopped -= PlaybackStopped; old.Stop(); old.Dispose(); }
        reader?.Dispose(); reader = null; activeDevice?.Dispose(); activeDevice = null;
        playingPath = null;
        status.Text = "停止中";
    }
    private void LoadSettings()
    {
        string? warning = null;
        try { if (File.Exists(settingsPath)) settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(settingsPath)) ?? new(); }
        catch (Exception ex) { warning = $"設定を読み込めません: {ex.Message}"; }
        settings.Shortcuts ??= [];
        foreach (var path in settings.Files ?? []) files.Items.Add(new SoundFile(path));
        UpdateShortcutLabels();
        if (files.Items.Count > 0) files.SelectedIndex = 0;
        volume.Value = Math.Clamp(settings.Volume, 0, 100); loop.Checked = settings.Loop;
        RefreshDevices();
        if (warning != null) status.Text = warning;
    }
    private void Save()
    {
        if (loading) return;
        settings.Files = files.Items.Cast<SoundFile>().Select(f => f.Path).ToList();
        settings.DeviceId = (devices.SelectedItem as OutputDevice)?.Id ?? settings.DeviceId;
        settings.Volume = volume.Value; settings.Loop = loop.Checked;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            File.WriteAllText(settingsPath + ".tmp", JsonSerializer.Serialize(settings));
            File.Move(settingsPath + ".tmp", settingsPath, true);
        }
        catch (Exception ex) { status.Text = $"設定を保存できません: {ex.Message}"; }
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var play = RegisterHotKey(Handle, 1, 0x4003, (uint)Keys.F9);
        var stop = RegisterHotKey(Handle, 2, 0x4003, (uint)Keys.F10);
        hotkeysRegistered = true;
        RebindSoundHotkeys();
        if (!play || !stop) status.Text = "一部のホットキーが他のアプリと競合しています。画面・トレイから操作できます。";
    }
    protected override void WndProc(ref Message m)
    {
        if (TaskbarCreated != 0 && m.Msg == TaskbarCreated && tray != null && !exiting)
        {
            tray.Visible = false;
            tray.Icon = appIcon;
            tray.Visible = true;
        }
        if (m.Msg == 0x0312)
        {
            if (m.WParam == 1) TogglePlayback();
            else if (m.WParam == 2) Stop();
            else if (soundHotkeys.TryGetValue((int)m.WParam, out var shortcut))
            {
                if (player?.PlaybackState == PlaybackState.Playing && playingPath != null && SamePath(playingPath, shortcut.Path)) Stop();
                else PlaySound(new SoundFile(shortcut.Path));
            }
        }
        base.WndProc(ref m);
    }
    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (hotkeysRegistered) { UnregisterHotKey(Handle, 1); UnregisterHotKey(Handle, 2); }
        foreach (var id in soundHotkeys.Keys) UnregisterHotKey(Handle, id);
        soundHotkeys.Clear();
        base.OnHandleDestroyed(e);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !resourcesDisposed) {
            resourcesDisposed = true;
            updateTimer.Stop(); updateTimer.Dispose();
            updates.StatusChanged -= UpdateStatusChanged; updates.UpdateReady -= UpdateReady;
            Stop(); tray.Visible = false; tray.Dispose(); appIcon.Dispose(); enumerator.Dispose();
        }
        base.Dispose(disposing);
    }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);
}
