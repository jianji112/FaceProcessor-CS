#define TRACE
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FaceProcessor.Models;
using OpenCvSharp;

namespace FaceProcessor.Services;

public class ImageProcessor
{
	public ImageProcessor(FaceDetector? detector = null)
	{
	}

	public Mat Process(Mat image, List<FaceRect> faces, ProcessMode mode, ProcessOptions? options = null)
	{
		if (options == null)
		{
			options = new ProcessOptions();
		}
		faces = SelectFacesToProcess(faces, options.MaxFacesToProcess);
		if (faces.Count != 0)
		{
			switch (mode)
			{
			case ProcessMode.None:
				break;
			case ProcessMode.Mosaic:
				return ApplyMosaic(image, faces, options.BlockSize);
			case ProcessMode.Blur:
				return ApplyBlur(image, faces, options.BlurStrength);
			case ProcessMode.BlackMesh:
				return ApplyBlackMesh(image, faces);
			case ProcessMode.Grid:
				return ApplyGrid(image, faces);
			case ProcessMode.Split:
				return CreateBackgroundOnlyImage(image, faces);
			default:
				return image.Clone();
			}
		}
		return image.Clone();
	}

	public (Mat BackgroundOnly, Mat FaceOnly) CreateFaceSeparationOutputs(Mat image, List<FaceRect> faces, ProcessOptions? options = null)
	{
		List<FaceRect> list = SelectFacesToProcess(faces, options?.MaxFacesToProcess ?? 1);
		Mat backgroundOnly = image.Clone();
		Mat faceOnly = new Mat(image.Rows, image.Cols, image.Type(), Scalar.Black);
		foreach (FaceRect face in list)
		{
			Rect region = GetExpandedRect(image, face, 0.04);
			using Mat mask = CreateFaceMask(region, face);
			using (Mat sourceRegion = new Mat(image, region))
			{
				using Mat faceRegion = new Mat(faceOnly, region);
				sourceRegion.CopyTo(faceRegion, mask);
			}
			using Mat backgroundRegion = new Mat(backgroundOnly, region);
			backgroundRegion.SetTo(Scalar.Black, mask);
		}
		return (BackgroundOnly: backgroundOnly, FaceOnly: faceOnly);
	}

	public static List<FaceRect> SelectFacesToProcess(IEnumerable<FaceRect> faces, int maxFacesToProcess)
	{
		List<FaceRect> orderedFaces = (from face in faces
			orderby face.Confidence descending, face.Width * face.Height descending
			select face).ToList();
		if (maxFacesToProcess <= 0 || orderedFaces.Count <= maxFacesToProcess)
		{
			return orderedFaces;
		}
		return orderedFaces.Take(maxFacesToProcess).ToList();
	}

	private Mat ApplyMosaic(Mat image, List<FaceRect> faces, int blockSize = 0)
	{
		Mat result = image.Clone();
		foreach (FaceRect face in faces)
		{
			Rect region = GetExpandedRect(image, face, 0.04);
			using Mat mask = CreateFaceMask(region, face);
			int actualBlockSize = ((blockSize > 0) ? blockSize : Math.Max(10, Math.Min(face.Width, face.Height) / 10));
			ApplyMaskedEffect(result, region, mask, (Mat source) => CreateMosaicRegion(source, actualBlockSize));
		}
		return result;
	}

	private Mat ApplyBlur(Mat image, List<FaceRect> faces, int blurStrength = 99)
	{
		Mat result = image.Clone();
		int kernelSize = NormalizeKernelSize(blurStrength);
		foreach (FaceRect face in faces)
		{
			Rect region = GetExpandedRect(image, face, 0.06);
			using Mat mask = CreateFaceMask(region, face);
			ApplyMaskedEffect(result, region, mask, delegate(Mat source)
			{
				Mat mat = new Mat();
				Cv2.GaussianBlur(source, mat, new Size(kernelSize, kernelSize), 30.0);
				return mat;
			});
		}
		return result;
	}

