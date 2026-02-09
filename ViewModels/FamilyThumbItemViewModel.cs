using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Input;

namespace RevitFamilyBrowser.ViewModels
{
    public class FamilyThumbItemViewModel : INotifyPropertyChanged
    {
        public string FullPath { get; private set; }
        public string FileName { get; private set; }
        private ImageSource _thumbnail;
        public ImageSource Thumbnail
        {
            get => _thumbnail;
            set { _thumbnail = value; OnpropertyChanged(); }
        }

        public ICommand LoadCommand { get; set; }

        public ICommand PlaceCommand { get; set; }

        public FamilyThumbItemViewModel(string fullPath, string fileName)
        {
            FullPath = fullPath;
            FileName = fileName;
        }
        public event PropertyChangedEventHandler PropertyChanged;
        private void OnpropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
