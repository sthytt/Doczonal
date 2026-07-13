using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using Tesseract;
using Doczonal.Models;

namespace Doczonal.Services
{
    internal static class ImageService
    {
        public static async Task<string> RenderPdfFirstPageToPngAsync(string pdfPath)
        {
            var pdfFile = await StorageFile.GetFileFromPathAsync(pdfPath);
            var pdfDoc = await PdfDocument.LoadFromFileAsync(pdfFile);
            if (pdfDoc.PageCount == 0) throw new Exception("PDF has no pages");

            using (var page = pdfDoc.GetPage(0))
            {
                var renderStream = new InMemoryRandomAccessStream();
                await page.RenderToStreamAsync(renderStream);
                renderStream.Seek(0);

                var decoder = await BitmapDecoder.CreateAsync(renderStream);
                var pixelData = await decoder.GetPixelDataAsync(decoder.BitmapPixelFormat, decoder.BitmapAlphaMode, new BitmapTransform(), ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb);
                var pixels = pixelData.DetachPixelData();

                var outFile = await ApplicationData.Current.TemporaryFolder.CreateFileAsync(Guid.NewGuid().ToString() + ".png", CreationCollisionOption.ReplaceExisting);
                using (var outStream = await outFile.OpenAsync(FileAccessMode.ReadWrite))
                {
                    var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, outStream);
                    encoder.SetPixelData(decoder.BitmapPixelFormat, decoder.BitmapAlphaMode, decoder.PixelWidth, decoder.PixelHeight, decoder.DpiX, decoder.DpiY, pixels);
                    await encoder.FlushAsync();
                }

                return outFile.Path;
            }
        }

        public static async Task<string> CropImageAsync(string sourcePath, OcrZone zone, double containerW, double containerH)
        {
            // If the source is a PDF, render the first PDF page to PNG first.
            string actualPath = sourcePath;
            var ext = System.IO.Path.GetExtension(sourcePath).ToLowerInvariant();
            if (ext == ".pdf")
            {
                actualPath = await RenderPdfFirstPageToPngAsync(sourcePath);
            }

            var file = await StorageFile.GetFileFromPathAsync(actualPath);
            using (var stream = await file.OpenAsync(FileAccessMode.Read))
            {
                var decoder = await BitmapDecoder.CreateAsync(stream);

                int imgPixelW = (int)decoder.PixelWidth;
                int imgPixelH = (int)decoder.PixelHeight;

                double scale = Math.Min(containerW / imgPixelW, containerH / imgPixelH);
                if (double.IsInfinity(scale) || scale <= 0) scale = 1.0;

                double displayImgW = imgPixelW * scale;
                double displayImgH = imgPixelH * scale;
                double offsetX = (containerW - displayImgW) / 2.0;
                double offsetY = (containerH - displayImgH) / 2.0;

                double imgXf = (zone.X - offsetX) / scale;
                double imgYf = (zone.Y - offsetY) / scale;
                double imgWf = zone.Width / scale;
                double imgHf = zone.Height / scale;

                uint x = (uint)Math.Max(0, Math.Round(imgXf));
                uint y = (uint)Math.Max(0, Math.Round(imgYf));
                uint w = (uint)Math.Max(1, Math.Round(imgWf));
                uint h = (uint)Math.Max(1, Math.Round(imgHf));

                if (x + w > (uint)imgPixelW) w = (uint)imgPixelW - x;
                if (y + h > (uint)imgPixelH) h = (uint)imgPixelH - y;

                var transform = new BitmapTransform
                {
                    Bounds = new BitmapBounds { X = x, Y = y, Width = w, Height = h }
                };

                var pixelData = await decoder.GetPixelDataAsync(decoder.BitmapPixelFormat, decoder.BitmapAlphaMode, transform, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb);
                var pixels = pixelData.DetachPixelData();

                var outFile = await ApplicationData.Current.TemporaryFolder.CreateFileAsync(Guid.NewGuid().ToString() + ".png", CreationCollisionOption.ReplaceExisting);
                using (var outStream = await outFile.OpenAsync(FileAccessMode.ReadWrite))
                {
                    var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, outStream);
                    encoder.SetPixelData(decoder.BitmapPixelFormat, decoder.BitmapAlphaMode, w, h, decoder.DpiX, decoder.DpiY, pixels);
                    await encoder.FlushAsync();
                }

                return outFile.Path;
            }
        }

        public static Tesseract.Rect ScaleZoneToImage(double zoneX, double zoneY, double zoneWidth, double zoneHeight, int imageWidth, int imageHeight, double containerW, double containerH)
        {
            if (containerW <= 0 || containerH <= 0 || imageWidth <= 0 || imageHeight <= 0)
            {
                return new Tesseract.Rect(0, 0, (int)zoneWidth, (int)zoneHeight);
            }

            double scale = Math.Min(containerW / imageWidth, containerH / imageHeight);
            double displayImageW = imageWidth * scale;
            double displayImageH = imageHeight * scale;
            double offsetX = (containerW - displayImageW) / 2.0;
            double offsetY = (containerH - displayImageH) / 2.0;

            double imgX = (zoneX - offsetX) / scale;
            double imgY = (zoneY - offsetY) / scale;
            double imgW = zoneWidth / scale;
            double imgH = zoneHeight / scale;

            if (imgX < 0) imgX = 0;
            if (imgY < 0) imgY = 0;
            if (imgX + imgW > imageWidth) imgW = imageWidth - imgX;
            if (imgY + imgH > imageHeight) imgH = imageHeight - imgY;

            return new Tesseract.Rect((int)Math.Round(imgX), (int)Math.Round(imgY), (int)Math.Round(imgW), (int)Math.Round(imgH));
        }
    }
}
