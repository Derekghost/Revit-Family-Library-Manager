using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.IO;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using RevitFamilyBrowser.ViewModels;

namespace RevitFamilyBrowser.ViewModels
{
    public class FamilyLibraryViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<TreeNodeViewModel> RootNodes { get; } = new ObservableCollection<TreeNodeViewModel>();
        public ObservableCollection<FamilyThumbItemViewModel> FamilyFiles { get; } = new ObservableCollection<FamilyThumbItemViewModel>();
        public ICollectionView FamilyFilesView { get; private set; }

        private int _scanVersion = 0;
        private readonly Dispatcher _ui;

        // ✅ 新增：根目录路径（可绑定到UI显示）
        private string _rootPath;
        public string RootPath
        {
            get => _rootPath;
            private set
            {
                if (_rootPath == value) return;
                _rootPath = value;
                OnpropertyChanged();
            }
        }

        // ✅ 新增：状态栏文本（你原来是 get; private set; 但没通知UI，这里改成可通知）
        private string _statusText;
        public string StatusText
        {
            get => _statusText;
            private set
            {
                if (_statusText == value) return;
                _statusText = value;
                OnpropertyChanged();
            }
        }

        // ✅ 新增：选择文件夹命令（左侧按钮绑定）
        public RelayCommand BrowseRootCommand { get; }

        private FolderNodeViewModel _selectedFolder;
        public FolderNodeViewModel SelectedFolder
        {
            get => _selectedFolder;
            set
            {
                if (_selectedFolder == value) return;
                _selectedFolder = value;
                OnpropertyChanged();
                RefreshFamilyFiles();
            }
        }

        private string _searchText = "";
        public string SearchText
        {
            get { return _searchText; }
            set
            {
                if (_searchText == value) return;
                _searchText = value ?? "";
                OnpropertyChanged();
                if (FamilyFilesView != null) FamilyFilesView.Refresh();
            }
        }

        // ✅ 改造点：构造函数参数改成可选（兼容你旧用法：new FamilyLibraryViewModel(@"D:\User\Family")）
        public FamilyLibraryViewModel(string rootPath = null)
        {
            _ui = Dispatcher.CurrentDispatcher;

            FamilyFilesView = CollectionViewSource.GetDefaultView(FamilyFiles);
            FamilyFilesView.Filter = FilterFamilyFile;

            BrowseRootCommand = new RelayCommand(_ => BrowseForRootFolder());

            // ✅ 固定路径（先注释保留，后续想切回直接取消注释即可）
            // SetRootPath(@"D:\User\Family");

            // ✅ 当前：不固定路径（rootPath 传 null 就等待用户选择）
            SetRootPath(string.IsNullOrWhiteSpace(rootPath) ? null : rootPath);
        }

        private void SetRootPath(string rootPath)
        {
            RootPath = rootPath;

            // 重置界面数据
            RootNodes.Clear();
            FamilyFiles.Clear();
            _selectedFolder = null;
            OnpropertyChanged(nameof(SelectedFolder));

            if (string.IsNullOrWhiteSpace(rootPath))
            {
                StatusText = "请选择族库目录";
                if (FamilyFilesView != null) FamilyFilesView.Refresh();
                return;
            }

            if (!Directory.Exists(rootPath))
            {
                StatusText = "目录不存在或无权限访问： " + rootPath;
                // 仍然显示一个根节点，让用户知道你在看哪个路径
                var invalidRoot = new FolderNodeViewModel(rootPath, null, true, this);
                RootNodes.Add(invalidRoot);
                return;
            }

            StatusText = "Root: " + rootPath;
            var rootNode = new FolderNodeViewModel(rootPath, null, true, this);
            RootNodes.Add(rootNode);

            // ✅ 让右侧立刻显示根目录（含子文件夹内的族）
            SelectedFolder = rootNode;
        }

        private void BrowseForRootFolder()
        {
            try
            {
                // 用 WinForms 的 FolderBrowserDialog，在 Revit 宿主里更稳
                using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
                {
                    dlg.Description = "选择族库根目录";
                    if (!string.IsNullOrWhiteSpace(RootPath) && Directory.Exists(RootPath))
                        dlg.SelectedPath = RootPath;

                    var result = dlg.ShowDialog();
                    if (result != System.Windows.Forms.DialogResult.OK) return;

                    var path = dlg.SelectedPath;
                    if (string.IsNullOrWhiteSpace(path)) return;

                    SetRootPath(path);
                }
            }
            catch
            {
                // 需要的话你可以 StatusText 提示
            }
        }

        private bool FilterFamilyFile(object obj)
        {
            var item = obj as FamilyThumbItemViewModel;
            if (item == null) return false;

            var s = (SearchText ?? "").Trim();
            if (s.Length == 0) return true;

            return item.FileName != null && item.FileName.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void RefreshFamilyFiles()
        {
            FamilyFiles.Clear();

            if (_selectedFolder == null)
            {
                if (FamilyFilesView != null) FamilyFilesView.Refresh();
                return;
            }

            string dir = _selectedFolder.FullPath;
            if (!Directory.Exists(dir))
            {
                if (FamilyFilesView != null) FamilyFilesView.Refresh();
                return;
            }

            int myVersion = ++_scanVersion;

            // 后台递归扫描（避免 UI 卡死）
            Task.Run(() =>
            {
                var list = new List<string>();
                try
                {
                    foreach (var f in Directory.EnumerateFiles(dir, "*.rfa", SearchOption.AllDirectories))
                        list.Add(f);
                }
                catch
                {
                    // 权限/路径异常直接忽略
                }
                return list;
            })
            .ContinueWith(t =>
            {
                _ui.BeginInvoke(new Action(() =>
                {
                    if (myVersion != _scanVersion) return;

                    var files = (t.Status == TaskStatus.RanToCompletion && t.Result != null) ? t.Result : new List<string>();

                    foreach (var f in files)
                    {
                        var name = Path.GetFileNameWithoutExtension(f);
                        var vm = new FamilyThumbItemViewModel(f, name);

                        vm.LoadCommand = new RelayCommand(_ =>
                        {
                            RevitBridge.RevitFamilyLoader.RequestLoad(vm.FullPath);
                        });

                        vm.PlaceCommand = new RelayCommand(_ =>
                        {
                            RevitBridge.RevitFamilyPlacer.RequestPlace(vm.FileName, vm.FullPath);
                        });

                        FamilyFiles.Add(vm);

                        ThumbnailQueue.Enqueue(f, 256, img =>
                        {
                            if (img != null) vm.Thumbnail = img;
                        });
                    }

                    if (FamilyFilesView != null) FamilyFilesView.Refresh();
                }));
            });
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnpropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
