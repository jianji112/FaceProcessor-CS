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
                
                // 打印模型输入/输出名称，帮助调试
                System.Diagnostics.Debug.WriteLine($"[FaceDetector] ONNX 模型加载成功: {modelPath}");
                System.Diagnostics.Debug.WriteLine($"[FaceDetector] 输入数量: {_onnxSession.InputMetadata.Count}");
                foreach (var input in _onnxSession.InputMetadata)
                {
                    System.Diagnostics.Debug.WriteLine($"[FaceDetector]   输入: name={input.Key}, shape=[{string.Join(",", input.Value.Dimensions)}], type={input.Value.ElementDataType}");
                }
                System.Diagnostics.Debug.WriteLine($"[FaceDetector] 输出数量: {_onnxSession.OutputMetadata.Count}");
                foreach (var output in _onnxSession.OutputMetadata)
                {
                    System.Diagnostics.Debug.WriteLine($"[FaceDetector]   输出: name={output.Key}, shape=[{string.Join(",", output.Value.Dimensions)}], type={output.Value.ElementDataType}");
                }
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

        // 获取模型的第一个输入名称
        var inputName = _onnxSession.InputMetadata.Keys.FirstOrDefault() ?? "input";
        var outputName = _onnxSession.OutputMetadata.Keys.FirstOrDefault() ?? "output";
        
        var inputTensor = Preprocess(image);
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(inputName, inputTensor)
        };

        using var results = _onnxSession.Run(inputs);
        
        // 使用实际输出名称获取结果
        float[] output = Array.Empty<float>();
        int[] outputDims = Array.Empty<int>();
        bool foundOutput = false;
        if (_onnxSession.OutputMetadata.Count > 1)
        {
            // 多输出：找第一个包含检测数据的输出
            foreach (var name in _onnxSession.OutputMetadata.Keys)
            {
                var val = results.FirstOrDefault(r => r.Name == name);
                if (val != null)
                {
                    outputName = name;
                    output = val.AsEnumerable<float>().ToArray();
                    outputDims = val.AsTensor<float>().Dimensions.ToArray();
                    foundOutput = true;
                    break;
                }
            }
        }
        
        if (!foundOutput)
        {
            var firstResult = results.First();
            output = firstResult.AsEnumerable<float>().ToArray();
            outputDims = firstResult.AsTensor<float>().Dimensions.ToArray();
        }
        
        System.Diagnostics.Debug.WriteLine($"[FaceDetector] ONNX 推理完成，输出名称: {outputName}, 维度: [{string.Join(",", outputDims)}]");
        
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

    /// <summary>
    /// UltraFace ONNX 输出后处理
    /// 输出格式: [1, 1, num_detections, 7]
    /// 每个检测: [batch_idx, class_id, confidence, x1, y1, x2, y2] (x1,y1,x2,y2 归一化到[0,1])
    /// </summary>
    private List<FaceRect> Postprocess(float[] output, int[] dims, int originalWidth, int originalHeight)
    {
        var faces = new List<FaceRect>();

        if (dims.Length != 4 || dims[0] != 1 || dims[1] != 1)
        {
            System.Diagnostics.Debug.WriteLine($"[FaceDetector] Postprocess: 不支持的输出维度 [{string.Join(",", dims)}]");
            return faces;
        }

        int numDetections = dims[2];
        int valuesPerDetection = dims[3];

        System.Diagnostics.Debug.WriteLine($"[FaceDetector] Postprocess: numDetections={numDetections}, valuesPerDetection={valuesPerDetection}");

        if (valuesPerDetection != 7)
        {
            System.Diagnostics.Debug.WriteLine($"[FaceDetector] Postprocess: 期望7值/检测，当前{valuesPerDetection}，跳过");
            return faces;
        }

        for (int i = 0; i < numDetections; i++)
        {
            int offset = i * 7;
            float confidence = output[offset + 2];
            if (confidence < _confidenceThreshold) continue;

            float x1Norm = output[offset + 3];
            float y1Norm = output[offset + 4];
            float x2Norm = output[offset + 5];
            float y2Norm = output[offset + 6];

            // 归一化坐标转像素坐标
            int x1 = (int)(x1Norm * originalWidth);
            int y1 = (int)(y1Norm * originalHeight);
            int x2 = (int)(x2Norm * originalWidth);
            int y2 = (int)(y2Norm * originalHeight);

            // 边界检查
            x1 = Math.Max(0, x1);
            y1 = Math.Max(0, y1);
            x2 = Math.Min(originalWidth, x2);
            y2 = Math.Min(originalHeight, y2);

            int width = x2 - x1;
            int height = y2 - y1;

            if (width > 0 && height > 0)
            {
                faces.Add(new FaceRect(x1, y1, width, height, confidence));
            }
        }

        System.Diagnostics.Debug.WriteLine($"[FaceDetector] Postprocess: 过滤后剩余 {faces.Count} 个人脸");
        return NMS(faces);
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
