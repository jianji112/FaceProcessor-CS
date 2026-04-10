#define TRACE
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FaceProcessor.Models;
using OpenCvSharp;

namespace FaceProcessor.Services;

public class VideoProcessor
{
	private const int MaxDetectionEdge = 1920;

	private static readonly (string Codec, string Label)[] WriterCodecCandidates = new(string, string)[3]
	{
		("mp4v", "MPEG-4"),
		("avc1", "H.264"),
		("H264", "H.264 alt")
	};

	private readonly FaceDetector? _detector;

	private readonly ImageProcessor _processor;

	private CancellationTokenSource? _cts;

	private bool _isProcessing;

	public bool IsProcessing => _isProcessing;

	public VideoProcessor(FaceDetector? detector = null)
	{
		_detector = detector;
		_processor = new ImageProcessor(detector);
	}

	public async Task<bool> ProcessVideoAsync(string inputPath, string outputPath, ProcessMode mode, ProcessOptions options, IProgress<double>? progress = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		_cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		_isProcessing = true;
		string tempOutputPath = string.Empty;
		try
		{
			Trace.WriteLine("[VideoProcessor] Start: " + inputPath);
			tempOutputPath = Path.Combine(Path.GetDirectoryName(outputPath) ?? string.Empty, Path.GetFileNameWithoutExtension(outputPath) + "_temp" + Path.GetExtension(outputPath));
			string outputDirectory = Path.GetDirectoryName(outputPath);
			if (!string.IsNullOrWhiteSpace(outputDirectory))
			{
				Directory.CreateDirectory(outputDirectory);
			}
			if (!(await Task.Run(() => ProcessFrames(inputPath, tempOutputPath, mode, options, progress, _cts.Token), _cts.Token)))
			{
				return false;
			}
			ReportStatus(progress, 0.95, "Merging audio...");
			if (options.KeepAudio)
			{
				if (await AudioMerger.MergeAudioAsync(inputPath, tempOutputPath, outputPath, _cts.Token))
				{
					TryDelete(tempOutputPath);
					Trace.WriteLine("[VideoProcessor] Audio merged successfully");
				}
				else
				{
					File.Copy(tempOutputPath, outputPath, overwrite: true);
					TryDelete(tempOutputPath);
					Trace.WriteLine("[VideoProcessor] Audio merge failed, kept the silent video");
				}
			}
			else
			{
				File.Copy(tempOutputPath, outputPath, overwrite: true);
				TryDelete(tempOutputPath);
			}
			Trace.WriteLine(File.Exists(outputPath) ? $"[VideoProcessor] Output saved: {outputPath} ({new FileInfo(outputPath).Length} bytes)" : ("[VideoProcessor] Output missing after processing: " + outputPath));
			ReportStatus(progress, 1.0, "Done");
			return true;
		}
		catch (OperationCanceledException)
		{
			Trace.WriteLine("[VideoProcessor] Cancelled by user");
			ReportStatus(progress, -1.0, "Cancelled");
			return false;
		}
		catch (Exception ex2)
		{
			Trace.WriteLine($"[VideoProcessor] Failed: {ex2}");
			ReportStatus(progress, -1.0, "Failed: " + ex2.Message);
			return false;
		}
		finally
		{
			_isProcessing = false;
			if (!string.IsNullOrWhiteSpace(tempOutputPath) && File.Exists(tempOutputPath))
			{
				TryDelete(tempOutputPath);
			}
		}
	}

	public void Cancel()
	{
		_cts?.Cancel();
		_isProcessing = false;
	}

	public void Reset()
	{
		_cts?.Dispose();
		_cts = null;
		_isProcessing = false;
	}

	private static void TryDelete(string path)
	{
		try
		{
			File.Delete(path);
		}
		catch
		{
		}
	}

	private static void ReportStatus(IProgress<double>? progress, double value, string message)
	{
		Trace.WriteLine($"[VideoProcessor] {value:P0} - {message}");
		progress?.Report(value);
	}