	private Mat ApplyBlackMesh(Mat image, List<FaceRect> faces)
	{
		Mat result = image.Clone();
		foreach (FaceRect face in faces)
		{
			Rect region = GetExpandedRect(image, face, 0.04);
			using Mat mask = CreateFaceMask(region, face);
			int spacing = Math.Max(8, Math.Min(face.Width, face.Height) / 20);
			int lineWidth = Math.Max(2, spacing / 4);
			ApplyMaskedEffect(result, region, mask, (Mat source) => CreateBlackMeshRegion(source, spacing, lineWidth));
		}
		return result;
	}

	private Mat ApplyGrid(Mat image, List<FaceRect> faces)
	{
		Mat result = image.Clone();
		foreach (FaceRect face in faces)
		{
			Rect region = GetExpandedRect(image, face, 0.04);
			using Mat mask = CreateFaceMask(region, face);
			int spacing = Math.Max(15, Math.Min(face.Width, face.Height) / 15);
			Scalar color = new Scalar(50.0, 50.0, 50.0);
			ApplyMaskedEffect(result, region, mask, (Mat source) => CreateGridRegion(source, spacing, color));
		}
		return result;
	}

	private Mat CreateBackgroundOnlyImage(Mat image, List<FaceRect> faces)
	{
		(Mat BackgroundOnly, Mat FaceOnly) tuple = CreateFaceSeparationOutputs(image, faces);
		var (backgroundOnly, _) = tuple;
		tuple.FaceOnly.Dispose();
		return backgroundOnly;
	}

	public Mat Resize(Mat image, int maxResolution)
	{
		if (maxResolution <= 0)
		{
			return image.Clone();
		}
		int maxDimension = Math.Max(image.Width, image.Height);
		if (maxDimension <= maxResolution)
		{
			return image.Clone();
		}
		double scale = (double)maxResolution / (double)maxDimension;
		int newWidth = (int)((double)image.Width * scale);
		int newHeight = (int)((double)image.Height * scale);
		Mat resized = new Mat();
		Cv2.Resize(image, resized, new Size(newWidth, newHeight), 0.0, 0.0, InterpolationFlags.Area);
		return resized;
	}

	public bool Save(Mat image, string outputPath, string format = "PNG", int quality = 95, int maxSizeMB = 0)
	{
		try
		{
			if (image == null || image.Empty())
			{
				Trace.WriteLine("[ImageProcessor] Invalid image, save skipped");
				return false;
			}
			string directory = Path.GetDirectoryName(outputPath);
			if (string.IsNullOrWhiteSpace(directory))
			{
				Trace.WriteLine("[ImageProcessor] Invalid output path: " + outputPath);
				return false;
			}
			Directory.CreateDirectory(directory);
			string normalizedFormat = (format ?? "PNG").Trim().ToLowerInvariant();
			string text;
			switch (normalizedFormat)
			{
			case "jpg":
			case "jpeg":
				text = ".jpg";
				break;
			case "webp":
				text = ".webp";
				break;
			default:
				text = ".png";
				break;
			}
			string extension = text;
			string finalPath = Path.ChangeExtension(outputPath, extension);
			if (!((normalizedFormat == "png") ? Cv2.ImWrite(finalPath, image) : Cv2.ImWrite(finalPath, image, new ImageEncodingParam((!(normalizedFormat == "webp")) ? ImwriteFlags.JpegQuality : ImwriteFlags.WebPQuality, quality))) || !File.Exists(finalPath))
			{
				Trace.WriteLine("[ImageProcessor] Save failed: " + finalPath);
				return false;
			}
			long fileSize = new FileInfo(finalPath).Length;
			if (maxSizeMB > 0 && fileSize > (long)maxSizeMB * 1024L * 1024 && quality > 10)
			{
				return Save(image, finalPath, normalizedFormat, quality - 10, maxSizeMB);
			}
			return true;
		}
		catch (Exception value)
		{
			Trace.WriteLine($"[ImageProcessor] Save exception: {value}");
			return false;
		}
	}

