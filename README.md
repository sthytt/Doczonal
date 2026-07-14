# Doczonal

> Doczonal is a WinUI desktop tool for drawing OCR zones on images and PDFs and performing batch OCR using Tesseract.

## Prerequisites
- Windows 10/11
- .NET 8 SDK
- Visual Studio 2022/2026 or the `dotnet` CLI
- Tesseract traineddata files placed in `Doczonal/Assets/tessdata` (e.g. `eng.traineddata`, `fin.traineddata`)

## Build & run
- Open the solution in Visual Studio and run.
- Or from command line:

```
dotnet build Doczonal\\Doczonal.csproj
dotnet run --project Doczonal\\Doczonal.csproj
```

## Usage
1. Click `Load` to open an image or PDF (first page will be rendered for PDFs).
2. Draw zones on the preview by dragging on the canvas.
3. Click `Done` to run batch OCR for a selected folder; results are exported to CSV in the selected folder.
