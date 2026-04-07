using System.IO;
using OpenCvSharp;
using FaceProcessor.Models;

namespace FaceProcessor.Services;

/// <summary>视频处理器</summary>
public class VideoProcessor
{
    private readonly FaceDetector? _detector;
    private readonly ImageProcessor _processor;
    private CancellationTokenSource? _cts;
    private bool _isProcessing;

    public VideoProcessor(FaceDetector? detector = null)
    {
        _detector = detector;
        _processor = new ImageProcessor(detector);
    }

    /// <summary>处理视频</summary>
    public async Task<bool> ProcessVideoAsync(
        string inputPath,
        string outputPath,
        ProcessMode mode,
        ProcessOptions options,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _isProcessing = true;

        string tempOutputPath = "";
        
        try
        {
            System.Diagnostics.Debug.WriteLine($"[VideoProcessor] 开始处理: {inputPath}");
            
            using var capture = new VideoCapture(inputPath);
            if (!capture.IsOpened())
            {
                System.Diagnostics.Debug.WriteLine($"[VideoProcessor] 无法打开视频文件: {inputPath}");
                ReportStatus(progress, -1, "无法打开视频文件");
                return false;
            }

            var fps = capture.Get(VideoCaptureProperties.Fps);
            var width = (int)capture.Get(VideoCaptureProperties.FrameWidth);
            var height = (int)capture.Get(VideoCaptureProperties.FrameHeight);
            var totalFrames = (int)capture.Get(VideoCaptureProperties.FrameCount);

            System.Diagnostics.Debug.WriteLine($"[VideoProcessor] 视频信息: {width}x{height}, {fps:F1}fps, {totalFrames}帧");

            ReportStatus(progress, 0, $"视频: {width}x{height}, {fps:F1}fps, {totalFrames}帧");

            // 创建临时输出文件（无音频）
            tempOutputPath = Path.Combine(
                Path.GetDirectoryName(outputPath) ?? "",
                $"{Path.GetFileNameWithoutExtension(outputPath)}_temp{Path.GetExtension(outputPath)}"
            );

            using var writer = new VideoWriter();
            writer.Open(tempOutputPath, FourCC.FromString("mp4v"), fps, new Size(width, height));

            if (!writer.IsOpened())
            {
                System.Diagnostics.Debug.WriteLine($"[VideoProcessor] 无法创建输出文件: {tempOutputPath}");
                ReportStatus(progress, -1, "无法创建输出文件");
                return false;
            }

            var frameIndex = 0;
            var faceCount = 0;
            var frame = new Mat();

            while (capture.Read(frame))
            {
                if (_cts.Token.IsCancellationRequested)
                {
                    System.Diagnostics.Debug.WriteLine($"[VideoProcessor] 用户取消");
                    ReportStatus(progress, -1, "已取消");
                    return false;
                }

                if (frame.Empty())
                    break;

                // 检测人脸
                var faces = _detector?.Detect(frame) ?? new List<FaceRect>();
                faceCount += faces.Count;

                // 处理帧
                var processed = _processor.Process(frame, faces, mode, options);

                // 写入
                writer.Write(processed);

                frameIndex++;
                var progressValue = (double)frameIndex / totalFrames;
                ReportStatus(progress, progressValue, $"处理中: {frameIndex}/{totalFrames} 帧, 检测到 {faces.Count} 个人脸");
            }

            writer.Release();
            capture.Release();

            System.Diagnostics.Debug.WriteLine($"[VideoProcessor] 帧处理完成: {frameIndex}帧, 共检测到 {faceCount} 个人脸");

            ReportStatus(progress, 0.95, "正在合并音频...");

            // 合并音频
            if (options.KeepAudio)
            {
                var mergeSuccess = await AudioMerger.MergeAudioAsync(
                    inputPath, tempOutputPath, outputPath, _cts.Token);

                if (mergeSuccess)
                {
                    try { File.Delete(tempOutputPath); } catch { }
                    System.Diagnostics.Debug.WriteLine($"[VideoProcessor] 音频合并成功");
                }
                else
                {
                    File.Copy(tempOutputPath, outputPath, overwrite: true);
                    try { File.Delete(tempOutputPath); } catch { }
                    System.Diagnostics.Debug.WriteLine($"[VideoProcessor] 音频合并失败，使用无音频版本");
                }
            }
            else
            {
                File.Copy(tempOutputPath, outputPath, overwrite: true);
                try { File.Delete(tempOutputPath); } catch { }
            }

            // 验证输出文件
            if (File.Exists(outputPath))
            {
                System.Diagnostics.Debug.WriteLine($"[VideoProcessor] 输出文件成功: {outputPath}, 大小: {new FileInfo(outputPath).Length} bytes");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[VideoProcessor] 输出文件不存在: {outputPath}");
            }

            ReportStatus(progress, 1.0, "处理完成");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VideoProcessor] 处理失败: {ex.Message}\n{ex.StackTrace}");
            ReportStatus(progress, -1, $"处理失败: {ex.Message}");
            return false;
        }
        finally
        {
            _isProcessing = false;
            
            // 清理临时文件
            if (!string.IsNullOrEmpty(tempOutputPath) && File.Exists(tempOutputPath))
            {
                try { File.Delete(tempOutputPath); } catch { }
            }
        }
    }

    /// <summary>取消处理</summary>
    public void Cancel()
    {
        _cts?.Cancel();
        _isProcessing = false;
    }
    
    /// <summary>重置取消状态</summary>
    public void Reset()
    {
        _cts?.Dispose();
        _cts = null;
        _isProcessing = false;
    }

    /// <summary>是否正在处理</summary>
    public bool IsProcessing => _isProcessing;

    private void ReportStatus(IProgress<double>? progress, double value, string message)
    {
        System.Diagnostics.Debug.WriteLine($"[VideoProcessor] {value:P0} - {message}");
        progress?.Report(value);
    }
}