	private bool ProcessFrames(string inputPath, string tempOutputPath, ProcessMode mode, ProcessOptions options, IProgress<double>? progress, CancellationToken cancellationToken)
	{
		using VideoCapture capture = new VideoCapture(inputPath);
		if (!capture.IsOpened())
		{
			Trace.WriteLine("[VideoProcessor] Cannot open input video: " + inputPath);
			ReportStatus(progress, -1.0, "Unable to open the input video.");
			return false;
		}
		double fps = capture.Get(VideoCaptureProperties.Fps);
		int width = (int)capture.Get(VideoCaptureProperties.FrameWidth);
		int height = (int)capture.Get(VideoCaptureProperties.FrameHeight);
		int totalFrames = (int)capture.Get(VideoCaptureProperties.FrameCount);
		double normalizedFps = NormalizeFps(fps);
		Size sourceSize = new Size(width, height);
		Size outputSize = GetOutputSize(width, height, options.OutputResolution);
		Size detectionSize = GetDetectionSize(sourceSize);
		Trace.WriteLine($"[VideoProcessor] Video info: {width}x{height}, output {outputSize.Width}x{outputSize.Height}, detect {detectionSize.Width}x{detectionSize.Height}, {normalizedFps:F1} fps, {totalFrames} frames");
		ReportStatus(progress, 0.0, $"Video loaded: {width}x{height} -> {outputSize.Width}x{outputSize.Height}, {normalizedFps:F1} fps");
		using VideoWriter writer = new VideoWriter();
		if (!TryOpenWriter(writer, tempOutputPath, normalizedFps, outputSize, out string selectedCodec))
		{
			Trace.WriteLine("[VideoProcessor] Cannot create output file: " + tempOutputPath);
			ReportStatus(progress, -1.0, "Unable to create the output video.");
			return false;
		}
		Trace.WriteLine("[VideoProcessor] Writer codec: " + selectedCodec);
		int frameIndex = 0;
		int faceCount = 0;
		int lastReportedFrame = 0;
		Stopwatch progressStopwatch = Stopwatch.StartNew();
		using Mat frame = new Mat();
		using Mat resizedFrame = new Mat();
		using Mat detectionFrame = new Mat();
		while (capture.Read(frame))
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (frame.Empty())
			{
				break;
			}
			List<FaceRect> faces = DetectFaces(frame, detectionSize, detectionFrame, options.FaceThreshold);
			faceCount += faces.Count;
			Mat workingFrame = PrepareWorkingFrame(frame, outputSize, resizedFrame);
			List<FaceRect> scaledFaces = ScaleFaces(faces, outputSize, sourceSize);
			using Mat processed = _processor.Process(workingFrame, scaledFaces, mode, options);
			writer.Write(processed);
			frameIndex++;
			if (ShouldReportProgress(frameIndex, totalFrames, lastReportedFrame, progressStopwatch.ElapsedMilliseconds))
			{
				lastReportedFrame = frameIndex;
				progressStopwatch.Restart();
				double progressValue = ((totalFrames > 0) ? ((double)frameIndex / (double)totalFrames) : 0.0);
				ReportStatus(progress, progressValue, $"Processing frame {frameIndex}/{totalFrames}, faces {scaledFaces.Count}");
			}
		}
		writer.Release();
		capture.Release();
		Trace.WriteLine($"[VideoProcessor] Frame pass done. Frames={frameIndex}, faces={faceCount}");
		return true;
	}

	private List<FaceRect> DetectFaces(Mat workingFrame, Size detectionSize, Mat detectionFrame, float faceThreshold)
	{
		if (_detector == null)
		{
			return new List<FaceRect>();
		}
		if (workingFrame.Width == detectionSize.Width && workingFrame.Height == detectionSize.Height)
		{
			return _detector.Detect(workingFrame, faceThreshold);
		}
		Cv2.Resize(workingFrame, detectionFrame, detectionSize);
		return ScaleFaces(_detector.Detect(detectionFrame, faceThreshold), new Size(workingFrame.Width, workingFrame.Height), detectionSize);
	}

	private static Mat PrepareWorkingFrame(Mat source, Size outputSize, Mat resizedFrame)
	{
		if (source.Width == outputSize.Width && source.Height == outputSize.Height)
		{
			return source;
		}
		Cv2.Resize(source, resizedFrame, outputSize, 0.0, 0.0, InterpolationFlags.Area);
		return resizedFrame;
	}

	private static bool TryOpenWriter(VideoWriter writer, string outputPath, double fps, Size frameSize, out string selectedCodec)
	{
		(string, string)[] writerCodecCandidates = WriterCodecCandidates;
		for (int i = 0; i < writerCodecCandidates.Length; i++)
		{
			(string, string) candidate = writerCodecCandidates[i];
			writer.Release();
			writer.Open(outputPath, FourCC.FromString(candidate.Item1), fps, frameSize);
			if (writer.IsOpened())
			{
				selectedCodec = candidate.Item2;
				return true;
			}
		}
		selectedCodec = string.Empty;
		return false;
	}

	private static double NormalizeFps(double fps)
	{
		if (!double.IsFinite(fps) || !(fps > 1.0))
		{
			return 25.0;
		}
		return fps;
	}

	private static Size GetOutputSize(int sourceWidth, int sourceHeight, VideoOutputResolution outputResolution)
	{
		int targetShortEdge = outputResolution switch
		{
			VideoOutputResolution.P1080 => 1080, 
			VideoOutputResolution.P720 => 720, 
			_ => 0, 
		};
		if (targetShortEdge <= 0)
		{
			return NormalizeFrameSize(new Size(sourceWidth, sourceHeight));
		}
		int sourceShortEdge = Math.Min(sourceWidth, sourceHeight);
		if (sourceShortEdge <= targetShortEdge)
		{
			return NormalizeFrameSize(new Size(sourceWidth, sourceHeight));
		}
		double scale = (double)targetShortEdge / (double)sourceShortEdge;
		int width = (int)Math.Round((double)sourceWidth * scale);
		int scaledHeight = (int)Math.Round((double)sourceHeight * scale);
		return NormalizeFrameSize(new Size(width, scaledHeight));
	}

	private static Size GetDetectionSize(Size workingSize)
	{
		int maxEdge = Math.Max(workingSize.Width, workingSize.Height);
		if (maxEdge <= 1920)
		{
			return workingSize;
		}
		double scale = 1920.0 / (double)maxEdge;
		int width = (int)Math.Round((double)workingSize.Width * scale);
		int scaledHeight = (int)Math.Round((double)workingSize.Height * scale);
		return NormalizeFrameSize(new Size(width, scaledHeight));
	}

	private static Size NormalizeFrameSize(Size size)
	{
		return new Size(NormalizeDimension(size.Width), NormalizeDimension(size.Height));
	}

	private static int NormalizeDimension(int value)
	{
		if (value <= 2)
		{
			return Math.Max(1, value);
		}
		if (value % 2 != 0)
		{
			return value - 1;
		}
		return value;
	}

	private static List<FaceRect> ScaleFaces(List<FaceRect> faces, Size targetSize, Size sourceSize)
	{
		if (faces.Count == 0)
		{
			return faces;
		}
		double scaleX = (double)targetSize.Width / (double)sourceSize.Width;
		double scaleY = (double)targetSize.Height / (double)sourceSize.Height;
		List<FaceRect> scaledFaces = new List<FaceRect>(faces.Count);
		foreach (FaceRect face in faces)
		{
			int x = Math.Clamp((int)Math.Round((double)face.X * scaleX), 0, Math.Max(0, targetSize.Width - 1));
			int y = Math.Clamp((int)Math.Round((double)face.Y * scaleY), 0, Math.Max(0, targetSize.Height - 1));
			int width = Math.Max(1, (int)Math.Round((double)face.Width * scaleX));
			int height = Math.Max(1, (int)Math.Round((double)face.Height * scaleY));
			if (x + width > targetSize.Width)
			{
				width = Math.Max(1, targetSize.Width - x);
			}
			if (y + height > targetSize.Height)
			{
				height = Math.Max(1, targetSize.Height - y);
			}
			scaledFaces.Add(new FaceRect(x, y, width, height, face.Confidence));
		}
		return scaledFaces;
	}

	private static bool ShouldReportProgress(int frameIndex, int totalFrames, int lastReportedFrame, long elapsedMilliseconds)
	{
		if (frameIndex <= 1 || frameIndex == totalFrames)
		{
			return true;
		}
		if (frameIndex - lastReportedFrame >= 8)
		{
			return true;
		}
		return elapsedMilliseconds >= 200;
	}
}
