using System.IO;
using OpenCvSharp;
using FaceProcessor.Models;

namespace FaceProcessor.Services;

/// <summary>图片处理器</summary>
public class ImageProcessor
{
    private readonly FaceDetector? _detector;
    private readonly Random _random = new();

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

            // 创建椭圆蒙版平滑过渡
            var mask = new Mat(faceRegion.Height, faceRegion.Width, MatType.CV_32F, Scalar.Black);
            var center = new Point(faceRegion.Width / 2, faceRegion.Height / 2);
            var axes = new Size(faceRegion.Width / 2, faceRegion.Height / 2);
            Cv2.Ellipse(mask, center, axes, 0, 0, 360, Scalar.White, -1);
            Cv2.GaussianBlur(mask, mask, new Size(51, 51), 0);

            // 混合：用蒙版混合模糊图和原图
            var maskNormalized = new Mat();
            mask.ConvertTo(maskNormalized, MatType.CV_32F, 1.0 / 255.0);
            
            // 复制模糊区域到结果
            var faceRegionFloat = new Mat();
            var blurredFloat = new Mat();
            faceRegion.ConvertTo(faceRegionFloat, MatType.CV_32FC3);
            blurred.ConvertTo(blurredFloat, MatType.CV_32FC3);
            
            // 用蒙版混合
            var mask3Channel = new Mat();
            Cv2.Merge(new[] { maskNormalized, maskNormalized, maskNormalized }, mask3Channel);
            
            var resultFloat = new Mat();
            Cv2.Multiply(blurredFloat, mask3Channel, blurredFloat);
            Cv2.Multiply(faceRegionFloat, new Scalar(1.0, 1.0, 1.0) - mask3Channel, faceRegionFloat);
            Cv2.Add(blurredFloat, faceRegionFloat, resultFloat);
            
            resultFloat.ConvertTo(faceRegion, MatType.CV_8UC3);
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

            // 添加噪点
            var noise = new Mat();
            Cv2.Randn(noise, new Scalar(0, 0, 0), new Scalar(30, 30, 30));
            Cv2.Add(mesh, noise, mesh);

            // 创建蒙版
            var mask = new Mat(height, width, MatType.CV_32F, Scalar.Black);
            Cv2.Ellipse(mask, new Point(width / 2, height / 2), new Size(width / 2, height / 2), 0, 0, 360, Scalar.White, -1);
            Cv2.GaussianBlur(mask, mask, new Size(51, 51), 0);

            var faceRegion = result[new Rect(x1, y1, width, height)];
            var mask3 = new Mat();
            Cv2.Merge(new[] { mask, mask, mask }, mask3);

            // 混合：用蒙版混合网格和原图
            var faceRegionFloat = new Mat();
            var meshFloat = new Mat();
            faceRegion.ConvertTo(faceRegionFloat, MatType.CV_32FC3, 1.0 / 255.0);
            mesh.ConvertTo(meshFloat, MatType.CV_32FC3, 1.0 / 255.0);
            
            var mask3Float = mask3.ToMat(MatType.CV_32F, 1.0 / 255.0);
            var mask3Channel = new Mat();
            Cv2.Merge(new[] { mask3Float, mask3Float, mask3Float }, mask3Channel);
            
            var resultFloat = new Mat();
            Cv2.Multiply(meshFloat, mask3Channel, meshFloat, 0.7);
            Cv2.Multiply(faceRegionFloat, new Scalar(1.0, 1.0, 1.0) - mask3Channel, faceRegionFloat, 1.0);
            Cv2.Add(meshFloat, faceRegionFloat, resultFloat);
            
            resultFloat.ConvertTo(faceRegion, MatType.CV_8UC3, 255.0);
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
        // 返回原图，拆分结果通过回调返回
        return image;
    }

    /// <summary>调整分辨率</summary>
    public Mat Resize(Mat image, int maxResolution)
    {
        if (maxResolution <= 0)
            return image;

        var maxDim = Math.Max(image.Width, image.Height);
        if (maxDim <= maxResolution)
            return image;

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
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? "");

            var ext = format.ToLower() switch
            {
                "jpg" or "jpeg" => ".jpg",
                "webp" => ".webp",
                _ => ".png"
            };

            outputPath = Path.ChangeExtension(outputPath, ext);
            var parameters = format.ToLower() switch
            {
                "jpg" or "jpeg" => new[] { new ImageEncodingParam(ImwriteFlags.JpegQuality, quality) },
                "webp" => new[] { new ImageEncodingParam(ImwriteFlags.WebPCompression, quality / 10) },
                _ => new[] { new ImageEncodingParam(ImwriteFlags.PngCompression, 6) }
            };

            Cv2.ImWrite(outputPath, image, parameters);

            // 文件大小限制
            if (maxSizeMB > 0)
            {
                var fileInfo = new FileInfo(outputPath);
                if (fileInfo.Length > maxSizeMB * 1024 * 1024 && quality > 10)
                {
                    // 递减质量重新保存
                    return Save(image, outputPath, format, quality - 10, maxSizeMB);
                }
            }

            return true;
        }
        catch
        {
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
