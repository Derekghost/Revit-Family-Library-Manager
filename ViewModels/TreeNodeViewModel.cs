using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System;

namespace RevitFamilyBrowser.ViewModels
{
    public abstract class TreeNodeViewModel : INotifyPropertyChanged
    {
        private bool _isExpanded;
        private bool _isSelected;
        
        public ObservableCollection<TreeNodeViewModel> Children { get; } = new ObservableCollection<TreeNodeViewModel>();
        public TreeNodeViewModel Parent { get; private set; }

        protected Action<TreeNodeViewModel> ExpandChildrenCallback { get; set; }

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value) return;
                {
                    _isExpanded = value;
                    OnPropertyChanged();
                }
                if (_isExpanded)
                    OnExpanded();
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                OnPropertyChanged();
                OnSelected();
            }
        }

        protected TreeNodeViewModel(TreeNodeViewModel parent)
        {
            Parent = parent;
        }

        protected virtual void OnExpanded() 
        {
            ExpandChildrenCallback?.Invoke(this);
        }

        protected virtual void OnSelected() { }

        
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

}
