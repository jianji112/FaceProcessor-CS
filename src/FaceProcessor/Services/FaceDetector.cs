using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using FaceProcessor.Models;

namespace FaceProcessor.Services;

/// <summary>
/// 人脸检测器 - ONNX Runtime + YOLOv8-Face
/// </summary>
public class FaceDetector : IDisposable
{
    private readonly InferenceSession _session;
    private readonly int _inputSize = 640;
    private readonly float _confidenceThreshold;
    private readonly float _nmsThreshold = 0.45f;
    private bool _disposed;

    public FaceDetector(string modelPath, float confidenceThreshold = 0.5f, bool useGpu = false)
    {
        _confidenceThreshold = confidenceThreshold;

        var sessionOptions = new SessionOptions();
        
        // 尝试使用 CUDA
        if (useGpu)
        {
            try
            {
                sessionOptions.AppendExecutionProvider_CUDA(0);
            }
            catch
            {
                // CUDA 不可用，回退到 CPU
            }
        }
        sessionOptions.AppendExecutionProvider_CPU();

        _session = new InferenceSession(modelPath, sessionOptions);
    }

    /// <summary>
    /// 检测人脸
    /// </summary>
    public List<FaceRect> Detect(Mat image, float confidenceThreshold = 0)
    {
        var threshold = confidenceThreshold > 0 ? confidenceThreshold : _confidenceThreshold;
        
        // 预处理：缩放、归一化、转为 NCHW
        var inputTensor = Preprocess(image);
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("images", inputTensor)
        };

        // 推理
        using var results = _session.Run(inputs);
        
        // 获取输出
        var output = results.First().AsEnumerable<float>().ToArray();
        var outputDims = results.First().AsTensor<float>().Dimensions.ToArray();
        
