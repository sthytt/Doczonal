using System;
using System.Collections.ObjectModel;
using Doczonal.Models;

namespace Doczonal.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        public ObservableCollection<OcrZone> Zones { get; } = new ObservableCollection<OcrZone>();

        private string _imagePath;
        public string ImagePath
        {
            get => _imagePath;
            set => SetProperty(ref _imagePath, value);
        }

        public MainViewModel()
        {
        }
    }
}
