using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Foundation;
using Windows.Foundation.Collections;

namespace Doczonal
{
    public sealed partial class MainWindow : Window
    {
        private bool _isDrawingMode = true;
        private bool _isDrawing = false;
        private Point _startPoint;
        private Rectangle _currentRect;

        public ViewModels.MainViewModel ViewModel { get; } = new ViewModels.MainViewModel();

        public MainWindow()
        {
            this.InitializeComponent();
        }

        private async void LoadImage_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            picker.ViewMode = PickerViewMode.Thumbnail;
            picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".png");

            StorageFile file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                using var stream = await file.OpenAsync(FileAccessMode.Read);
                var bitmapImage = new BitmapImage();
                await bitmapImage.SetSourceAsync(stream);
                BackgroundImage.Source = bitmapImage;
                
                ImageContainer.Width = bitmapImage.PixelWidth;
                ImageContainer.Height = bitmapImage.PixelHeight;
            }
        }

        private void Done_Click(object sender, RoutedEventArgs e)
        {
            // Here you can handle the completion of the zone drawing process.
            // For example, you might want to save the zones or perform some action with them.
            // Currently, this just shows a message box.
            var zones = DrawingCanvas.Children.OfType<Rectangle>().Select(r => new
            {
                X = Canvas.GetLeft(r),
                Y = Canvas.GetTop(r),
                Width = r.Width,
                Height = r.Height
            }).ToList();
            string message = "Zones drawn:\n" + string.Join("\n", zones.Select(z => $"X: {z.X}, Y: {z.Y}, Width: {z.Width}, Height: {z.Height}"));
            var dialog = new ContentDialog
            {
                Title = "Drawing Complete",
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = this.Content.XamlRoot
            };
            _ = dialog.ShowAsync();
        }

        private void DrawMode_Click(object sender, RoutedEventArgs e) => _isDrawingMode = true;
        private void SelectMode_Click(object sender, RoutedEventArgs e) => _isDrawingMode = false;
        private void ClearZones_Click(object sender, RoutedEventArgs e) => DrawingCanvas.Children.Clear();

        private void Canvas_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!_isDrawingMode) return;

            DrawingCanvas.CapturePointer(e.Pointer);
            _isDrawing = true;
            _startPoint = e.GetCurrentPoint(DrawingCanvas).Position;

            _currentRect = new Rectangle
            {
                Stroke = new SolidColorBrush(Microsoft.UI.Colors.Red),
                StrokeThickness = 2,
                Fill = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(50, 255, 0, 0)),
                ManipulationMode = ManipulationModes.TranslateX | ManipulationModes.TranslateY
            };
            
            _currentRect.ManipulationDelta += Rect_ManipulationDelta;

            Canvas.SetLeft(_currentRect, _startPoint.X);
            Canvas.SetTop(_currentRect, _startPoint.Y);
            
            DrawingCanvas.Children.Add(_currentRect);
        }

        private void Canvas_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_isDrawing || _currentRect == null) return;

            var currentPoint = e.GetCurrentPoint(DrawingCanvas).Position;

            var x = Math.Min(currentPoint.X, _startPoint.X);
            var y = Math.Min(currentPoint.Y, _startPoint.Y);
            var width = Math.Abs(currentPoint.X - _startPoint.X);
            var height = Math.Abs(currentPoint.Y - _startPoint.Y);

            Canvas.SetLeft(_currentRect, x);
            Canvas.SetTop(_currentRect, y);
            _currentRect.Width = width;
            _currentRect.Height = height;
        }

        private async void Canvas_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (!_isDrawing) return;

            _isDrawing = false;
            DrawingCanvas.ReleasePointerCapture(e.Pointer);

            if (_currentRect != null && _currentRect.Width > 10 && _currentRect.Height > 10)
            {
                await PromptForLabelAsync(_currentRect);
            }
            else if (_currentRect != null)
            {
                DrawingCanvas.Children.Remove(_currentRect);
            }

            _currentRect = null;
        }

        private void Rect_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
        {
            if (_isDrawingMode) return;

            if (sender is FrameworkElement element)
            {
                var left = Canvas.GetLeft(element);
                var top = Canvas.GetTop(element);
                Canvas.SetLeft(element, left + e.Delta.Translation.X);
                Canvas.SetTop(element, top + e.Delta.Translation.Y);
            }
        }

        private async System.Threading.Tasks.Task PromptForLabelAsync(Rectangle rect)
        {
            var tb = new TextBox { PlaceholderText = "Enter zone label..." };
            var dialog = new ContentDialog
            {
                Title = "Zone Label",
                Content = tb,
                PrimaryButtonText = "OK",
                CloseButtonText = "Cancel",
                XamlRoot = this.Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(tb.Text))
            {
                var labelBorder = new Border
                {
                    Background = new SolidColorBrush(Microsoft.UI.Colors.Red),
                    Padding = new Thickness(4)
                };

                var label = new TextBlock
                {
                    Text = tb.Text,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
                };

                labelBorder.Child = label;

                Canvas.SetLeft(labelBorder, Canvas.GetLeft(rect));
                Canvas.SetTop(labelBorder, Canvas.GetTop(rect) - 28);
                DrawingCanvas.Children.Add(labelBorder);
            }
        }
    }
}

