namespace OtoPlayer;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, "OtoPlayer.Harusame.SingleInstance", out var first);
        if (!first) { MessageBox.Show("すでに起動しています。タスクトレイを確認してください。", "音鳴らしますねぇ！"); return; }
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
