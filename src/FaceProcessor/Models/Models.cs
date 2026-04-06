namespace FaceProcessor.Models;

/// <summary>人脸矩形</summary>
public class FaceRect
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public float Confidence { get; set; }

    public FaceRect(int x, int y, int width, int height, float confidence = 1f)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
        Confidence = confidence;
    }
}

/// <summary>处理模式</summary>
public enum ProcessMode
{
    None,
    Mosaic,
    Blur,
    BlackMesh,
    Grid,
    Split
}

/// <summary>应用配置</summary>
public class AppConfig
{
    // 图片处理
    public ProcessMode ImageMode { get; set; } = ProcessMode.Mosaic;
    public int MaxResolution { get; set; } = 0;
    public int MaxFileSizeMB { get; set; } = 0;
    public string ExportFormat { get; set; } = "PNG";
    public int ExportQuality { get; set; } = 95;
    public string OutputPath { get; set; } = "";
    public bool UseSubfolder { get; set; } = true;
    public string SubfolderName { get; set; } = "processed";

    // 视频处理
    public ProcessMode VideoMode { get; set; } = ProcessMode.Mosaic;
    public int VideoBlockSize { get; set; } = 0;
    public int VideoBlurStrength { get; set; } = 99;
    public bool VideoKeepAudio { get; set; } = true;

    // GPU
    public bool UseGpu { get; set; } = false;
}
