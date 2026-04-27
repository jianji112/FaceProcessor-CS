namespace FaceProcessor.Models;

public class AppConfig
{
	public ProcessMode ImageMode { get; set; } = ProcessMode.Mosaic;

	public int MaxResolution { get; set; }

	public int MaxFileSizeMB { get; set; }

	public string ExportFormat { get; set; } = "PNG";

	public int ExportQuality { get; set; } = 95;

	public string OutputPath { get; set; } = "";

	public bool UseSubfolder { get; set; } = true;

	public string SubfolderName { get; set; } = "processed";

	public int ImageFaceCountLimit { get; set; } = 1;

	public ProcessMode VideoMode { get; set; } = ProcessMode.Mosaic;

	public int VideoBlockSize { get; set; }

	public int VideoBlurStrength { get; set; } = 99;

	public double VideoFaceThreshold { get; set; } = 0.35;

	public bool VideoKeepAudio { get; set; } = true;

	public VideoOutputResolution VideoOutputResolution { get; set; }

	public int VideoFaceCountLimit { get; set; } = 1;

	public bool UseGpu { get; set; } = true;

	public double WindowWidth { get; set; } = 1320.0;

	public double WindowHeight { get; set; } = 1020.0;

	public double? WindowLeft { get; set; }

	public double? WindowTop { get; set; }

	public bool WindowIsMaximized { get; set; }
}
