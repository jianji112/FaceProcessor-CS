using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using FaceProcessor.Models;
using FaceProcessor.Services;

namespace FaceProcessor.Views;

public partial class VideoTabView : UserControl
{
    private readonly ConfigManager _configManager;
    private readonly VideoProcessor _processor;
    private CancellationTokenSource? _cts;

    public VideoTabView(ConfigManager configManager, VideoProcessor processor)
    {
        InitializeComponent();
        _configManager = configManager;
        _processor = processor;
        LoadConfig();
        BindEvents();
    }

    private void LoadConfig()
    {
        var c = _configManager.Config;
        ModeCombo.SelectedIndex = c.VideoMode == ProcessMode.Blur ? 1 : 0;
        BlockSizeTextBox.Text = c.VideoBlockSize.ToString();
        BlurStrengthTextBox.Text = c.VideoBlurStrength.ToString();
        KeepAudioCheckBox.IsChecked = c.VideoKeepAudio;
    }

    private void BindEvents()
    {
        ModeCombo.SelectionChanged += (_, _) =>
            _configManager.Update(c => c.VideoMode = ModeCombo.SelectedIndex == 0 ? ProcessMode.Mosaic : ProcessMode.Blur);
        BlockSizeTextBox.TextChanged += (_, _) =>
        {
            if (int.TryParse(BlockSizeTextBox.Text, out var v))
                _configManager.Update(c => c.VideoBlockSize = v);
        };
        BlurStrengthTextBox.TextChanged += (_, _) =>
        {
            if (int.TryParse(BlurStrengthTextBox.Text, out var v))
                _configManager.Update(c => c.VideoBlurStrength = v);
        };
        KeepAudioCheckBox.Checked += (_, _) =>
            _configManager.Update(c => c.VideoKeepAudio = KeepAudioCheckBox.IsChecked == true);
    }

    public void UpdateGpuStatus(bool available, string message)
    {
        Dispatcher.Invoke(() =>
        {
            GpuStatusText.Text = available ? $"✅ {message}" : $"⚠️ {message}";
            GpuStatusText.Foreground = available ? 
                System.Windows.Media.Brushes.LightGreen : 
                System.Windows.Media.Brushes.Orange;
        });
    }

    private void SelectVideo_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "视频|*.mp4;*.avi;*.mov;*.mkv;*.flv;*.wmv" };
        if (dlg.ShowDialog() == true)
        {
            VideoPathTextBox.Text = dlg.FileName;
            // Always clear output path when input changes to avoid stale filename
            OutputPathTextBox.Text = "";
            var dir = Path.GetDirectoryName(dlg.FileName);
            var name = Path.GetFileNameWithoutExtension(dlg.FileName);
            OutputPathTextBox.Text = Path.Combine(dir!, $"{name}_processed.mp4");
        }
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog();
        if (dlg.ShowDialog() == true)
        {
            // Always use the currently selected input video name (not a stale one)
            var name = string.IsNullOrEmpty(VideoPathTextBox.Text)
                ? "output"
                : Path.GetFileNameWithoutExtension(VideoPathTextBox.Text);
            OutputPathTextBox.Text = Path.Combine(dlg.FolderName, $"{name}_processed.mp4");
        }
    }

    private async void Process_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(VideoPathTextBox.Text) || !File.Exists(VideoPathTextBox.Text))
        {
            MessageBox.Show("请先选择视频文件", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrEmpty(OutputPathTextBox.Text))
        {
            MessageBox.Show("请指定输出路径", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ProcessBtn.Visibility = Visibility.Collapsed;
        CancelBtn.Visibility = Visibility.Visible;
        CancelBtn.IsEnabled = true;
        ProgressBar.Value = 0;
        StatusText.Text = "正在初始化...";

        // 重置处理器状态
        _processor.Reset();
        _cts = new CancellationTokenSource();
        
        var progress = new Progress<double>(p => Dispatcher.Invoke(() =>
        {
            ProgressBar.Value = p * 100;
            ProgressText.Text = $"{(int)(p * 100)}%";
        }));

        var options = new ProcessOptions
        {
            BlockSize = int.TryParse(BlockSizeTextBox.Text, out var bs) ? bs : 0,
            BlurStrength = int.TryParse(BlurStrengthTextBox.Text, out var bls) ? bls : 99,
            KeepAudio = KeepAudioCheckBox.IsChecked == true
        };
        var mode = ModeCombo.SelectedIndex == 0 ? ProcessMode.Mosaic : ProcessMode.Blur;

        bool success = false;
        bool cancelled = false;
        
        try
        {
            success = await _processor.ProcessVideoAsync(
                VideoPathTextBox.Text, OutputPathTextBox.Text, mode, options, progress, _cts.Token);
            cancelled = _cts.Token.IsCancellationRequested;
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"❌ 错误: {ex.Message}";
        }

        Dispatcher.Invoke(() =>
        {
            ProcessBtn.Visibility = Visibility.Visible;
            CancelBtn.Visibility = Visibility.Collapsed;
            
            if (cancelled)
                StatusText.Text = "⚠️ 已取消";
            else if (success)
                StatusText.Text = "✅ 完成！";
            else
                StatusText.Text = "❌ 处理失败";
        });
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        _processor.Cancel();
        StatusText.Text = "正在取消...";
        CancelBtn.IsEnabled = false;
    }
}
