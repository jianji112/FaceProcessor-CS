using System.IO;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using OpenCvSharp.Dnn;
using FaceProcessor.Models;

namespace FaceProcessor.Services;

/// <summary>
/// 人脸检测器 - 支持 ONNX 和 Caffe 模型
/// </summary>
public class FaceDetector : IDisposable
{
    private readonly InferenceSession? _onnxSession;
    private readonly Net? _caffeNet;
    private readonly int _inputSize;
    private readonly float _confidenceThreshold;
    private readonly float _nmsThreshold = 0.45f;
    private readonly bool _useCaffe;
    private bool _disposed;

    public FaceDetector(string modelPath, float confidenceThreshold = 0.5f, bool useGpu = false)
    {
        _confidenceThreshold = confidenceThreshold;
        _inputSize = 320; // UltraFace 默认输入尺寸

        // 判断模型类型
        if (modelPath.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase))
        {
            _useCaffe = false;
            var sessionOptions = new SessionOptions();
            
            if (useGpu)
            {
                try { sessionOptions.AppendExecutionProvider_CUDA(0); }
                catch { }
            }
            sessionOptions.AppendExecutionProvider_CPU();

            try
            {
                _onnxSession = new InferenceSession(modelPath, sessionOptions);
                System.Diagnostics.Debug.WriteLine($"[FaceDetector] ONNX 模型加载成功: {modelPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FaceDetector] ONNX 模型加载失败: {ex.Message}");
                throw;
            }
        }
        else if (modelPath.EndsWith(".caffemodel", StringComparison.OrdinalIgnoreCase))
        {
            _useCaffe = true;
            var protoPath = Path.Combine(Path.GetDirectoryName(modelPath)!, "deploy.prototxt");
            
            if (!File.Exists(protoPath))
            {
                throw new FileNotFoundException($"Caffe prototxt not found: {protoPath}");
            }

            try
            {
                _caffeNet = CvDnn.ReadNetFromCaffe(protoPath, modelPath);
                System.Diagnostics.Debug.WriteLine($"[FaceDetector] Caffe 模型加载成功: {modelPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FaceDetector] Caffe 模型加载失败: {ex.Message}");
                throw;
            }
        }
        else
        {
            throw new NotSupportedException($"不支持的模型格式: {modelPath}");
        }
    }

    /// <summary>检测人脸</summary>
    public List<FaceRect> Detect(Mat image)
    {
        return _useCaffe ? DetectWithCaffe(image) : DetectWithOnnx(image);
    }

    /// <summary>使用 ONNX 检测</summary>
    private List<FaceRect> DetectWithOnnx(Mat image)
    {
        if (_onnxSession == null) return new List<FaceRect>();

        var inputTensor = Preprocess(image);
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input", inputTensor)
        };

        using var results = _onnxSession.Run(inputs);
        var output = results.First().AsEnumerable<float>().ToArray();
        var outputDims = results.First().AsTensor<float>().Dimensions.ToArray();
        
        return Postprocess(output, outputDims, image.Width, image.Height);
    }

    /// <summary>使用 Caffe 检测</summary>
    private List<FaceRect> DetectWithCaffe(Mat image)
    {
        if (_caffeNet == null) return new List<FaceRect>();

        var faces = new List<FaceRect>();

        // 预处理
        var blob = CvDnn.BlobFromImage(image, 1.0, new Size(_inputSize, _inputSize), new Scalar(104, 177, 123), false, false);
        _caffeNet.SetInput(blob, "data");

        // 前向传播
        var detections = _caffeNet.Forward();

        // 解析结果
        for (int i = 0; i < detections.Size(2); i++)
        {
            var confidence = detections.At<float>(0, 0, i, 2);
            if (confidence < _confidenceThreshold) continue;

            var x1 = (int)(detections.At<float>(0, 0, i, 3) * image.Width);
            var y1 = (int)(detections.At<float>(0, 0, i, 4) * image.Height);
            var x2 = (int)(detections.At<float>(0, 0, i, 5) * image.Width);
            var y2 = (int)(detections.At<float>(0, 0, i, 6) * image.Height);

            x1 = Math.Max(0, x1);
            y1 = Math.Max(0, y1);
            x2 = Math.Min(image.Width, x2);
            y2 = Math.Min(image.Height, y2);

            if (x2 > x1 && y2 > y1)
            {
                faces.Add(new FaceRect(x1, y1, x2 - x1, y2 - y1, confidence));
            }
        }

        // NMS
        return NMS(faces);
    }

    private DenseTensor<float> Preprocess(Mat image)
    {
        var resized = new Mat();
        Cv2.Resize(image, resized, new Size(_inputSize, _inputSize));

        var rgb = new Mat();
        Cv2.CvtColor(resized, rgb, ColorConversionCodes.BGR2RGB);

        var normalized = new Mat();
        rgb.ConvertTo(normalized, MatType.CV_32FC3, 1.0 / 255.0);

        var tensor = new DenseTensor<float>(new[] { 1, 3, _inputSize, _inputSize });
        var indexer = normalized.GetGenericIndexer<Vec3f>();

        for (int y = 0; y < _inputSize; y++)
        {
            for (int x = 0; x < _inputSize; x++)
            {
                var pixel = indexer[y, x];
                tensor[0, 0, y, x] = pixel.Item0;
                tensor[0, 1, y, x] = pixel.Item1;
                tensor[0, 2, y, x] = pixel.Item2;
            }
        }

        return tensor;
    }

    private List<FaceRect> Postprocess(float[] output, int[] dims, int originalWidth, int originalHeight)
    {
        // 简化版后处理 - 实际需要根据模型调整
        return new List<FaceRect>();
    }

    private List<FaceRect> NMS(List<FaceRect> boxes)
    {
        if (boxes.Count == 0) return boxes;

        var sorted = boxes.OrderByDescending(b => b.Confidence).ToList();
        var result = new List<FaceRect>();

        while (sorted.Count > 0)
        {
            var best = sorted[0];
            result.Add(best);
            sorted.RemoveAt(0);

            sorted = sorted.Where(box => IoU(best, box) < _nmsThreshold).ToList();
        }

        return result;
    }

    private float IoU(FaceRect a, FaceRect b)
    {
        var x1 = Math.Max(a.X, b.X);
        var y1 = Math.Max(a.Y, b.Y);
        var x2 = Math.Min(a.X + a.Width, b.X + b.Width);
        var y2 = Math.Min(a.Y + a.Height, b.Y + b.Height);

        var inter = Math.Max(0, x2 - x1) * Math.Max(0, y2 - y1);
        var areaA = a.Width * a.Height;
        var areaB = b.Width * b.Height;
        var union = areaA + areaB - inter;

        return union > 0 ? inter / union : 0;
    }

    public static (bool available, string info) GetGpuStatus()
    {
        try
        {
            var testOptions = new SessionOptions();
            testOptions.AppendExecutionProvider_CUDA(0);
            return (true, "CUDA 可用");
        }
        catch
        {
            return (false, "CPU 模式");
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _onnxSession?.Dispose();
            _caffeNet?.Dispose();
            _disposed = true;
        }
    }
}
