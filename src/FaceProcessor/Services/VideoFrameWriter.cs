using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using OpenCvSharp;

namespace FaceProcessor.Services;

public interface IVideoFrameWriter : IDisposable
{
	string BackendDescription { get; }

	void Write(Mat frame);

	void Complete();
}

public sealed class FfmpegRawVideoWriter : IVideoFrameWriter
{
	private readonly Process _process;

	private readonly Stream _inputStream;

	private byte[] _buffer = [];

	private bool _completed;

	public string BackendDescription { get; } = "NVIDIA NVENC (h264_nvenc)";

	private FfmpegRawVideoWriter(Process process)
	{
		_process = process;
		_inputStream = process.StandardInput.BaseStream;
	}

	public static bool SupportsNvenc()
	{
		if (!File.Exists(AudioMerger.FfmpegExecutablePath))
		{
			return false;
		}

		try
		{
			using Process probe = new();
			probe.StartInfo = new ProcessStartInfo
			{
				FileName = AudioMerger.FfmpegExecutablePath,
				Arguments = "-hide_banner -encoders",
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true
			};
			probe.Start();
			string output = probe.StandardOutput.ReadToEnd();
			string error = probe.StandardError.ReadToEnd();
			probe.WaitForExit(5000);
			return output.Contains("h264_nvenc", StringComparison.OrdinalIgnoreCase)
				|| error.Contains("h264_nvenc", StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			return false;
		}
	}

	public static bool TryCreate(string outputPath, double fps, OpenCvSharp.Size frameSize, out IVideoFrameWriter? writer, out string? error)
	{
		writer = null;
		error = null;
		if (!SupportsNvenc())
		{
			error = "NVENC 不可用";
			return false;
		}

		Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? AppDomain.CurrentDomain.BaseDirectory);

		Process process = new();
		process.StartInfo = new ProcessStartInfo
		{
			FileName = AudioMerger.FfmpegExecutablePath,
			Arguments = BuildArguments(outputPath, fps, frameSize),
			UseShellExecute = false,
			RedirectStandardInput = true,
			RedirectStandardError = true,
			CreateNoWindow = true
		};

		process.Start();
		if (process.HasExited)
		{
			error = process.StandardError.ReadToEnd();
			process.Dispose();
			return false;
		}

		writer = new FfmpegRawVideoWriter(process);
		return true;
	}

	public void Write(Mat frame)
	{
		if (_completed)
		{
			throw new InvalidOperationException("写入器已经结束。");
		}

		Mat? converted = null;
		Mat? contiguous = null;
		try
		{
			Mat source = frame;
			if (frame.Type() != MatType.CV_8UC3)
			{
				converted = new Mat();
				if (frame.Channels() == 4)
				{
					Cv2.CvtColor(frame, converted, ColorConversionCodes.BGRA2BGR);
				}
				else if (frame.Channels() == 1)
				{
					Cv2.CvtColor(frame, converted, ColorConversionCodes.GRAY2BGR);
				}
				else
				{
					frame.ConvertTo(converted, MatType.CV_8UC3);
				}

				source = converted;
			}

			if (!source.IsContinuous())
			{
				contiguous = source.Clone();
				source = contiguous;
			}

			int byteCount = checked((int)(source.Total() * source.ElemSize()));
			if (_buffer.Length != byteCount)
			{
				_buffer = new byte[byteCount];
			}

			Marshal.Copy(source.Data, _buffer, 0, byteCount);
			_inputStream.Write(_buffer, 0, byteCount);
		}
		finally
		{
			contiguous?.Dispose();
			converted?.Dispose();
		}
	}

	public void Complete()
	{
		if (_completed)
		{
			return;
		}

		_completed = true;
		_inputStream.Flush();
		_inputStream.Close();
		_process.WaitForExit();
		if (_process.ExitCode != 0)
		{
			string error = _process.StandardError.ReadToEnd();
			throw new InvalidOperationException("FFmpeg NVENC 编码失败：" + error);
		}
	}

	public void Dispose()
	{
		try
		{
			if (!_completed)
			{
				_inputStream.Close();
			}
		}
		catch
		{
		}

		try
		{
			if (!_process.HasExited)
			{
				_process.Kill(entireProcessTree: true);
			}
		}
		catch
		{
		}

		_process.Dispose();
	}

	private static string BuildArguments(string outputPath, double fps, OpenCvSharp.Size frameSize)
	{
		string safeOutputPath = "\"" + outputPath + "\"";
		string fpsText = fps.ToString("0.###", CultureInfo.InvariantCulture);
		return $"-hide_banner -loglevel error -y -f rawvideo -pix_fmt bgr24 -s {frameSize.Width}x{frameSize.Height} -r {fpsText} -i pipe:0 -an -c:v h264_nvenc -preset p5 -pix_fmt yuv420p -movflags +faststart {safeOutputPath}";
	}
}

public sealed class OpenCvVideoFrameWriter : IVideoFrameWriter
{
	private static readonly (string Codec, string Label)[] CodecCandidates =
	[
		("mp4v", "OpenCV / MPEG-4"),
		("avc1", "OpenCV / H.264"),
		("H264", "OpenCV / H.264 alt")
	];

	private readonly VideoWriter _writer;

	public string BackendDescription { get; }

	private OpenCvVideoFrameWriter(VideoWriter writer, string backendDescription)
	{
		_writer = writer;
		BackendDescription = backendDescription;
	}

	public static bool TryCreate(string outputPath, double fps, OpenCvSharp.Size frameSize, out IVideoFrameWriter? writer, out string? error)
	{
		writer = null;
		error = null;

		VideoWriter videoWriter = new();
		foreach ((string codec, string label) in CodecCandidates)
		{
			videoWriter.Release();
			videoWriter.Open(outputPath, FourCC.FromString(codec), fps, frameSize);
			if (videoWriter.IsOpened())
			{
				writer = new OpenCvVideoFrameWriter(videoWriter, label);
				return true;
			}
		}

		error = "OpenCV VideoWriter 无法创建输出文件。";
		videoWriter.Dispose();
		return false;
	}

	public void Write(Mat frame)
	{
		_writer.Write(frame);
	}

	public void Complete()
	{
		_writer.Release();
	}

	public void Dispose()
	{
		_writer.Dispose();
	}
}
