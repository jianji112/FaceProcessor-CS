#define TRACE
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using FaceProcessor.Models;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using OpenCvSharp.Dnn;

namespace FaceProcessor.Services;

public class FaceDetector : IDisposable
{
	private readonly InferenceSession? _onnxSession;

	private readonly Net? _caffeNet;

	private readonly CascadeClassifier? _cascadeClassifier;

	private readonly int _inputSize = 300;

	private readonly float _confidenceThreshold;

	private readonly float _nmsThreshold = 0.45f;

	private readonly bool _useCaffe;

	private bool _primaryEnabled;

	private bool _disposed;

	public bool IsGpuActive { get; }

	public string BackendDescription { get; } = "CPU";

	public FaceDetector(string? modelPath, float confidenceThreshold = 0.5f, bool useGpu = false, string? cascadePath = null)
	{
		_confidenceThreshold = confidenceThreshold;
		if (!string.IsNullOrWhiteSpace(modelPath))
		{
			if (modelPath.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase))
			{
				bool isGpuActive;
				string backendDescription;
				using SessionOptions sessionOptions = CreateSessionOptions(useGpu, out isGpuActive, out backendDescription);
				_onnxSession = new InferenceSession(modelPath, sessionOptions);
				IsGpuActive = isGpuActive;
				BackendDescription = backendDescription;
				_primaryEnabled = true;
				Trace.WriteLine($"[FaceDetector] Loaded ONNX model: {modelPath} ({BackendDescription})");
			}
			else
			{
				if (!modelPath.EndsWith(".caffemodel", StringComparison.OrdinalIgnoreCase))
				{
					throw new NotSupportedException("Unsupported model format: " + modelPath);
				}
				string protoPath = Path.Combine(Path.GetDirectoryName(modelPath), "deploy.prototxt");
				if (!File.Exists(protoPath))
				{
					throw new FileNotFoundException("Caffe prototxt not found: " + protoPath);
				}
				_caffeNet = CvDnn.ReadNetFromCaffe(protoPath, modelPath);
				if (useGpu && _caffeNet != null)
				{
					BackendDescription = ((IsGpuActive = TryConfigureCaffeGpu(_caffeNet, out string gpuBackend)) ? gpuBackend : "Caffe / CPU");
				}
				else
				{
					IsGpuActive = false;
					BackendDescription = "Caffe / CPU";
				}
				_useCaffe = true;
				_primaryEnabled = true;
				Trace.WriteLine($"[FaceDetector] Loaded Caffe model: {modelPath} ({BackendDescription})");
			}
		}
		if (!string.IsNullOrWhiteSpace(cascadePath) && File.Exists(cascadePath))
		{
			_cascadeClassifier = new CascadeClassifier(cascadePath);
			if (_cascadeClassifier.Empty())
			{
				_cascadeClassifier.Dispose();
				throw new InvalidOperationException("Failed to load cascade file: " + cascadePath);
			}
			if (!_primaryEnabled)
			{
				BackendDescription = "Cascade / CPU";
			}
			Trace.WriteLine("[FaceDetector] Loaded Haar cascade fallback: " + cascadePath);
		}
	}

	public List<FaceRect> Detect(Mat image, float? confidenceThresholdOverride = null)
	{
		if (image.Empty())
		{
			return new List<FaceRect>();
		}
		float effectiveThreshold = confidenceThresholdOverride ?? _confidenceThreshold;
		if (_primaryEnabled)
		{
			try
			{
				List<FaceRect> primaryFaces = (_useCaffe ? DetectWithCaffe(image, effectiveThreshold) : DetectWithOnnx(image, effectiveThreshold));
				if (primaryFaces.Count > 0)
				{
					return primaryFaces;
				}
			}
			catch (Exception value)
			{
				_primaryEnabled = false;
				Trace.WriteLine($"[FaceDetector] Primary detector failed and was disabled: {value}");
			}
		}
		return DetectWithCascade(image);
	}

	private List<FaceRect> DetectWithOnnx(Mat image, float confidenceThreshold)
	{
		if (_onnxSession == null)
		{
			return new List<FaceRect>();
		}
		string inputName = _onnxSession.InputMetadata.Keys.FirstOrDefault() ?? "input";
		DenseTensor<float> inputTensor = Preprocess(image);
		using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = _onnxSession.Run((IReadOnlyCollection<NamedOnnxValue>)(object)new NamedOnnxValue[1] { NamedOnnxValue.CreateFromTensor(inputName, inputTensor) });
		DisposableNamedOnnxValue disposableNamedOnnxValue = results.First();
		float[] output = disposableNamedOnnxValue.AsEnumerable<float>().ToArray();
		int[] dims = disposableNamedOnnxValue.AsTensor<float>().Dimensions.ToArray();
		return Postprocess(output, dims, image.Width, image.Height, confidenceThreshold);
	}

	private List<FaceRect> DetectWithCaffe(Mat image, float confidenceThreshold)
	{
		if (_caffeNet == null)
		{
			return new List<FaceRect>();
		}
		using Mat blob = CvDnn.BlobFromImage(image, 1.0, new Size(_inputSize, _inputSize), new Scalar(104.0, 177.0, 123.0), swapRB: false, crop: false);
		_caffeNet.SetInput(blob);
		using Mat detections = _caffeNet.Forward();
		if (detections.Empty() || detections.Dims < 4 || detections.Size(3) < 7)
		{
			throw new InvalidOperationException($"Unexpected Caffe output shape: dims={detections.Dims}, size=[{string.Join(",", Enumerable.Range(0, detections.Dims).Select(detections.Size))}]");
		}
		List<FaceRect> faces = new List<FaceRect>();
		for (int i = 0; i < detections.Size(2); i++)
		{
			float confidence = detections.At<float>(new int[4] { 0, 0, i, 2 });
			if (!(confidence < confidenceThreshold))
			{
				int x1 = (int)(detections.At<float>(new int[4] { 0, 0, i, 3 }) * (float)image.Width);
				int y1 = (int)(detections.At<float>(new int[4] { 0, 0, i, 4 }) * (float)image.Height);
				int x2 = (int)(detections.At<float>(new int[4] { 0, 0, i, 5 }) * (float)image.Width);
				int y2 = (int)(detections.At<float>(new int[4] { 0, 0, i, 6 }) * (float)image.Height);
				x1 = Math.Max(0, x1);
				y1 = Math.Max(0, y1);
				x2 = Math.Min(image.Width, x2);
				y2 = Math.Min(image.Height, y2);
				if (x2 > x1 && y2 > y1)
				{
					faces.Add(new FaceRect(x1, y1, x2 - x1, y2 - y1, confidence));
				}
			}
		}
		return Nms(faces);
	}

	private List<FaceRect> DetectWithCascade(Mat image)
	{
		if (_cascadeClassifier == null)
		{
			return new List<FaceRect>();
		}
		using Mat gray = new Mat();
		Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
		Cv2.EqualizeHist(gray, gray);
		return (from rect in _cascadeClassifier.DetectMultiScale(gray, 1.1, 4, HaarDetectionTypes.ScaleImage, new Size(24, 24))
			select new FaceRect(rect.X, rect.Y, rect.Width, rect.Height, 0.6f)).ToList();
	}

	private DenseTensor<float> Preprocess(Mat image)
	{
		using Mat blob = CvDnn.BlobFromImage(image, 1.0 / 255.0, new Size(_inputSize, _inputSize), default(Scalar), swapRB: true, crop: false);
		float[] buffer = new float[3 * _inputSize * _inputSize];
		Marshal.Copy(blob.Data, buffer, 0, buffer.Length);
		return new DenseTensor<float>(buffer, new int[4] { 1, 3, _inputSize, _inputSize });
	}

	private List<FaceRect> Postprocess(float[] output, int[] dims, int originalWidth, int originalHeight, float confidenceThreshold)
	{
		List<FaceRect> faces = new List<FaceRect>();
		if (dims.Length != 4 || dims[0] != 1 || dims[1] != 1 || dims[3] != 7)
		{
			Trace.WriteLine("[FaceDetector] Unsupported ONNX output dims: [" + string.Join(",", dims) + "]");
			return faces;
		}
		int numDetections = dims[2];
		for (int i = 0; i < numDetections; i++)
		{
			int offset = i * 7;
			float confidence = output[offset + 2];
			if (!(confidence < confidenceThreshold))
			{
				int x1 = Math.Max(0, (int)(output[offset + 3] * (float)originalWidth));
				int y1 = Math.Max(0, (int)(output[offset + 4] * (float)originalHeight));
				int x2 = Math.Min(originalWidth, (int)(output[offset + 5] * (float)originalWidth));
				int y2 = Math.Min(originalHeight, (int)(output[offset + 6] * (float)originalHeight));
				if (x2 > x1 && y2 > y1)
				{
					faces.Add(new FaceRect(x1, y1, x2 - x1, y2 - y1, confidence));
				}
			}
		}
		return Nms(faces);
	}

	private List<FaceRect> Nms(List<FaceRect> boxes)
	{
		if (boxes.Count == 0)
		{
			return boxes;
		}
		List<FaceRect> remaining = boxes.OrderByDescending((FaceRect box) => box.Confidence).ToList();
		List<FaceRect> result = new List<FaceRect>();
		while (remaining.Count > 0)
		{
			FaceRect best = remaining[0];
			result.Add(best);
			remaining.RemoveAt(0);
			remaining = remaining.Where((FaceRect candidate) => IoU(best, candidate) < _nmsThreshold).ToList();
		}
		return result;
	}

	private static float IoU(FaceRect a, FaceRect b)
	{
		int x1 = Math.Max(a.X, b.X);
		int y1 = Math.Max(a.Y, b.Y);
		int x2 = Math.Min(a.X + a.Width, b.X + b.Width);
		int y2 = Math.Min(a.Y + a.Height, b.Y + b.Height);
		int intersection = Math.Max(0, x2 - x1) * Math.Max(0, y2 - y1);
		int union = a.Width * a.Height + b.Width * b.Height - intersection;
		if (union <= 0)
		{
			return 0f;
		}
		return (float)intersection / (float)union;
	}

	public static (bool available, string info) GetGpuStatus()
	{
		try
		{
			using SessionOptions options = new SessionOptions();
			string info;
			return TryEnableDirectMl(options, out info) ? (available: true, info: info) : (available: false, info: "CPU mode");
		}
		catch
		{
			return (available: false, info: "CPU mode");
		}
	}

	private static SessionOptions CreateSessionOptions(bool useGpu, out bool isGpuActive, out string backendDescription)
	{
		SessionOptions sessionOptions = new SessionOptions
		{
			GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
		};
		if (useGpu && TryEnableDirectMl(sessionOptions, out string gpuInfo))
		{
			sessionOptions.AppendExecutionProvider_CPU();
			isGpuActive = true;
			backendDescription = gpuInfo;
			return sessionOptions;
		}
		sessionOptions.AppendExecutionProvider_CPU();
		isGpuActive = false;
		backendDescription = "CPU";
		return sessionOptions;
	}

	private static bool TryEnableDirectMl(SessionOptions sessionOptions, out string info)
	{
		try
		{
			sessionOptions.AppendExecutionProvider_DML();
			info = "DirectML";
			return true;
		}
		catch (Exception ex)
		{
			Trace.WriteLine("[FaceDetector] DirectML unavailable, falling back to CPU: " + ex.Message);
			info = ex.Message;
			return false;
		}
	}

	private static bool TryConfigureCaffeGpu(Net net, out string backendDescription)
	{
		try
		{
			net.SetPreferableBackend(Backend.OPENCV);
			net.SetPreferableTarget(Target.OPENCL_FP16);
			backendDescription = "OpenCL FP16 / GPU";
			Trace.WriteLine("[FaceDetector] Caffe backend configured for OpenCL FP16");
			return true;
		}
		catch (Exception ex)
		{
			Trace.WriteLine("[FaceDetector] OpenCL FP16 unavailable: " + ex.Message);
		}
		try
		{
			net.SetPreferableBackend(Backend.OPENCV);
			net.SetPreferableTarget(Target.OPENCL);
			backendDescription = "OpenCL / GPU";
			Trace.WriteLine("[FaceDetector] Caffe backend configured for OpenCL");
			return true;
		}
		catch (Exception ex2)
		{
			Trace.WriteLine("[FaceDetector] OpenCL unavailable, falling back to CPU: " + ex2.Message);
			backendDescription = "Caffe / CPU";
			return false;
		}
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_onnxSession?.Dispose();
			_caffeNet?.Dispose();
			_cascadeClassifier?.Dispose();
			_disposed = true;
		}
	}
}