        // 后处理
        return Postprocess(output, outputDims, image.Width, image.Height, threshold);
    }

    /// <summary>
    /// 预处理：缩放到 640x640，BGR->RGB，归一化到 0-1，转为 NCHW
    /// </summary>
    private DenseTensor<float> Preprocess(Mat image)
    {
        // 缩放
        var resized = new Mat();
        Cv2.Resize(image, resized, new Size(_inputSize, _inputSize));

        // BGR -> RGB
        var rgb = new Mat();
        Cv2.CvtColor(resized, rgb, ColorConversionCodes.BGR2RGB);

        // 归一化到 0-1
        var normalized = new Mat();
        rgb.ConvertTo(normalized, MatType.CV_32FC3, 1.0 / 255.0);

        // 转换为 NCHW 格式 (batch, channels, height, width)
        var tensor = new DenseTensor<float>(new[] { 1, 3, _inputSize, _inputSize });
        var indexer = normalized.GetGenericIndexer<Vec3f>();

        for (int y = 0; y < _inputSize; y++)
        {
            for (int x = 0; x < _inputSize; x++)
            {
                var pixel = indexer[y, x];
                tensor[0, 0, y, x] = pixel.Item0; // R
                tensor[0, 1, y, x] = pixel.Item1; // G
                tensor[0, 2, y, x] = pixel.Item2; // B
            }
        }

        return tensor;
    }

    /// <summary>
    /// 后处理：解析 YOLOv8-Face 输出
    /// YOLOv8 输出格式: [1, 84 + num_landmarks*2, num_boxes] 或 [1, num_boxes, 84 + num_landmarks*2]
    /// 其中 84 = 4(bbox) + 80(classes) 或 4(bbox) + 1(conf) + 79/...
    /// 人脸检测简化：前 4 个是中心点+宽高，第 5 个是置信度
    /// </summary>
    private List<FaceRect> Postprocess(float[] output, int[] dims, int originalWidth, int originalHeight, float threshold)
    {
        var faces = new List<FaceRect>();

        // 计算缩放比例
        float scaleX = (float)originalWidth / _inputSize;
        float scaleY = (float)originalHeight / _inputSize;

        // YOLOv8 输出格式判断
        // 格式1: [1, num_features, num_boxes] - 需要转置
        // 格式2: [1, num_boxes, num_features] - 直接使用
        
        int numFeatures, numBoxes;
        bool needTranspose = false;

        if (dims.Length == 3)
        {
            if (dims[1] > dims[2])
            {
                // [1, num_features, num_boxes] -> 需要转置
                numFeatures = dims[1];
                numBoxes = dims[2];
                needTranspose = true;
            }
            else
            {
                // [1, num_boxes, num_features]
                numBoxes = dims[1];
                numFeatures = dims[2];
            }
        }
        else
        {
            return faces;
        }

        // 遍历所有检测框
        var candidates = new List<(float x, float y, float w, float h, float conf)>();

        for (int i = 0; i < numBoxes; i++)
        {
            float cx, cy, w, h, conf;

            if (needTranspose)
            {
                // [1, num_features, num_boxes] 格式
                // cx, cy, w, h 在前 4 个特征
                cx = output[0 * numBoxes + i];
                cy = output[1 * numBoxes + i];
                w = output[2 * numBoxes + i];
                h = output[3 * numBoxes + i];
                // 第 4 个特征是置信度（索引从 0 开始）
                conf = output[4 * numBoxes + i];
            }
            else
            {
                // [1, num_boxes, num_features] 格式
                int offset = i * numFeatures;
                cx = output[offset + 0];
                cy = output[offset + 1];
                w = output[offset + 2];
                h = output[offset + 3];
                conf = output[offset + 4];
            }

            // 置信度过滤
            if (conf < threshold)
                continue;

            // 过滤无效框
            if (w <= 0 || h <= 0)
                continue;

            candidates.Add((cx, cy, w, h, conf));
        }

        // 非极大值抑制 (NMS)
        var nmsBoxes = NMS(candidates);

        // 转换为原始坐标
        foreach (var box in nmsBoxes)
        {
            int x = (int)((box.x - box.w / 2) * scaleX);
            int y = (int)((box.y - box.h / 2) * scaleY);
            int width = (int)(box.w * scaleX);
            int height = (int)(box.h * scaleY);

            // 边界检查
            x = Math.Max(0, x);
            y = Math.Max(0, y);
            width = Math.Min(width, originalWidth - x);
            height = Math.Min(height, originalHeight - y);

            if (width > 0 && height > 0)
            {
                faces.Add(new FaceRect(x, y, width, height, box.conf));
            }
        }

        return faces;
    }

    /// <summary>
    /// 非极大值抑制
    /// </summary>
    private List<(float x, float y, float w, float h, float conf)> NMS(
        List<(float x, float y, float w, float h, float conf)> boxes)
    {
        if (boxes.Count == 0)
            return boxes;

        // 按置信度降序排序
        var sorted = boxes.OrderByDescending(b => b.conf).ToList();
        var result = new List<(float x, float y, float w, float h, float conf)>();

        while (sorted.Count > 0)
        {
            var best = sorted[0];
            result.Add(best);
            sorted.RemoveAt(0);

            // 过滤与最佳框重叠度高的框
            sorted = sorted.Where(box =>
            {
                float iou = CalculateIoU(best, box);
                return iou < _nmsThreshold;
            }).ToList();
        }

        return result;
    }

    /// <summary>
    /// 计算 IoU (Intersection over Union)
    /// </summary>
    private float CalculateIoU(
        (float x, float y, float w, float h, float conf) a,
        (float x, float y, float w, float h, float conf) b)
    {
        float x1_a = a.x - a.w / 2, y1_a = a.y - a.h / 2;
        float x2_a = a.x + a.w / 2, y2_a = a.y + a.h / 2;
        float x1_b = b.x - b.w / 2, y1_b = b.y - b.h / 2;
        float x2_b = b.x + b.w / 2, y2_b = b.y + b.h / 2;

        float inter_x1 = Math.Max(x1_a, x1_b);
        float inter_y1 = Math.Max(y1_a, y1_b);
        float inter_x2 = Math.Min(x2_a, x2_b);
        float inter_y2 = Math.Min(y2_a, y2_b);

        float inter_area = Math.Max(0, inter_x2 - inter_x1) * Math.Max(0, inter_y2 - inter_y1);
        float area_a = a.w * a.h;
        float area_b = b.w * b.h;
        float union_area = area_a + area_b - inter_area;

        return union_area > 0 ? inter_area / union_area : 0;
    }

    /// <summary>
    /// 获取 GPU 状态
    /// </summary>
    public static (bool available, string info) GetGpuStatus()
    {
        try
        {
            // 尝试创建 CUDA session
            var testOptions = new SessionOptions();
            testOptions.AppendExecutionProvider_CUDA(0);
            // 如果没有抛出异常，说明 CUDA 可用
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
            _session?.Dispose();
            _disposed = true;
        }
    }
}
