using System.IO;
using OpenCvSharp;
using FaceProcessor.Models;

namespace FaceProcessor.Services;

/// <summary>图片处理器</summary>
public class ImageProcessor
{
    private readonly FaceDetector? _detector;

    public ImageProcessor(FaceDetector? detector = null)
    {
        _detector = detector;
    }

    /// <summary>处理图片</summary>
    public Mat Process(Mat image, List<FaceRect> faces, ProcessMode mode, ProcessOptions? options = null)
    {
        options ??= new ProcessOptions();
        
        return mode switch
        {
            ProcessMode.Mosaic => ApplyMosaic(image, faces, options.BlockSize),
            ProcessMode.Blur => ApplyBlur(image, faces, options.BlurStrength),
            ProcessMode.BlackMesh => ApplyBlackMesh(image, faces),
            ProcessMode.Grid => ApplyGrid(image, faces),
            ProcessMode.Split => ApplySplit(image, faces, options),
            _ => image
        };
    }

    /// <summary>马赛克</summary>
    private Mat ApplyMosaic(Mat image, List<FaceRect> faces, int blockSize = 0)
    {
        var result = image.Clone();
        
        foreach (var face in faces)
        {
            var pad = (int)(Math.Min(face.Width, face.Height) * 0.1);
            var x1 = Math.Max(0, face.X - pad);
            var y1 = Math.Max(0, face.Y - pad);
            var x2 = Math.Min(image.Width, face.X + face.Width + pad);
            var y2 = Math.Min(image.Height, face.Y + face.Height + pad);

            var actualBlockSize = blockSize > 0 ? blockSize : Math.Max(10, Math.Min(face.Width, face.Height) / 10);
            var faceRegion = result[new Rect(x1, y1, x2 - x1, y2 - y1)];

            for (int y = 0; y < faceRegion.Height; y += actualBlockSize)
            {
                for (int x = 0; x < faceRegion.Width; x += actualBlockSize)
                {
                    var w = Math.Min(actualBlockSize, faceRegion.Width - x);
                    var h = Math.Min(actualBlockSize, faceRegion.Height - y);
                    var block = faceRegion[new Rect(x, y, w, h)];
                    var meanColor = Cv2.Mean(block);
                    block.SetTo(new Scalar(meanColor.Val0, meanColor.Val1, meanColor.Val2));
                }
            }
        }

        return result;
    }

    /// <summary>高斯模糊</summary>
    private Mat ApplyBlur(Mat image, List<FaceRect> faces, int blurStrength = 99)
    {
        var result = image.Clone();
        
        foreach (var face in faces)
        {
            var pad = (int)(Math.Min(face.Width, face.Height) * 0.15);
            var x1 = Math.Max(0, face.X - pad);
            var y1 = Math.Max(0, face.Y - pad);
            var x2 = Math.Min(image.Width, face.X + face.Width + pad);
            var y2 = Math.Min(image.Height, face.Y + face.Height + pad);

            var faceRegion = result[new Rect(x1, y1, x2 - x1, y2 - y1)];
            var blurred = new Mat();
            Cv2.GaussianBlur(faceRegion, blurred, new Size(blurStrength, blurStrength), 30);
            blurred.CopyTo(faceRegion);
        }

        return result;
    }

    /// <summary>黑色网格</summary>
    private Mat ApplyBlackMesh(Mat image, List<FaceRect> faces)
    {
        var result = image.Clone();

        foreach (var face in faces)
        {
            var pad = (int)(Math.Min(face.Width, face.Height) * 0.1);
            var x1 = Math.Max(0, face.X - pad);
            var y1 = Math.Max(0, face.Y - pad);
            var x2 = Math.Min(image.Width, face.X + face.Width + pad);
            var y2 = Math.Min(image.Height, face.Y + face.Height + pad);

            var width = x2 - x1;
            var height = y2 - y1;
            var spacing = Math.Max(8, Math.Min(face.Width, face.Height) / 20);
            var lineWidth = Math.Max(2, spacing / 4);

            // 创建网格
            var mesh = new Mat(height, width, MatType.CV_8UC3, new Scalar(20, 20, 20));
            
            // 绘制斜线
            for (int i = -height; i < width + height; i += spacing)
            {
                Cv2.Line(mesh, new Point(i, 0), new Point(i + height, height), new Scalar(20, 20, 20), lineWidth);
                Cv2.Line(mesh, new Point(i + height, 0), new Point(i, height), new Scalar(20, 20, 20), lineWidth);
            }

            // 简单混合
            var faceRegion = result[new Rect(x1, y1, width, height)];
            Cv2.AddWeighted(mesh, 0.7, faceRegion, 0.3, 0, faceRegion);
        }

        return result;
    }

