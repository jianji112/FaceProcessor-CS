# Face Processor (C# / WPF)

人脸处理工具，支持图片和视频人脸打码。

## 技术栈

- **UI 框架**: WPF (.NET 8)
- **图像处理**: OpenCvSharp4
- **人脸检测**: ONNX Runtime + YOLOv8-Face
- **MVVM**: CommunityToolkit.Mvvm

## 功能

### 图片处理
- 马赛克
- 高斯模糊
- 黑丝网格
- 网格线
- 拆分图

### 视频处理
- 逐帧人脸检测
- 马赛克/模糊
- 保留音频

## 本地开发

```bash
# 安装 .NET 8 SDK
# 下载: https://dotnet.microsoft.com/download/dotnet/8.0

# 还原依赖
dotnet restore

# 运行
dotnet run --project src/FaceProcessor

# 发布单文件 EXE
dotnet publish src/FaceProcessor/FaceProcessor.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -o ./publish
```

## ONNX 模型

需要下载 YOLOv8-Face 模型到 `assets/models/yolov8n-face.onnx`:

```bash
# 从 Hugging Face 下载
curl -L -o assets/models/yolov8n-face.onnx \
  "https://huggingface.co/Nick3151/yolov8n-face/resolve/main/yolov8n-face.onnx"
```

## 对比 Python 版

| | Python 版 | C# 版 |
|---|---|---|
| 打包体积 | ~500MB | ~100MB |
| 启动速度 | 慢 | 快 |
| 依赖管理 | pip 易冲突 | NuGet 稳定 |
| 打包可靠性 | PyInstaller 问题多 | 单文件 EXE |

## 项目结构

```
FaceProcessor/
├── FaceProcessor.sln
├── src/FaceProcessor/
│   ├── App.xaml(.cs)
│   ├── MainWindow.xaml(.cs)
│   ├── Models/
│   │   └── Models.cs
│   └── Services/
│       ├── ConfigManager.cs
│       ├── FaceDetector.cs
│       ├── ImageProcessor.cs
│       └── VideoProcessor.cs
├── assets/models/
│   └── yolov8n-face.onnx
└── .github/workflows/build.yml
```
