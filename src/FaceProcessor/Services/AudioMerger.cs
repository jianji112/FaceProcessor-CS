using System.IO;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

namespace FaceProcessor.Services;

/// <summary>
/// 音频合并器 - 使用 Xabe.FFmpeg 合并音频轨道
/// </summary>
public static class AudioMerger
{
    private static bool _ffmpegInitialized = false;
    private static readonly object _lock = new();

    /// <summary>
    /// 初始化 FFmpeg（首次使用时下载）
    /// </summary>
    public static async Task InitializeAsync()
    {
        if (_ffmpegInitialized) return;

        lock (_lock)
        {
            if (_ffmpegInitialized) return;
        }

        try
        {
            // 下载 FFmpeg 到应用目录
            var ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg");
            await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official, ffmpegPath);
            
            // 设置 FFmpeg 路径
            FFmpeg.SetExecutablesPath(ffmpegPath);
            
            _ffmpegInitialized = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FFmpeg 初始化失败: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 从原视频提取音频并合并到处理后的视频
    /// </summary>
    /// <param name="sourceVideo">原视频路径</param>
    /// <param name="processedVideo">处理后的视频路径</param>
    /// <param name="outputVideo">最终输出路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否成功</returns>
    public static async Task<bool> MergeAudioAsync(
        string sourceVideo,
        string processedVideo,
        string outputVideo,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await InitializeAsync();

            // 检查原视频是否有音频
            var sourceInfo = await FFmpeg.GetMediaInfo(sourceVideo, cancellationToken);
            var hasAudio = sourceInfo.AudioStreams.Any();

            if (!hasAudio)
            {
                // 没有音频，直接复制处理后的视频
                File.Copy(processedVideo, outputVideo, overwrite: true);
                return true;
            }

            // 获取音频流
            var audioStream = sourceInfo.AudioStreams.First();

            // 获取处理后的视频流
            var processedInfo = await FFmpeg.GetMediaInfo(processedVideo, cancellationToken);
            var videoStream = processedInfo.VideoStreams.First();

            // 创建转换
            var conversion = FFmpeg.Conversions.New()
                .AddStream(videoStream)
                .AddStream(audioStream)
                .SetOutput(outputVideo)
                .SetOverwriteOutput(true)
                .UseMultiThread(true);

            // 执行转换
            await conversion.Start(cancellationToken);

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"音频合并失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 检查视频是否有音频轨道
    /// </summary>
    public static async Task<bool> HasAudioAsync(string videoPath)
    {
        try
        {
            await InitializeAsync();
            var info = await FFmpeg.GetMediaInfo(videoPath);
            return info.AudioStreams.Any();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 获取视频信息
    /// </summary>
    public static async Task<VideoInfo?> GetVideoInfoAsync(string videoPath)
    {
        try
        {
            await InitializeAsync();
            var info = await FFmpeg.GetMediaInfo(videoPath);
            
            return new VideoInfo
            {
                Duration = info.Duration,
                Width = info.VideoStreams.FirstOrDefault()?.Width ?? 0,
                Height = info.VideoStreams.FirstOrDefault()?.Height ?? 0,
                FrameRate = info.VideoStreams.FirstOrDefault()?.Framerate ?? 0,
                HasAudio = info.AudioStreams.Any(),
                AudioCodec = info.AudioStreams.FirstOrDefault()?.Codec ?? "",
                VideoCodec = info.VideoStreams.FirstOrDefault()?.Codec ?? ""
            };
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// 视频信息
/// </summary>
public class VideoInfo
{
    public TimeSpan Duration { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public double FrameRate { get; set; }
    public bool HasAudio { get; set; }
    public string AudioCodec { get; set; } = "";
    public string VideoCodec { get; set; } = "";
}
