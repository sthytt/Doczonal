using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tesseract;
using Windows.Storage;

namespace Doczonal.Services
{
    internal class OcrService
    {
        private readonly string dataPath = Path.Combine(AppContext.BaseDirectory, "Assets", "tessdata");

        public string ProcessImage(string imagePath)
        {
            string resultText = "";

            try
            {
                using (var engine = new TesseractEngine(dataPath, "fin", EngineMode.Default))
                {
                    using (var img = Pix.LoadFromFile(imagePath))
                    {
                        using (var page = engine.Process(img))
                        {
                            resultText = page.GetText();
                            float confidence = page.GetMeanConfidence();
                            Console.WriteLine($"Confidence: {confidence}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"error : {ex.Message}");
            }

            return resultText;
        }
    }
}
