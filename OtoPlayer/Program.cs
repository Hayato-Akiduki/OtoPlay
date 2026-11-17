namespace OtoPlayer;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        Velopack.VelopackApp.Build().Run();
        using var mutex = new Mutex(true, "OtoPlayer.Harusame.SingleInstance", out var first);
        if (!first) { MessageBox.Show("すでに起動しています。タスクトレイを確認してください。", "音ならしますよっ！"); return; }
        ApplicationConfiguration.Initialize();
        using var updates = new AppUpdates();
        using var form = new MainForm(updates);
        Application.Run(form);
        updates.PrepareExit();
    }
}
