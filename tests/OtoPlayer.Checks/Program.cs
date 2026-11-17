using System.Reflection;
using OtoPlayer;
using Velopack.Locators;
using Velopack;
using System.Text.Json.Nodes;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Velopack.VelopackApp.Build().Run();
        ApplicationConfiguration.Initialize();
        using var updates = new AppUpdates();
        string? updateStatus = null;
        updates.StatusChanged += value => updateStatus = value;
        updates.CheckAsync().GetAwaiter().GetResult();
        Assert(updateStatus?.Contains("Setup.exe") == true, "Portable build explains installation requirement");
        using var form = new MainForm(updates);
        _ = form.Handle;
        var tray = (NotifyIcon)GetField("tray").GetValue(form)!;
        Assert(form.Icon != null && tray.Icon != null && tray.Visible, "Window and tray have an icon");
        using var bitmap = tray.Icon!.ToBitmap();
        Assert(bitmap.Width >= 16, "Embedded tray icon loads at a usable size");
        var taskbarMessage = (uint)GetField("TaskbarCreated").GetValue(null)!;
        var message = Message.Create(form.Handle, (int)taskbarMessage, IntPtr.Zero, IntPtr.Zero);
        typeof(MainForm).GetMethod("WndProc", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, [message]);
        Assert(tray.Visible && tray.Icon != null, "Tray icon re-registers after Explorer restart notification");
        form.Dispose(); form.Dispose();
        Assert(!tray.Visible, "Tray icon is removed on disposal; repeated disposal is safe");
        if (args.Length > 0) Task.Run(() => VerifyUpdates(Path.GetFullPath(args[0]))).GetAwaiter().GetResult();
    }

    private static async Task VerifyUpdates(string feed)
    {
        var scratch = Path.Combine(Path.GetDirectoryName(feed)!, "update-checks", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        var currentVersion = JsonNode.Parse(File.ReadAllText(Path.Combine(feed, "releases.win.json")))!["Assets"]![0]!["Version"]!.GetValue<string>();
        UpdateManager Manager(string version, string source, string name)
        {
            var packages = Path.Combine(scratch, name);
            Directory.CreateDirectory(packages);
            return new UpdateManager(source, locator: new TestVelopackLocator("HayatoAkiduki.OtoPlay", version, packages, null));
        }
        using (var service = new AppUpdates(Manager("0.0.0", feed, "upgrade")))
        {
            var ready = false;
            service.UpdateReady += () => ready = true;
            await service.CheckAsync();
            Assert(ready && service.HasPendingUpdate, "Older installation downloads and verifies the update, leaving it pending without interrupting playback");
        }
        var same = await Manager(currentVersion, feed, "same").CheckForUpdatesAsync();
        Assert(same == null, "Current version is not offered the same update");
        var newer = await Manager("9999.0.0", feed, "newer").CheckForUpdatesAsync();
        Assert(newer == null, "Newer installation is not downgraded");
        var brokenFeed = Path.Combine(scratch, "broken-feed"); Directory.CreateDirectory(brokenFeed);
        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(feed, "releases.win.json")))!;
        var asset = json["Assets"]![0]!;
        asset["SHA256"] = new string('0', 64); asset["SHA1"] = new string('0', 40);
        File.WriteAllText(Path.Combine(brokenFeed, "releases.win.json"), json.ToJsonString());
        File.WriteAllText(Path.Combine(brokenFeed, asset["FileName"]!.GetValue<string>()), "corrupted package");
        using (var service = new AppUpdates(Manager("0.0.0", brokenFeed, "corrupt")))
        {
            var ready = false; string? message = null;
            service.UpdateReady += () => ready = true;
            service.StatusChanged += value => message = value;
            await service.CheckAsync();
            Assert(!ready && !service.HasPendingUpdate && message?.Contains("取得できませんでした") == true, "Corrupt update is rejected and the application remains usable");
        }
        using (var service = new AppUpdates(Manager("0.0.0", "http://127.0.0.1:1", "offline")))
        {
            string? message = null; service.StatusChanged += value => message = value;
            await service.CheckAsync();
            Assert(message?.Contains("後で再試行") == true && !service.HasPendingUpdate, "Unavailable update feed is handled without a crash");
        }
    }

    private static FieldInfo GetField(string name) => typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic)!;
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }
}
