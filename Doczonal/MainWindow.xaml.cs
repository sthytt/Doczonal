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
        private bool _isLoaded = false;
        private bool _isDrawingMode = true;
        private bool _isDrawing = false;
        private Point _startPoint;
        private Grid _currentGrid;

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

                ImageContainer.Width = 1000;
                ImageContainer.Height = 800;
            }

            _isLoaded = true;
        }

        /// <summary>
        /// Handles the event when the Done button is clicked. Gathers all zones and displays them in a dialog.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data.</param>
        private void Done_Click(object sender, RoutedEventArgs e)
        {
            var zones = DrawingCanvas.Children.OfType<Grid>().Select(g => new
            {
                X = Canvas.GetLeft(g),
                Y = Canvas.GetTop(g),
                Width = g.Width,
                Height = g.Height
            }).ToList();
            string message = "Established zones:\n" + string.Join("\n", zones.Select(z => $"X: {z.X}, Y: {z.Y}, Width: {z.Width}, Height: {z.Height}"));
            var dialog = new ContentDialog
            {
                Title = "Drawing Complete",
                Content = message,
                CloseButtonText = "Choose folder",
                XamlRoot = this.Content.XamlRoot
            };
            _ = dialog.ShowAsync();
        }

        /// <summary>
        /// Placeholder event handler for choosing a folder.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data.</param>
        private void ChooseFolder_click(object sender, RoutedEventArgs e)
        {

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
        private void ClearZones_Click(object sender, RoutedEventArgs e) => DrawingCanvas.Children.Clear();

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

            DrawingCanvas.CapturePointer(e.Pointer);
            _isDrawing = true;
            _startPoint = e.GetCurrentPoint(DrawingCanvas).Position;

            var grid = new Grid
            {
                ManipulationMode = ManipulationModes.TranslateX | ManipulationModes.TranslateY,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            
            grid.ManipulationDelta += Rect_ManipulationDelta;

            var rect = new Rectangle
            {
                Stroke = new SolidColorBrush(Microsoft.UI.Colors.Red),
                StrokeThickness = 2,
                Fill = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(50, 255, 0, 0))
            };
            grid.Children.Add(rect);

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
            thumb.DragDelta += (s, ev) =>
            {
                if (!_isDrawingMode)
                {
                    grid.Width = Math.Max(10, grid.Width + ev.HorizontalChange);
                    
                    double newHeight = grid.Height - ev.VerticalChange;
                    if (newHeight >= 10)
                    {
                        var top = Canvas.GetTop(grid);
                        Canvas.SetTop(grid, top + ev.VerticalChange);
                        grid.Height = newHeight;
                    }
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

            var x = Math.Min(currentPoint.X, _startPoint.X);
            var y = Math.Min(currentPoint.Y, _startPoint.Y);
            var width = Math.Abs(currentPoint.X - _startPoint.X);
            var height = Math.Abs(currentPoint.Y - _startPoint.Y);

            Canvas.SetLeft(_currentGrid, x);
            Canvas.SetTop(_currentGrid, y);
            _currentGrid.Width = width;
            _currentGrid.Height = height;
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

            if (_currentGrid != null && _currentGrid.Width > 10 && _currentGrid.Height > 10)
            {
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
            if (_isDrawingMode) return;

            if (sender is FrameworkElement element)
            {
                var left = Canvas.GetLeft(element);
                var top = Canvas.GetTop(element);
                Canvas.SetLeft(element, left + e.Delta.Translation.X);
                Canvas.SetTop(element, top + e.Delta.Translation.Y);
            }
        }

        /// <summary>
        /// Prompts the user to enter a label for the newly created zone and adds the label to the canvas.
        /// </summary>
        /// <param name="grid">The grid representing the newly created zone.</param>
        private async System.Threading.Tasks.Task PromptForLabelAsync(Grid grid)
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
                    Padding = new Thickness(4),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, -28, 0, 0)
                };

                var label = new TextBlock
                {
                    Text = tb.Text,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
                };

                labelBorder.Child = label;

                grid.Children.Add(labelBorder);
            }
        }
    }
}

