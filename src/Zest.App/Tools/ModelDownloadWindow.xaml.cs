using System.Globalization;
using System.Windows;
using Zest.App.Theme;
using Zest.Core;
using Zest.Core.Tools;

namespace Zest.App.Tools;

/// <summary>
/// Asks before the first background removal downloads the model (~973 MB), then downloads it with progress. 취소 (or
/// closing the window) stops the download and leaves nothing behind; any failure puts the window back in a state where
/// 다시 시도 works. On success the window closes and runs the queued work (one action per drop that arrived while it was
/// open). Windows ML accelerators are not handled here (see <c>BackgroundRemover.LoadAsync</c>).
/// </summary>
public partial class ModelDownloadWindow : ZestWindow
{
    readonly List<Action> _then = [];
    CancellationTokenSource? _running;

    public ModelDownloadWindow(Action ready)
    {
        _then.Add(ready);
        InitializeComponent();
        SizeRun.Text = string.Create(CultureInfo.InvariantCulture, $"{ModelStore.ExpectedBytes / 1_000_000.0:0} MB");
        DownloadButton.Click += async (_, _) => await DownloadAsync();
        CancelButton.Click += (_, _) => Close();
        Closing += (_, _) => _running?.Cancel();
    }

    /// <summary>Adds work for another drop to run once the model is ready.</summary>
    public void Then(Action ready) => _then.Add(ready);

    /// <summary>For the render harness: shows the window mid-download.</summary>
    internal void Preview(double fraction)
    {
        ShowRunning();
        ShowModelProgress(fraction);
    }

    /// <summary>For the render harness: shows the window after a failed download.</summary>
    internal void PreviewFailed(string message) => ShowFailed(message);

    async Task DownloadAsync()
    {
        using var cts = new CancellationTokenSource();
        _running = cts;
        ShowRunning();
        try
        {
            PhaseText.Text = "모델 확인 중…";
            if (!await Task.Run(() => ModelStore.IsReady, cts.Token))
            {
                ShowModelProgress(0);
                var progress = new Progress<double>(ShowModelProgress); // created here so reports land on the UI thread
                await Task.Run(() => ModelStore.DownloadAsync(progress, cts.Token), cts.Token);
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            return; // the window is closing
        }
        catch (ZestException e)
        {
            ShowFailed(e.Message);
            return;
        }
        catch (Exception e)
        {
            ShowFailed($"알 수 없는 오류: {e.Message}");
            return;
        }
        finally
        {
            _running = null;
        }
        Close();
        foreach (var ready in _then) ready();
    }

    void ShowRunning()
    {
        ErrorText.Text = "";
        DownloadButton.IsEnabled = false;
        ProgressPanel.Visibility = Visibility.Visible;
        AmountText.Text = "";
        LoadBar.IsIndeterminate = true;
    }

    void ShowModelProgress(double fraction)
    {
        PhaseText.Text = "모델 내려받는 중…";
        SetBar(fraction);
        var total = ModelStore.ExpectedBytes / 1_000_000.0;
        AmountText.Text = string.Create(CultureInfo.InvariantCulture, $"{fraction * total:0} / {total:0} MB");
    }

    void SetBar(double fraction)
    {
        LoadBar.IsIndeterminate = false;
        LoadBar.Value = fraction;
    }

    void ShowFailed(string message)
    {
        ProgressPanel.Visibility = Visibility.Collapsed;
        ErrorText.Text = message;
        DownloadButton.Content = "다시 시도";
        DownloadButton.IsEnabled = true;
    }
}