	private static Rect GetExpandedRect(Mat image, FaceRect face, double paddingRatio)
	{
		int padding = (int)((double)Math.Min(face.Width, face.Height) * paddingRatio);
		int x1 = Math.Max(0, face.X - padding);
		int y1 = Math.Max(0, face.Y - padding);
		int x2 = Math.Min(image.Width, face.X + face.Width + padding);
		int y2 = Math.Min(image.Height, face.Y + face.Height + padding);
		return new Rect(x1, y1, Math.Max(1, x2 - x1), Math.Max(1, y2 - y1));
	}

	private static int NormalizeKernelSize(int blurStrength)
	{
		int kernelSize = Math.Max(1, blurStrength);
		if (kernelSize % 2 != 0)
		{
			return kernelSize;
		}
		return kernelSize + 1;
	}

	private static void ApplyMaskedEffect(Mat image, Rect region, Mat mask, Func<Mat, Mat> effectFactory)
	{
		using Mat targetRegion = new Mat(image, region);
		using Mat source = targetRegion.Clone();
		using Mat processed = effectFactory(source);
		processed.CopyTo(targetRegion, mask);
	}

	private static Mat CreateMosaicRegion(Mat source, int blockSize)
	{
		Mat result = source.Clone();
		for (int y = 0; y < result.Height; y += blockSize)
		{
			for (int x = 0; x < result.Width; x += blockSize)
			{
				int width = Math.Min(blockSize, result.Width - x);
				int height = Math.Min(blockSize, result.Height - y);
				using Mat block = new Mat(result, new Rect(x, y, width, height));
				Scalar meanColor = Cv2.Mean(block);
				block.SetTo(new Scalar(meanColor.Val0, meanColor.Val1, meanColor.Val2));
			}
		}
		return result;
	}

	private static Mat CreateBlackMeshRegion(Mat source, int spacing, int lineWidth)
	{
		Mat result = source.Clone();
		using Mat mesh = new Mat(source.Height, source.Width, MatType.CV_8UC3, new Scalar(20.0, 20.0, 20.0));
		for (int i = -source.Height; i < source.Width + source.Height; i += spacing)
		{
			Cv2.Line(mesh, new Point(i, 0), new Point(i + source.Height, source.Height), new Scalar(20.0, 20.0, 20.0), lineWidth);
			Cv2.Line(mesh, new Point(i + source.Height, 0), new Point(i, source.Height), new Scalar(20.0, 20.0, 20.0), lineWidth);
		}
		Cv2.AddWeighted(mesh, 0.7, result, 0.3, 0.0, result);
		return result;
	}

	private static Mat CreateGridRegion(Mat source, int spacing, Scalar color)
	{
		Mat result = source.Clone();
		for (int x = 0; x < result.Width; x += spacing)
		{
			Cv2.Line(result, new Point(x, 0), new Point(x, result.Height), color);
		}
		for (int y = 0; y < result.Height; y += spacing)
		{
			Cv2.Line(result, new Point(0, y), new Point(result.Width, y), color);
		}
		return result;
	}

	private static Mat CreateFaceMask(Rect region, FaceRect face)
	{
		Mat mat = new Mat(region.Height, region.Width, MatType.CV_8UC1, Scalar.Black);
		int centerX = Math.Clamp(face.X - region.X + face.Width / 2, 0, region.Width - 1);
		int centerY = Math.Clamp(face.Y - region.Y + face.Height / 2, 0, region.Height - 1);
		int maxRadiusX = Math.Max(1, Math.Min(centerX, region.Width - centerX - 1));
		int maxRadiusY = Math.Max(1, Math.Min(centerY, region.Height - centerY - 1));
		int radiusX = Math.Min(Math.Max(1, (int)Math.Round((double)face.Width * 0.42)), maxRadiusX);
		Cv2.Ellipse(axes: new Size(radiusX, Math.Min(Math.Max(1, (int)Math.Round((double)face.Height * 0.48)), maxRadiusY)), img: mat, center: new Point(centerX, centerY), angle: 0.0, startAngle: 0.0, endAngle: 360.0, color: Scalar.White, thickness: -1, lineType: LineTypes.AntiAlias);
		return mat;
	}
}