    /// <summary>网格线</summary>
    private Mat ApplyGrid(Mat image, List<FaceRect> faces)
    {
        var result = image.Clone();

        foreach (var face in faces)
        {
            var pad = (int)(Math.Min(face.Width, face.Height) * 0.1);
            var x1 = Math.Max(0, face.X - pad);
            var y1 = Math.Max(0, face.Y - pad);
            var x2 = Math.Min(image.Width, face.X + face.Width + pad);
            var y2 = Math.Min(image.Height, face.Y + face.Height + pad);

            var spacing = Math.Max(15, Math.Min(face.Width, face.Height) / 15);
            var color = new Scalar(50, 50, 50);

            for (int x = x1; x < x2; x += spacing)
            {
                Cv2.Line(result, new Point(x, y1), new Point(x, y2), color, 1);
            }
            for (int y = y1; y < y2; y += spacing)
            {
                Cv2.Line(result, new Point(x1, y), new Point(x2, y), color, 1);
            }
        }

        return result;
    }

    /// <summary>拆分图（人脸 + 无脸）</summary>
    private Mat ApplySplit(Mat image, List<FaceRect> faces, ProcessOptions options)
    {
        return image;
    }

    /// <summary>调整分辨率</summary>
    public Mat Resize(Mat image, int maxResolution)
    {
        if (maxResolution <= 0) return image;

        var maxDim = Math.Max(image.Width, image.Height);
        if (maxDim <= maxResolution) return image;

        var scale = (double)maxResolution / maxDim;
        var newWidth = (int)(image.Width * scale);
        var newHeight = (int)(image.Height * scale);
        
        var resized = new Mat();
        Cv2.Resize(image, resized, new Size(newWidth, newHeight), 0, 0, InterpolationFlags.Area);
        return resized;
    }

    /// <summary>保存图片</summary>
    public bool Save(Mat image, string outputPath, string format = "PNG", int quality = 95, int maxSizeMB = 0)
    {
        try
        {
            // 验证图片有效性
            if (image == null || image.Empty())
            {
                System.Diagnostics.Debug.WriteLine($"[ImageProcessor] 图片无效，无法保存");
                return false;
            }

            // 确保输出目录存在
            var dir = Path.GetDirectoryName(outputPath);
            if (string.IsNullOrEmpty(dir))
            {
                System.Diagnostics.Debug.WriteLine($"[ImageProcessor] 输出目录无效: {outputPath}");
                return false;
            }
            if (!Directory.Exists(dir))
            {
                System.Diagnostics.Debug.WriteLine($"[ImageProcessor] 创建输出目录: {dir}");
                Directory.CreateDirectory(dir);
            }

            // 确定文件扩展名
            var ext = format.ToLower() switch
            {
                "jpg" or "jpeg" => ".jpg",
                "webp" => ".webp",
                _ => ".png"
            };
            var finalPath = Path.ChangeExtension(outputPath, ext);

            System.Diagnostics.Debug.WriteLine($"[ImageProcessor] 保存图片: {finalPath}, 格式: {format}, 质量: {quality}");
            System.Diagnostics.Debug.WriteLine($"[ImageProcessor] 图片尺寸: {image.Width}x{image.Height}, 通道: {image.Channels()}");

            // 保存图片
            bool success;
            if (format.ToLower() == "png")
            {
                success = Cv2.ImWrite(finalPath, image);
            }
            else
            {
                // JPG/WEBP 使用质量参数
                var param = new ImageEncodingParam(
                    format.ToLower() == "webp" ? ImwriteFlags.WebPQuality : ImwriteFlags.JpegQuality,
                    quality);
                success = Cv2.ImWrite(finalPath, image, new[] { param });
            }

            if (!success)
            {
                System.Diagnostics.Debug.WriteLine($"[ImageProcessor] Cv2.ImWrite 返回 false，格式: {format}");
                return false;
            }

            // 验证文件
            if (!File.Exists(finalPath))
            {
                System.Diagnostics.Debug.WriteLine($"[ImageProcessor] 文件保存后不存在: {finalPath}");
                return false;
            }

            var fileSize = new FileInfo(finalPath).Length;
            System.Diagnostics.Debug.WriteLine($"[ImageProcessor] 保存成功: {finalPath}, 大小: {fileSize / 1024.0:F1} KB");

            // 文件大小限制
            if (maxSizeMB > 0 && fileSize > maxSizeMB * 1024 * 1024 && quality > 10)
            {
                System.Diagnostics.Debug.WriteLine($"[ImageProcessor] 文件过大({fileSize / 1024.0:F1}KB)，尝试降低质量...");
                return Save(image, finalPath, format, quality - 10, maxSizeMB);
            }

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageProcessor] Save 异常: {ex.Message}\n{ex.StackTrace}");
            return false;
        }
    }
}

/// <summary>处理选项</summary>
public class ProcessOptions
{
    public int BlockSize { get; set; } = 0;
    public int BlurStrength { get; set; } = 99;
    public int MaxResolution { get; set; } = 0;
    public int MaxFileSizeMB { get; set; } = 0;
    public string OutputPath { get; set; } = "";
    public string Format { get; set; } = "PNG";
    public int Quality { get; set; } = 95;
    public bool KeepAudio { get; set; } = true;
}
