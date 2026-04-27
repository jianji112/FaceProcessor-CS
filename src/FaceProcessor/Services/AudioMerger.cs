using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

namespace FaceProcessor.Services;

public static class AudioMerger
{
	private static bool _ffmpegInitialized = false;

	private static readonly object _lock = new object();

	public static async Task InitializeAsync()
	{
		if (_ffmpegInitialized)
		{
			return;
		}
		lock (_lock)
		{
			if (_ffmpegInitialized)
			{
				return;
			}
		}
		try
		{
			string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg");
			if (!HasLocalFfmpeg(ffmpegPath))
			{
				await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official, ffmpegPath);
			}
			FFmpeg.SetExecutablesPath(ffmpegPath);
			_ffmpegInitialized = true;
		}
		catch (Exception ex)
		{
			Console.WriteLine("FFmpeg 初始化失败: " + ex.Message);
			throw;
		}
	}

	public static async Task<bool> MergeAudioAsync(string sourceVideo, string processedVideo, string outputVideo, CancellationToken cancellationToken = default(CancellationToken))
	{
		_ = 3;
		try
		{
			await InitializeAsync();
			IMediaInfo sourceInfo = await FFmpeg.GetMediaInfo(sourceVideo, cancellationToken);
			if (!sourceInfo.AudioStreams.Any())
			{
				File.Copy(processedVideo, outputVideo, overwrite: true);
				return true;
			}
			IAudioStream audioStream = sourceInfo.AudioStreams.First();
			IVideoStream videoStream = (await FFmpeg.GetMediaInfo(processedVideo, cancellationToken)).VideoStreams.First();
			await FFmpeg.Conversions.New().AddStream<IVideoStream>(videoStream).AddStream<IAudioStream>(audioStream)
				.SetOutput(outputVideo)
				.SetOverwriteOutput(overwrite: true)
				.UseMultiThread(multiThread: true)
				.Start(cancellationToken);
			return true;
		}
		catch (OperationCanceledException)
		{
			return false;
		}
		catch (Exception ex2)
		{
			Console.WriteLine("音频合并失败: " + ex2.Message);
			return false;
		}
	}

	public static async Task<bool> HasAudioAsync(string videoPath)
	{
		_ = 1;
		try
		{
			await InitializeAsync();
			return (await FFmpeg.GetMediaInfo(videoPath)).AudioStreams.Any();
		}
		catch
		{
			return false;
		}
	}

	public static async Task<VideoInfo?> GetVideoInfoAsync(string videoPath)
	{
		_ = 1;
		try
		{
			await InitializeAsync();
			IMediaInfo info = await FFmpeg.GetMediaInfo(videoPath);
			return new VideoInfo
			{
				Duration = info.Duration,
				Width = (info.VideoStreams.FirstOrDefault()?.Width ?? 0),
				Height = (info.VideoStreams.FirstOrDefault()?.Height ?? 0),
				FrameRate = (info.VideoStreams.FirstOrDefault()?.Framerate ?? 0.0),
				HasAudio = info.AudioStreams.Any(),
				AudioCodec = (info.AudioStreams.FirstOrDefault()?.Codec ?? ""),
				VideoCodec = (info.VideoStreams.FirstOrDefault()?.Codec ?? "")
			};
		}
		catch
		{
			return null;
		}
	}

	private static bool HasLocalFfmpeg(string ffmpegPath)
	{
		if (File.Exists(Path.Combine(ffmpegPath, "ffmpeg.exe")))
		{
			return File.Exists(Path.Combine(ffmpegPath, "ffprobe.exe"));
		}
		return false;
	}
}
