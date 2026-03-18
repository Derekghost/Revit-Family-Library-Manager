using System.Collections.ObjectModel;
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
        private string _parametersStatus = "点击卡片读取参数";
        public string ParametersStatus
        {
            get => _parametersStatus;
            set
            {
                if (_parametersStatus == value) return;
                _parametersStatus = value;
                OnpropertyChanged();
            }
        }
        public ObservableCollection<FamilyParameterItemViewModel> Parameters { get; set; } = new ObservableCollection<FamilyParameterItemViewModel>();
        private bool _showLoadPlaceActions = true;
        public bool ShowLoadPlaceActions
        {
            get => _showLoadPlaceActions;
            set
            {
                if (_showLoadPlaceActions == value) return;
                _showLoadPlaceActions = value;
                OnpropertyChanged();
            }
        }
        private bool _canReadProjectParameters = true;
        public bool CanReadProjectParameters
        {
            get => _canReadProjectParameters;
            set
            {
                if (_canReadProjectParameters == value) return;
                _canReadProjectParameters = value;
                OnpropertyChanged();
            }
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

    public class FamilyParameterItemViewModel
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public string Source { get; set; }
    }
}
