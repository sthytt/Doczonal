using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tesseract;
using Windows.Storage;
using Doczonal.Models;

namespace Doczonal.Services
{
    internal static class BatchOcrService
    {
        public static async Task<List<Dictionary<string, string>>> ProcessFilesAsync(IEnumerable<StorageFile> files, IEnumerable<OcrZone> zones, double containerW, double containerH, string tessdataPath, string language = "fin")
        {
            var results = new List<Dictionary<string, string>>();

            using (var engine = new TesseractEngine(tessdataPath, language, EngineMode.Default))
            {
                foreach (var file in files)
                {
                    var row = new Dictionary<string, string> { ["File_name"] = file.Name };

                    string pixPath = file.Path;
                    var ext = System.IO.Path.GetExtension(file.Path).ToLowerInvariant();
                    if (ext == ".pdf")
                    {
                        pixPath = await ImageService.RenderPdfFirstPageToPngAsync(file.Path);
                    }

                    using (var pix = Pix.LoadFromFile(pixPath))
                    {
                        foreach (var zone in zones)
                        {
                            var region = ImageService.ScaleZoneToImage(zone.X, zone.Y, zone.Width, zone.Height, pix.Width, pix.Height, containerW, containerH);
                            using (var page = engine.Process(pix, region))
                            {
                                var text = page.GetText()?.Trim() ?? string.Empty;
                                var conf = page.GetMeanConfidence();
                                row[zone.Label] = text;
                                row[zone.Label + "_conf"] = conf.ToString("F2");
                            }
                        }
                    }

                    results.Add(row);
                }
            }

            return results;
        }
    }
}
