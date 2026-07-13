using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Doczonal.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Tesseract;
using Windows.Foundation;
using Windows.Data.Pdf;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.Graphics.Imaging;
using Windows.Storage.Pickers;
using Windows.UI.WebUI;
using Tesseract;

namespace Doczonal
{
    public sealed partial class MainWindow : Window
    {
        private bool _isLoaded = false;
        private bool _isDrawingMode = true;
        private bool _isDrawing = false;
        private Point _startPoint;
        private Grid _currentGrid;
        private bool _isResizing = false;
        private Grid _resizingGrid = null;
        private const double MinZoneWidth = 20;
        private const double MinZoneHeight = 20;

        public ViewModels.MainViewModel ViewModel { get; } = new ViewModels.MainViewModel();


        /// <summary>
        /// Initializes a new instance of the <see cref="MainWindow"/> class.
        /// </summary>
        public MainWindow()
        {
            this.InitializeComponent();
        }

        /// <summary>
        /// Handles the event when the Load button is clicked. Opens a file picker to select an image.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data.</param>
        private async void LoadImage_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            picker.ViewMode = PickerViewMode.Thumbnail;
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".pdf");

            StorageFile file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                var ext = System.IO.Path.GetExtension(file.Path).ToLowerInvariant();
                string imagePathToLoad = null;

                if (ext == ".pdf")
                {
                    // Render first PDF page to PNG and load that image
                    try
                    {
                        imagePathToLoad = await RenderPdfFirstPageToPngAsync(file.Path);
                    }
                    catch (Exception ex)
                    {
                        var dlg = new ContentDialog
                        {
                            Title = "Error",
                            Content = "Failed to render PDF: " + ex.Message,
                            CloseButtonText = "OK",
                            XamlRoot = this.Content.XamlRoot
                        };
                        _ = await dlg.ShowAsync();
                    }
                }
                else
                {
                    imagePathToLoad = file.Path;
                }

