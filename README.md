# FaceProcessor

`FaceProcessor` is a .NET 8 / WPF desktop app for local face processing in images and videos.

Current version: `v1.0.1`

## Stack

- .NET 8
- WPF
- OpenCvSharp4
- ONNX Runtime
- Xabe.FFmpeg

## Features

- Image processing: mosaic, blur, black mesh, grid, split mode
- Video processing: frame-by-frame face detection and masking
- Optional GPU inference when the local runtime supports it
- Audio retention during video export

## Kept Project Layout

```text
.
|-- FaceProcessor.sln
|-- README.md
|-- assets/
|   `-- models/
|-- src/
|   `-- FaceProcessor/
`-- .github/
```

Temporary publish output, reverse-engineering artifacts, local tools, and build output are intentionally not kept in the repo.

## Models

Source model files live in `assets/models/`.

During build, these files are copied into the app output under `models/`:

- `face_detector.caffemodel`
- `deploy.prototxt`
- `yolov8n-face.onnx` if you place one in `assets/models/`

## Build

```powershell
dotnet restore
dotnet build .\FaceProcessor.sln -c Release
```

## Run

```powershell
dotnet run --project .\src\FaceProcessor\FaceProcessor.csproj
```

## Publish

```powershell
dotnet publish .\src\FaceProcessor\FaceProcessor.csproj -c Release -r win-x64 --self-contained true -o .\publish
```
