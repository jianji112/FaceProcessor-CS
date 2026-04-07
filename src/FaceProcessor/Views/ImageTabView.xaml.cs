using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OpenCvSharp;
using FaceProcessor.Models;
using FaceProcessor.Services;

namespace FaceProcessor.Views;

public partial class ImageTabView : UserControl
{
    private readonly ConfigManager _configManager;
    private readonly FaceDetector? _detector;
    private readonly ImageProcessor _processor;
    private readonly List<string> _selectedFiles = new();

    public ImageTabView(ConfigManager configManager, FaceDetector? detector)
    {
        InitializeComponent();
        _configManager = configManager;
        _detector = detector;
        _processor = new ImageProcessor(detector);
        LoadConfig();
        BindEvents();
    }

    private void LoadConfig()
    {
        var c = _configManager.Config;
        ModeCombo.SelectedIndex = (int)c.ImageMode;
        ResolutionCombo.SelectedIndex = c.MaxResolution switch { 1024 => 1, 2048 => 2, _ => 0 };
        FormatCombo.SelectedIndex = c.ExportFormat switch { "JPG" => 1, "WEBP" => 2, _ => 0 };
        QualitySlider.Value = c.ExportQuality;
        OutputPathTextBox.Text = c.OutputPath;
        SubfolderRadio.IsChecked = c.UseSubfolder;
        SubfolderNameTextBox.Text = c.SubfolderName;
    }

    private void BindEvents()
    {
        QualitySlider.ValueChanged += (_, e) => QualityText.Text = $"{(int)e.NewValue}%";
        
        ModeCombo.SelectionChanged += (_, _) => 
            _configManager.Update(c => c.ImageMode = (ProcessMode)ModeCombo.SelectedIndex);
        ResolutionCombo.SelectionChanged += (_, _) =>
            _configManager.Update(c => c.MaxResolution = ResolutionCombo.SelectedIndex switch
            {
                1 => 1024, 2 => 2048, _ => 0
            });
        FormatCombo.SelectionChanged += (_, _) =>
            _configManager.Update(c => c.ExportFormat = FormatCombo.SelectedIndex switch
            {
                1 => "JPG", 2 => "WEBP", _ => "PNG"
            });
        QualitySlider.ValueChanged += (_, _) =>
            _configManager.Update(c => c.ExportQuality = (int)QualitySlider.Value);
        OutputPathTextBox.TextChanged += (_, _) =>
            _configManager.Update(c => c.OutputPath = OutputPathTextBox.Text);
        SubfolderRadio.Checked += (_, _) =>
            _configManager.Update(c => c.UseSubfolder = SubfolderRadio.IsChecked == true);
        SubfolderNameTextBox.TextChanged += (_, _) =>
            _configManager.Update(c => c.SubfolderName = SubfolderNameTextBox.Text);
    }

    private void SelectImages_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Multiselect = true, Filter = "图片|*.png;*.jpg;*.jpeg;*.webp;*.bmp" };
        if (dlg.ShowDialog() == true)
        {
            foreach (var f in dlg.FileNames)
                if (!_selectedFiles.Contains(f))
                {
                    _selectedFiles.Add(f);
                    ImageListBox.Items.Add($"📷 {Path.GetFileName(f)}");
                }
        }
    }

    private void SelectFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog();
        if (dlg.ShowDialog() == true)
        {
            foreach (var ext in new[] { "*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp" })
                foreach (var f in Directory.GetFiles(dlg.FolderName, ext))
                    if (!_selectedFiles.Contains(f))
                    {
                        _selectedFiles.Add(f);
                        ImageListBox.Items.Add($"📷 {Path.GetFileName(f)}");
                    }
        }
    }

    private void ClearFiles_Click(object sender, RoutedEventArgs e)
    {
        _selectedFiles.Clear();
        ImageListBox.Items.Clear();
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog();
        if (dlg.ShowDialog() == true) OutputPathTextBox.Text = dlg.FolderName;
    }

    private async void Process_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedFiles.Count == 0)
        {
            MessageBox.Show("请先选择图片", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ProcessBtn.IsEnabled = false;
        ProgressBar.Value = 0;
        StatusText.Text = "处理中...";

        var success = 0;
        var failed = 0;
        var total = _selectedFiles.Count;
        var mode = (ProcessMode)ModeCombo.SelectedIndex;
        var maxRes = _configManager.Config.MaxResolution;
        var maxSize = _configManager.Config.MaxFileSizeMB;
        var format = _configManager.Config.ExportFormat;
        var quality = (int)QualitySlider.Value;

        await Task.Run(() =>
        {
            for (int i = 0; i < total; i++)
            {
                var inputPath = _selectedFiles[i];
                try
                {
                    using var img = Cv2.ImRead(inputPath);
                    if (img.Empty()) 
                    {
                        failed++;
                        continue;
                    }

                    // 检测人脸
                    var faces = _detector?.Detect(img) ?? new List<FaceRect>();
                    
                    // 处理
                    var options = new ProcessOptions 
                    { 
                        MaxResolution = maxRes, 
                        MaxFileSizeMB = maxSize, 
                        Format = format, 
                        Quality = quality 
                    };
                    var processed = _processor.Process(img, faces, mode, options);

                    // 确定输出目录
                    string outputDir;
                    if (SubfolderRadio.IsChecked == true)
                    {
                        outputDir = Path.Combine(Path.GetDirectoryName(inputPath)!, _configManager.Config.SubfolderName);
                    }
                    else
                    {
                        outputDir = OutputPathTextBox.Text;
                        if (string.IsNullOrWhiteSpace(outputDir))
                        {
                            // 如果输出路径为空，使用源文件所在目录
                            outputDir = Path.GetDirectoryName(inputPath)!;
                        }
                    }

                    // 确保输出目录存在
                    if (!Directory.Exists(outputDir))
                    {
                        Directory.CreateDirectory(outputDir);
                    }

                    // 确定输出文件名
                    var outputPath = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(inputPath) + 
                        GetExtension(format));
                    
                    // 保存
                    if (_processor.Save(processed, outputPath, format, quality, maxSize))
                    {
                        success++;
                    }
                    else
                    {
                        failed++;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"处理失败: {inputPath} - {ex.Message}");
                    failed++;
                }

                Dispatcher.Invoke(() =>
                {
                    ProgressBar.Value = (i + 1) * 100.0 / total;
                    StatusText.Text = $"处理中... {i + 1}/{total}";
                });
            }
        });

        StatusText.Text = $"✅ 完成！成功 {success}/{total}" + (failed > 0 ? $"，失败 {failed}" : "");
        ProcessBtn.IsEnabled = true;
    }

    private string GetExtension(string format)
    {
        return format.ToLower() switch
        {
            "jpg" or "jpeg" => ".jpg",
            "webp" => ".webp",
            _ => ".png"
        };
    }
}