                if (!string.IsNullOrEmpty(imagePathToLoad))
                {
                    var loadFile = await StorageFile.GetFileFromPathAsync(imagePathToLoad);
                    using var stream = await loadFile.OpenAsync(FileAccessMode.Read);
                    var bitmapImage = new BitmapImage();
                    await bitmapImage.SetSourceAsync(stream);
                    BackgroundImage.Source = bitmapImage;

                    // Fixed canvas size so zone coordinates stay consistent regardless of
                    // the loaded image's native resolution/aspect ratio.
                    ImageContainer.Width = 1000;
                    ImageContainer.Height = 1000;

                    // remember image path for OCR processing of folders
                    ViewModel.ImagePath = file.Path;
                }
            }

                // Enable drawing once an image is successfully loaded
                DrawingCanvas.IsHitTestVisible = true;
                DrawButton.IsEnabled = true;
                SelectButton.IsEnabled = true;
                ClearButton.IsEnabled = true;

                _isLoaded = true;
        }

        /// <summary>
        /// Handles the event when the Done button is clicked. Gathers all zones and displays them in a dialog.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data.</param>
        private async void Done_Click(object sender, RoutedEventArgs e)
        {
            var zones = DrawingCanvas.Children.OfType<Grid>().Select(g => new
            {
                Label = (g.Children.OfType<Border>().FirstOrDefault()?.Child as TextBlock)?.Text ?? "Zone",
                X = Canvas.GetLeft(g),
                Y = Canvas.GetTop(g),
                Width = g.Width,
                Height = g.Height
            }).ToList();

            if (zones.Count == 0)
            {
                var noZonesDialog = new ContentDialog
                {
                    Title = "No zones",
                    Content = "Draw at least one zone before processing.",
                    CloseButtonText = "OK",
                    XamlRoot = this.Content.XamlRoot
                };
                _ = noZonesDialog.ShowAsync();
                return;
            }

            var folderPicker = new FolderPicker();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);
            folderPicker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
            folderPicker.FileTypeFilter.Add("*");

            StorageFolder folder = await folderPicker.PickSingleFolderAsync();
            if (folder == null) return;

            var imageFiles = (await folder.GetFilesAsync())
                .Where(f => f.FileType is ".jpg" or ".jpeg" or ".png" or ".pdf")
                .ToList();

            var allResults = new List<Dictionary<string, string>>();
            string dataPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "tessdata");

            // One engine reused across the whole batch, instead of one per zone/image.
            using (var engine = new TesseractEngine(dataPath, "fin", EngineMode.Default))
            {
                foreach (var imageFile in imageFiles)
                {
                    var row = new Dictionary<string, string> { ["File_name"] = imageFile.Name };

                    string pixPath = imageFile.Path;
                    var ext = System.IO.Path.GetExtension(imageFile.Path).ToLowerInvariant();
                    if (ext == ".pdf")
                    {
                        pixPath = await RenderPdfFirstPageToPngAsync(imageFile.Path);
                    }

                    using (var pix = Pix.LoadFromFile(pixPath))
                    {
                        foreach (var zone in zones)
                        {
                            Tesseract.Rect region = ScaleZoneToImage(zone.X, zone.Y, zone.Width, zone.Height, pix.Width, pix.Height);

                            using (var page = engine.Process(pix, region))
                            {
                                row[zone.Label] = page.GetText().Trim();
                            }
                        }
                    }

                    allResults.Add(row);
                }
            }

            ExportToCsv(allResults, System.IO.Path.Combine(folder.Path, "ocr_results.csv"));

            var doneDialog = new ContentDialog
            {
                Title = "Done",
                Content = $"Processed {imageFiles.Count} files. Results saved to ocr_results.csv.",
                CloseButtonText = "OK",
                XamlRoot = this.Content.XamlRoot
            };
            _ = doneDialog.ShowAsync();
        }

        private Tesseract.Rect ScaleZoneToImage(double zoneX, double zoneY, double zoneWidth, double zoneHeight, int imageWidth, int imageHeight)
        {
            const double canvasWidth = 1000;
            const double canvasHeight = 1000;

            double scaleX = imageWidth / canvasWidth;
            double scaleY = imageHeight / canvasHeight;

            return new Tesseract.Rect(
                (int)(zoneX * scaleX),
                (int)(zoneY * scaleY),
                (int)(zoneWidth * scaleX),
                (int)(zoneHeight * scaleY)
            );
        }

        private void ExportToCsv(List<Dictionary<string, string>> allResults, string outputPath)
        {
            var allHeaders = allResults.SelectMany(r => r.Keys).Distinct().ToList();
            var csv = new StringBuilder();
            csv.AppendLine(string.Join(",", allHeaders));

            foreach (var row in allResults)
            {
                csv.AppendLine(string.Join(",", allHeaders.Select(h =>
                    row.TryGetValue(h, out var v) ? $"\"{v.Replace("\"", "\"\"")}\"" : "")));
            }

            File.WriteAllText(outputPath, csv.ToString());
        }

        // Helper: get bounding rect for a grid on the canvas
        private Windows.Foundation.Rect GetGridRect(Grid g)
        {
            double left = Canvas.GetLeft(g);
            double top = Canvas.GetTop(g);
            return new Windows.Foundation.Rect(left, top, g.Width, g.Height);
        }

        private bool RectsOverlap(Windows.Foundation.Rect a, Windows.Foundation.Rect b)
        {
            return a.X < b.X + b.Width && a.X + a.Width > b.X && a.Y < b.Y + b.Height && a.Y + a.Height > b.Y;
        }

        private bool IntersectsAny(Windows.Foundation.Rect rect, Grid ignore = null)
        {
            foreach (var child in DrawingCanvas.Children.OfType<Grid>())
            {
                if (child == ignore) continue;
                var r = GetGridRect(child);
                if (RectsOverlap(rect, r)) return true;
            }
            return false;
        }

        /// <summary>
        /// Placeholder event handler for choosing a folder.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data.</param>
        private async void ChooseFolder_click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.Zones.Count == 0)
            {
                var dlg = new ContentDialog
                {
                    Title = "No zones defined",
                    Content = "Please define at least one zone before running OCR.",
                    CloseButtonText = "OK",
                    XamlRoot = this.Content.XamlRoot
                };
                _ = await dlg.ShowAsync();
                return;
            }

            var picker = new FolderPicker();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            picker.SuggestedStartLocation = PickerLocationId.Desktop;
            picker.FileTypeFilter.Add("*");

            var folder = await picker.PickSingleFolderAsync();
            if (folder == null) return;

            var files = await folder.GetFilesAsync();
            var imageFiles = new System.Collections.Generic.List<StorageFile>();
            foreach (var f in files)
            {
                var ext = System.IO.Path.GetExtension(f.Path).ToLowerInvariant();
                if (ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".tif" || ext == ".bmp")
                    imageFiles.Add(f);
            }

            if (imageFiles.Count == 0)
            {
                var dlg2 = new ContentDialog
                {
                    Title = "No images",
                    Content = "No image files were found in the selected folder.",
                    CloseButtonText = "OK",
                    XamlRoot = this.Content.XamlRoot
                };
                _ = await dlg2.ShowAsync();
                return;
            }

            var sb = new StringBuilder();
            // header
            sb.Append("FileName");
            foreach (var z in ViewModel.Zones)
            {
                sb.Append(",");
                sb.Append(EscapeCsv(z.Label ?? ""));
            }
            sb.AppendLine();

            var ocr = new Services.OcrService();

            foreach (var file in imageFiles)
            {
                var row = new System.Collections.Generic.List<string>();
                row.Add(EscapeCsv(System.IO.Path.GetFileName(file.Path)));

                foreach (var z in ViewModel.Zones)
                {
                    // crop and OCR
                    try
                    {
                        var tempPath = await CropImageAsync(file.Path, z);
                        var text = ocr.ProcessImage(tempPath)?.Replace('\r', ' ').Replace('\n', ' ').Trim();
                        row.Add(EscapeCsv(text ?? ""));
                    }
                    catch (Exception ex)
                    {
                        row.Add(EscapeCsv("") );
                        Console.WriteLine($"OCR error for {file.Path}: {ex.Message}");
                    }
                }

                sb.AppendLine(string.Join(",", row));
            }

            var outPath = System.IO.Path.Combine(folder.Path, "ocr_output.csv");
            File.WriteAllText(outPath, sb.ToString());

            var done = new ContentDialog
            {
                Title = "OCR Complete",
                Content = $"OCR completed. CSV saved to: {outPath}",
                CloseButtonText = "OK",
                XamlRoot = this.Content.XamlRoot
            };
            _ = await done.ShowAsync();
        }

        private static string EscapeCsv(string s)
        {
            if (s == null) return "";
            if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
            {
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            }
            return s;
        }

        private async System.Threading.Tasks.Task<string> CropImageAsync(string sourcePath, Models.OcrZone zone)
        {
            // If the source is a PDF, render the first page to a temporary PNG first.
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

                // Determine scale between displayed container and actual image pixels
                double displayWidth = ImageContainer.Width;
                double displayHeight = ImageContainer.Height;
                double scaleX = (displayWidth > 0) ? (double)decoder.PixelWidth / displayWidth : 1.0;
                double scaleY = (displayHeight > 0) ? (double)decoder.PixelHeight / displayHeight : 1.0;

                uint x = (uint)Math.Max(0, Math.Round(zone.X * scaleX));
                uint y = (uint)Math.Max(0, Math.Round(zone.Y * scaleY));
                uint w = (uint)Math.Max(1, Math.Round(zone.Width * scaleX));
                uint h = (uint)Math.Max(1, Math.Round(zone.Height * scaleY));

                // clamp
                if (x + w > decoder.PixelWidth) w = decoder.PixelWidth - x;
                if (y + h > decoder.PixelHeight) h = decoder.PixelHeight - y;

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

        private async System.Threading.Tasks.Task<string> RenderPdfFirstPageToPngAsync(string pdfPath)
        {
            try
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
            catch (Exception ex)
            {
                Console.WriteLine($"PDF render error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Switches the application interaction mode to drawing mode.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data.</param>
        private void DrawMode_Click(object sender, RoutedEventArgs e) => _isDrawingMode = true;

        /// <summary>
        /// Switches the application interaction mode to selection/drag mode.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data.</param>
        private void SelectMode_Click(object sender, RoutedEventArgs e) => _isDrawingMode = false;

        /// <summary>
        /// Clears all drawn zones from the canvas.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data.</param>
        private void ClearZones_Click(object sender, RoutedEventArgs e)
        {
            DrawingCanvas.Children.Clear();
            ViewModel.Zones.Clear();
        }

        /// <summary>
        /// Handles pointer pressed events on the canvas, initiating the drawing of a new zone.
        /// </summary>
        /// <param name="sender">The canvas that raised the event.</param>
        /// <param name="e">The pointer event details.</param>
        private void Canvas_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!_isDrawingMode) return;
            if (!_isLoaded)
            {
                string message = "File must be first loaded.";
                var dialog = new ContentDialog
                {
                    Title = "Error",
                    Content = message,
                    CloseButtonText = "OK",
                    XamlRoot = this.Content.XamlRoot
                };
                _ = dialog.ShowAsync();

                return;
            }

            // Capturing the pointer ensures PointerMoved/PointerReleased keep firing on this
            // canvas even if the cursor moves outside it mid-drag (fast mouse movement, etc).
            DrawingCanvas.CapturePointer(e.Pointer);
            _isDrawing = true;
            _startPoint = e.GetCurrentPoint(DrawingCanvas).Position;

            var grid = new Grid
            {
                // Lets the whole zone be dragged around later, in select mode (see Rect_ManipulationDelta).
                ManipulationMode = ManipulationModes.TranslateX | ManipulationModes.TranslateY,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };

            grid.ManipulationDelta += Rect_ManipulationDelta;

            // Keep any label max width in sync with grid size so text trims correctly.
            grid.SizeChanged += (s, evt) =>
            {
                var lb = grid.Children.OfType<Border>().FirstOrDefault(x => x.Child is TextBlock);
                if (lb != null)
                {
                    lb.MaxWidth = Math.Max(0, grid.Width - 8);
                }
            };

            var rect = new Rectangle
            {
                Stroke = new SolidColorBrush(Microsoft.UI.Colors.Red),
                StrokeThickness = 2,
                Fill = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(50, 255, 0, 0))
            };
            grid.Children.Add(rect);

            // Resize handle pinned to the top-right corner of the zone.
            var thumb = new Thumb
            {
                Width = 16,
                Height = 16,
                Background = new SolidColorBrush(Microsoft.UI.Colors.White),
                BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Red),
                BorderThickness = new Thickness(2),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -8, -8, 0),
                CornerRadius = new CornerRadius(8)
            };

            // When resizing starts, mark state so move interactions are ignored.
            thumb.DragStarted += (s, ev) =>
            {
                _isResizing = true;
                _resizingGrid = grid;
            };

            thumb.DragCompleted += (s, ev) =>
            {
                _isResizing = false;
                if (_resizingGrid != null && _resizingGrid.Tag is Models.OcrZone oz)
                {
                    oz.X = Canvas.GetLeft(_resizingGrid);
                    oz.Y = Canvas.GetTop(_resizingGrid);
                    oz.Width = _resizingGrid.Width;
                    oz.Height = _resizingGrid.Height;
                }
                _resizingGrid = null;
            };

            thumb.DragDelta += (s, ev) =>
            {
                // Resizing is only allowed outside drawing mode
                if (_isDrawingMode) return;

                double left = Canvas.GetLeft(grid);
                double top = Canvas.GetTop(grid);
                double newWidth = Math.Max(MinZoneWidth, grid.Width + ev.HorizontalChange);
                double newTop = top + ev.VerticalChange;
                double newHeight = Math.Max(MinZoneHeight, grid.Height - ev.VerticalChange);

                // Clamp to image bounds
                if (left + newWidth > ImageContainer.Width) newWidth = ImageContainer.Width - left;
                if (newTop < 0)
                {
                    // push top to zero and adjust height accordingly
                    newHeight = newHeight - (0 - newTop);
                    newTop = 0;
                }

                var prospective = new Windows.Foundation.Rect(left, newTop, newWidth, newHeight);
                if (IntersectsAny(prospective, grid))
                {
                    // do not apply resize if it would overlap another zone
                    return;
                }

                Canvas.SetTop(grid, newTop);
                grid.Width = newWidth;
                grid.Height = newHeight;
                // Update linked model if present
                if (grid.Tag is Models.OcrZone oz2)
                {
                    oz2.X = left;
                    oz2.Y = newTop;
                    oz2.Width = newWidth;
                    oz2.Height = newHeight;
                }
            };

            grid.Children.Add(thumb);

            _currentGrid = grid;

            Canvas.SetLeft(_currentGrid, _startPoint.X);
            Canvas.SetTop(_currentGrid, _startPoint.Y);

            DrawingCanvas.Children.Add(_currentGrid);
        }

        /// <summary>
        /// Handles pointer moved events on the canvas, resizing the zone currently being drawn.
        /// </summary>
        /// <param name="sender">The canvas that raised the event.</param>
        /// <param name="e">The pointer event details.</param>
        private void Canvas_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_isDrawing || _currentGrid == null) return;

            var currentPoint = e.GetCurrentPoint(DrawingCanvas).Position;

            // Min/Abs handle dragging in any direction (e.g. up-left) so the rectangle
            // always ends up with a positive width/height and the correct top-left origin,
            // regardless of which way the user dragged from the start point.
            var x = Math.Min(currentPoint.X, _startPoint.X);
            var y = Math.Min(currentPoint.Y, _startPoint.Y);
            var width = Math.Abs(currentPoint.X - _startPoint.X);
            var height = Math.Abs(currentPoint.Y - _startPoint.Y);

            // Clamp to image container bounds
            x = Math.Max(0, Math.Min(x, ImageContainer.Width));
            y = Math.Max(0, Math.Min(y, ImageContainer.Height));
            if (x + width > ImageContainer.Width) width = ImageContainer.Width - x;
            if (y + height > ImageContainer.Height) height = ImageContainer.Height - y;

            Canvas.SetLeft(_currentGrid, x);
            Canvas.SetTop(_currentGrid, y);
            _currentGrid.Width = Math.Max(MinZoneWidth, width);
            _currentGrid.Height = Math.Max(MinZoneHeight, height);
        }

        /// <summary>
        /// Handles pointer released events on the canvas, finishing the drawing and prompting for a label.
        /// </summary>
        /// <param name="sender">The canvas that raised the event.</param>
        /// <param name="e">The pointer event details.</param>
        private async void Canvas_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (!_isDrawing) return;

            _isDrawing = false;
            DrawingCanvas.ReleasePointerCapture(e.Pointer);

            if (_currentGrid != null && _currentGrid.Width > MinZoneWidth && _currentGrid.Height > MinZoneHeight)
            {
                // Before finalizing, ensure new zone does not overlap others
                var rect = GetGridRect(_currentGrid);
                if (IntersectsAny(rect, _currentGrid))
                {
                    var overlapDlg = new ContentDialog
                    {
                        Title = "Overlap detected",
                        Content = "The drawn zone overlaps an existing zone. Please redraw or move the new zone.",
                        CloseButtonText = "OK",
                        XamlRoot = this.Content.XamlRoot
                    };
                    _ = await overlapDlg.ShowAsync();
                    DrawingCanvas.Children.Remove(_currentGrid);
                    _currentGrid = null;
                    return;
                }

                // Only prompt for a label if the drawn zone is big enough to be intentional;
                // otherwise treat it as an accidental click/tiny drag and discard it below.
                await PromptForLabelAsync(_currentGrid);
            }
            else if (_currentGrid != null)
            {
                DrawingCanvas.Children.Remove(_currentGrid);
            }

            _currentGrid = null;
        }

        /// <summary>
        /// Handles the manipulation delta event for rectangles, allowing them to be moved.
        /// </summary>
        /// <param name="sender">The UI element that was moved.</param>
        /// <param name="e">The manipulation delta event details.</param>
        private void Rect_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
        {
            // Dragging existing zones is a select-mode-only interaction.
            if (_isDrawingMode) return;

            // If currently resizing, ignore manipulation translations
            if (_isResizing) return;

            if (sender is FrameworkElement element && element is Grid grid)
            {
                var left = Canvas.GetLeft(element);
                var top = Canvas.GetTop(element);

                double newLeft = left + e.Delta.Translation.X;
                double newTop = top + e.Delta.Translation.Y;

                // Clamp to image bounds
                newLeft = Math.Max(0, Math.Min(newLeft, ImageContainer.Width - grid.Width));
                newTop = Math.Max(0, Math.Min(newTop, ImageContainer.Height - grid.Height));

                var prospective = new Windows.Foundation.Rect(newLeft, newTop, grid.Width, grid.Height);
                if (IntersectsAny(prospective, grid))
                {
                    // Abort movement if it would overlap another zone
                    return;
                }

                Canvas.SetLeft(element, newLeft);
                Canvas.SetTop(element, newTop);

                // Update linked model if present
                if (grid.Tag is Models.OcrZone oz)
                {
                    oz.X = newLeft;
                    oz.Y = newTop;
                }
            }
        }

        /// <summary>
        /// Prompts the user to enter a label for the newly created zone and adds the label to the canvas.
        /// </summary>
        /// <param name="grid">The grid representing the newly created zone.</param>
        private async System.Threading.Tasks.Task PromptForLabelAsync(Grid grid)
        {
            TextBox zoneLabelBox = new TextBox
            {
                PlaceholderText = "Zone Label",
                Margin = new Thickness(0, 0, 0, 10)
            };

            ComboBox zoneTypeBox = new ComboBox
            {
                PlaceholderText = "Choose Data Type",
                Margin = new Thickness(0, 0, 0, 10)
            };

            // Only shown/used when zoneTypeBox's selection is "Date" (see SelectionChanged below).
            ComboBox zoneDateBox = new ComboBox
            {
                PlaceholderText = "Choose date format",
                Margin = new Thickness(0, 0, 0, 10)
            };

            zoneTypeBox.Items.Add("Date");
            zoneTypeBox.Items.Add("Text");
            zoneTypeBox.Items.Add("Number");

            zoneDateBox.Items.Add("MM/DD/YYYY");
            zoneDateBox.Items.Add("DD/MM/YYYY");
            zoneDateBox.Items.Add("YYYY-MM-DD");

            StackPanel panel = new StackPanel();
            panel.Children.Add(zoneLabelBox);
            panel.Children.Add(zoneTypeBox);
            // zoneDateBox is deliberately NOT added here — it's added/removed dynamically
            // below, only while the dialog is open, so it only appears for "Date" zones.

            // Runs live while the ContentDialog is open (unlike a check placed before
            // ShowAsync, which would fire before any selection exists). Removing first
            // avoids adding the box twice if the user flips between types repeatedly.
            zoneTypeBox.SelectionChanged += (s, e) =>
            {
                panel.Children.Remove(zoneDateBox);

                if (zoneTypeBox.SelectedItem?.ToString() == "Date")
                {
                    panel.Children.Add(zoneDateBox);
                }
            };

            var dialog = new ContentDialog
            {
                Title = "Zone Label",
                Content = panel,
                PrimaryButtonText = "OK",
                CloseButtonText = "Cancel",
                XamlRoot = this.Content.XamlRoot
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary &&
                !string.IsNullOrWhiteSpace(zoneLabelBox.Text) &&
                zoneTypeBox.SelectedItem != null)
            {
                string zoneType = zoneTypeBox.SelectedItem.ToString();
                // Null whenever the zone type isn't "Date" (box was never shown/selected).
                string dateFormat = zoneDateBox.SelectedItem?.ToString();

                var labelBorder = new Border
                {
                    Background = new SolidColorBrush(Microsoft.UI.Colors.Red),
                    Padding = new Thickness(4),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    // Negative top margin lifts the label above the zone rectangle instead
                    // of overlapping its contents.
                    Margin = new Thickness(0, -28, 0, 0)
                };

                string displayText = zoneType == "Date" && dateFormat != null
                    ? $"{zoneLabelBox.Text} ({zoneType}: {dateFormat})"
                    : $"{zoneLabelBox.Text} ({zoneType})";

                var label = new TextBlock
                {
                    Text = displayText,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };

                // Tooltip shows full name when label doesn't fit
                Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(labelBorder, displayText);

                labelBorder.Child = label;

                grid.Children.Add(labelBorder);

                // Register zone in the ViewModel so it can be used for OCR/export later.
                var zone = new Models.OcrZone
                {
                    X = Canvas.GetLeft(grid),
                    Y = Canvas.GetTop(grid),
                    Width = grid.Width,
                    Height = grid.Height,
                    Label = zoneLabelBox.Text
                };

                ViewModel.Zones.Add(zone);
                grid.Tag = zone;
            }
        }
    }
}