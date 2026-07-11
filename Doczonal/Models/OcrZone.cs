using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Doczonal.Models
{
    public class OcrZone : INotifyPropertyChanged
    {
        private double _x;
        private double _y;
        private double _width;
        private double _height;
        private string _label;

        public double X { get => _x; set { _x = value; OnPropertyChanged(); } }
        public double Y { get => _y; set { _y = value; OnPropertyChanged(); } }
        public double Width { get => _width; set { _width = value; OnPropertyChanged(); } }
        public double Height { get => _height; set { _height = value; OnPropertyChanged(); } }
        public string Label { get => _label; set { _label = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
