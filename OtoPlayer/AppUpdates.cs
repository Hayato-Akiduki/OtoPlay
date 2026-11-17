using Velopack;
using Velopack.Sources;

namespace OtoPlayer;

public sealed class AppUpdates : IDisposable
{
    private readonly UpdateManager manager;
    private readonly CancellationTokenSource shutdown = new();
    private int checking;
    public bool RestartOnExit { get; set; }
    public event Action<string>? StatusChanged;
    public event Action? UpdateReady;
    public bool HasPendingUpdate => manager.IsInstalled && manager.UpdatePendingRestart != null;

    public AppUpdates(UpdateManager? manager = null)
    {
        this.manager = manager ?? new UpdateManager(new GithubSource("https://github.com/Hayato-Akiduki/OtoPlay", null, false));
    }

    public async Task CheckAsync()
    {
        if (Interlocked.Exchange(ref checking, 1) != 0) return;
        try
        {
            if (!manager.IsInstalled)
            {
                Report("自動更新は Setup.exe でインストールした版で利用できます。");
                return;
            }
            if (HasPendingUpdate) { Report("更新の準備ができています。アプリ終了時に適用します。"); return; }
            Report("更新を確認しています…");
            var update = await manager.CheckForUpdatesAsync();
            if (shutdown.IsCancellationRequested) return;
            if (update == null) { Report("最新バージョンです。"); return; }
            Report($"バージョン {update.TargetFullRelease.Version} をダウンロードしています…");
            await manager.DownloadUpdatesAsync(update, cancelToken: shutdown.Token);
            if (shutdown.IsCancellationRequested) return;
            Report("更新をダウンロードしました。アプリ終了時に自動適用します。");
            UpdateReady?.Invoke();
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
        catch (Exception ex) { Report($"更新を確認・取得できませんでした。後で再試行します: {ex.Message}"); }
        finally { Interlocked.Exchange(ref checking, 0); }
    }

    private void Report(string message)
    {
        if (!shutdown.IsCancellationRequested) StatusChanged?.Invoke(message);
    }

    public void PrepareExit()
    {
        shutdown.Cancel();
        try
        {
            if (HasPendingUpdate)
                manager.WaitExitThenApplyUpdates(manager.UpdatePendingRestart, silent: true, restart: RestartOnExit);
        }
        catch (Exception ex)
        {
            // Bootstrap retries a downloaded update on the next launch.
            try
            {
                var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OtoPlayer");
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "update-error.log"), ex.ToString());
            }
            catch { /* Exiting must remain possible even if the log cannot be written. */ }
        }
    }

    public void Dispose() { shutdown.Cancel(); shutdown.Dispose(); }
}
