using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.Windows.Data;
using RevitFamilyBrowser.ViewModels;
using RevitFamilyBrowser.Properties;
using RevitFamilyBrowser.RevitBridge;
using System.Data;

namespace RevitFamilyBrowser.ViewModels
{
    public class FamilyLibraryViewModel : INotifyPropertyChanged
    {
        public enum LibraryMode
        {
            Local,
            Project
        }
        public ObservableCollection<LibraryTreeNodeViewModel> RootNodes { get; } = new ObservableCollection<LibraryTreeNodeViewModel>();
        public ObservableCollection<FamilyThumbItemViewModel> FamilyFiles { get; } = new ObservableCollection<FamilyThumbItemViewModel>();
        public ICollectionView FamilyFilesView { get; private set; }

        private readonly List<ProjectFamilyNodeItem> _projectFamilies = new List<ProjectFamilyNodeItem>();
        private int _scanVersion;
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
        // ✅ 新增：打开上一次文件夹（左侧按钮绑定）
        public RelayCommand BrowseRootCommand { get; }
        public RelayCommand OpenLastRootCommand { get; }

        public RelayCommand ToggleLibraryModeCommand { get; }

        private LibraryMode _currentLibraryMode = LibraryMode.Local;
        public LibraryMode CurrentLibraryMode
        {
            get => _currentLibraryMode;
            private set
            {
                if (_currentLibraryMode == value) return;
                _currentLibraryMode = value;
                OnpropertyChanged();
                OnpropertyChanged(nameof(ToggleLibraryModeCommand));
                BrowseRootCommand?.RaiseCanExecuteChanged();
                OpenLastRootCommand?.RaiseCanExecuteChanged();
            }
        }

        public string ToggleLibraryModeText => CurrentLibraryMode == LibraryMode.Local ? "切换到项目族库" : "切换到本地族库";

        private LibraryTreeNodeViewModel _selectedNode;
        public LibraryTreeNodeViewModel SelectedNode
        {
            get => _selectedNode;
            set
            {
                if (_selectedNode == value) return;
                _selectedNode = value;
                OnpropertyChanged();
                RefreshFamilyFiles();
            }
        }

        private string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText == value) return;
                _searchText = value ?? "";
                OnpropertyChanged();
                FamilyFilesView?.Refresh();
            }
        }

        private FamilyThumbItemViewModel _selectedFamily;
        public FamilyThumbItemViewModel SelectedFamily
        {
            get => _selectedFamily;
            private set
            {
                if (_selectedFamily == value) return;
                _selectedFamily = value;
                OnpropertyChanged();
            }
        }

        private bool _isDetailsPaneOpen;
        public bool IsDetailsPaneOpen
        {
            get => _isDetailsPaneOpen;
            set
            {
                if (_isDetailsPaneOpen == value) return;
                _isDetailsPaneOpen = value;
                OnpropertyChanged();
            }
        }

        public RelayCommand OpenDetailsCommand { get; }
        public RelayCommand CloseDetailsCommand { get; }

        // ✅ 改造点：构造函数参数改成可选（兼容你旧用法：new FamilyLibraryViewModel(@"D:\User\Family")）
        public FamilyLibraryViewModel(string rootPath = null)
        {
            _ui = Dispatcher.CurrentDispatcher;

            FamilyFilesView = CollectionViewSource.GetDefaultView(FamilyFiles);
            FamilyFilesView.Filter = FilterFamilyFile;

            BrowseRootCommand = new RelayCommand(_ => BrowseForRootFolder(), _ => CurrentLibraryMode == LibraryMode.Local);
            OpenLastRootCommand = new RelayCommand(_ => OpenLastRootFolder(), _ => CurrentLibraryMode == LibraryMode.Local && HasLastRootPath());
            OpenDetailsCommand = new RelayCommand(OpenDetails);
            CloseDetailsCommand = new RelayCommand(_ => IsDetailsPaneOpen = false);

            // ✅ 固定路径（先注释保留，后续想切回直接取消注释即可）
            // SetRootPath(@"D:\User\Family");

            // ✅ 当前：不固定路径（rootPath 传 null 就等待用户选择）
            var initialPath = string.IsNullOrWhiteSpace(rootPath) ? null : rootPath;
            SetRootPath(string.IsNullOrWhiteSpace(initialPath) ? null : initialPath);
        }

        public void SetProjectFamilies(IEnumerable<ProjectFamilyNodeItem> families)
        {
            _projectFamilies.Clear();
            if (families != null)
                _projectFamilies.AddRange(families.Where(f => f != null));

            if (SelectedNode != null && SelectedNode.NodeType != LibraryTreeNodeType.Folder)
                RefreshFamilyFiles();
        }
        private bool HasLastRootPath()
        {
            return !string.IsNullOrWhiteSpace(GetLastRootPath());
        }

        private void ToggleLibraryMode()
        {
            if (CurrentLibraryMode == LibraryMode.Local)
                SwitchToProjectLibrary();
            else
                SwitchToLocalLibrary();
        }
        private void SwitchToLocalLibrary()
        {
            CurrentLibraryMode = LibraryMode.Local;

            var pathToUse = RootPath;
            if (string.IsNullOrWhiteSpace(pathToUse) || !Directory.Exists(pathToUse))
                pathToUse = GetLastRootPath();

            SetRootPath(string.IsNullOrWhiteSpace(pathToUse) ? null : pathToUse);
            OpenLastRootCommand.RaiseCanExecuteChanged();
        }
        private void SwitchToProjectLibrary()
        {
            CurrentLibraryMode = LibraryMode.Project;
            OpenLastRootCommand.RaiseCanExecuteChanged();

            RootNodes.Clear();
            FamilyFiles.Clear();
            _selectedNode = null;
            OnpropertyChanged(nameof(SelectedNode));
            UpdateStatusText("正在收集项目族...");

            RevitProjectFamilyCollector.RequestCollect(snapshot =>
            {
                _ui.BeginInvoke(new Action(() => BuildProjectTree(snapshot)));
            });
        }

        private void BuildProjectTree(ProjectFamilySnapshot snapshot)
        {
            RootNodes.Clear();
            FamilyFiles.Clear();
            _projectFamilies.Clear();
            _selectedNode = null;
            OnpropertyChanged(nameof(SelectedNode));

            var families = snapshot != null && snapshot.Families != null ? snapshot.Families : new List<ProjectFamilyItem> { };

            if (families.Count == 0)
            {
                FamilyFilesView?.Refresh();
                var message = snapshot != null && !string.IsNullOrWhiteSpace(snapshot.StatusMessage) ? snapshot.StatusMessage : "当前项目中未找到可用族";
                UpdateStatusText(message);
                return;
            }
            foreach (var family in families)
            {
                _projectFamilies.Add(new ProjectFamilyNodeItem
                {
                    Name = family.FamilyName,
                    FamilyName = family.FamilyName,
                    CategoryName = family.CategoryName,
                    FullPath = null
                });
            }

            var projectRoot = new LibraryTreeNodeViewModel(
                "项目族库根",
                LibraryTreeNodeType.FamilyCategory,
                null,
                onSelected: node => SelectedNode = node);

            var categoryNodes = _projectFamilies
                .GroupBy(f => string.IsNullOrWhiteSpace(f.CategoryName) ? "未分类" : f.CategoryName)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var categoryNode = new LibraryTreeNodeViewModel(
                        group.Key,
                        LibraryTreeNodeType.FamilyCategory,
                        projectRoot,
                        categoryName: group.Key,
                        onSelected: node => SelectedNode = node);

                    foreach (var family in group.OrderBy(f => f.FamilyName ?? f.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        var familyName = !string.IsNullOrWhiteSpace(family.FamilyName) ? family.FamilyName : family.Name;
                        categoryNode.Children.Add(new LibraryTreeNodeViewModel(
                            familyName,
                            LibraryTreeNodeType.Family,
                            categoryNode,
                            familyName: familyName,
                            categoryName: familyName,
                            onSelected: node => SelectedNode = node));
                    }

                    return categoryNode;
                });

            projectRoot.ReplaceChildren(categoryNodes);
            RootNodes.Add(projectRoot);
            projectRoot.IsExpanded = true;
            SelectedNode = projectRoot;

            var status = !string.IsNullOrWhiteSpace(snapshot.StatusMessage) ? snapshot.StatusMessage : $"已加载{families.Count}个项目族";
            UpdateStatusText(status);

        }
        private string GetLastRootPath()
        {
            try
            {
                return Settings.Default.LastRootPath;
            }
            catch
            {
                return null;
            }
        }

        private void SaveLastRootPath(string path)
        {
            try
            {
                Settings.Default.LastRootPath = path ?? string.Empty;
                Settings.Default.Save();
            }
            catch
            {
                // 写入配置失败时保持静默，避免影响主流程
            }
            finally
            {
                OpenLastRootCommand.RaiseCanExecuteChanged();
            }
        }

        private void SetRootPath(string rootPath)
        {
            RootPath = rootPath;

            // 重置界面数据
            RootNodes.Clear();
            FamilyFiles.Clear();
            _selectedNode = null;
            OnpropertyChanged(nameof(SelectedNode));

            if (string.IsNullOrWhiteSpace(rootPath))
            {
                UpdateStatusText("请选择族库目录");
                FamilyFilesView?.Refresh();
                return;
            }

            if (!Directory.Exists(rootPath))
            {
                UpdateStatusText("目录不存在或无权限访问： " + rootPath);
                // 仍然显示一个根节点，让用户知道你在看哪个路径
                var invalidRoot = new FolderNodeViewModel(rootPath, null, true, this);
                RootNodes.Add(CreateFolderNode(rootPath, null, true));
                return;
            }

            UpdateStatusText ("Root: " + rootPath);
            var rootNode = CreateFolderNode(rootPath, null, true);
            RootNodes.Add(rootNode);
            SelectedNode = rootNode;
        }

        private LibraryTreeNodeViewModel CreateFolderNode(string fullPath, LibraryTreeNodeViewModel parent, bool isRoot)
        {
            var nodeName = isRoot ? fullPath : Path.GetFileName(fullPath);
            return new LibraryTreeNodeViewModel(
                nodeName,
                LibraryTreeNodeType.Folder,
                parent,
                fullPath: fullPath,
                onSelected: node => SelectedNode = node,
                loadChildrenOnExpand: node =>
                {
                    node.ClearDummyChild();
                    foreach (var dir in SafeEnumerateDirectories(node.FullPath))
                        node.Children.Add(CreateFolderNode(dir, node, false));
                });
        }

        private static IEnumerable<string> SafeEnumerateDirectories(string path)
        {
            try { return Directory.GetDirectories(path); }
            catch { return Array.Empty<string>(); }
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
                    else if (HasLastRootPath() && Directory.Exists(GetLastRootPath()))
                        dlg.SelectedPath = GetLastRootPath();

                    var result = dlg.ShowDialog();
                    if (result != System.Windows.Forms.DialogResult.OK) return;

                    var path = dlg.SelectedPath;
                    if (string.IsNullOrWhiteSpace(path)) return;

                    SetRootPath(path);
                    SaveLastRootPath(path);
                }
            }
            catch
            {
                // 需要的话可以 StatusText 提示
            }
        }

        private void OpenLastRootFolder()
        {
            var path = GetLastRootPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                UpdateStatusText("没有上次使用的目录记录");
                return;
            }

            SetRootPath(path);
        }

        private bool FilterFamilyFile(object obj)
        {
            var item = obj as FamilyThumbItemViewModel;
            if (item == null) return false;

            var s = (SearchText ?? "").Trim();
            if (s.Length == 0) return true;

            return item.FileName != null && item.FileName.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void OpenDetails(object parameter)
        {
            var item = parameter as FamilyThumbItemViewModel;
            if (item == null) return;

            SelectedFamily = item;
            IsDetailsPaneOpen = true;
            ReadSelectedFamilyParameters(item);
        }

        private void RefreshFamilyFiles()
        {
            FamilyFiles.Clear();
            SelectedFamily = null;
            IsDetailsPaneOpen = false;

            if (_selectedNode == null)
            {
                if (FamilyFilesView != null) FamilyFilesView.Refresh();
                return;
            }

            if (SelectedNode.NodeType == LibraryTreeNodeType.Folder)
            {
                RefreshFromLocalFoler(SelectedNode.FullPath);
                return;
            }

            RefreshFromProjectFamilies(SelectedNode);
            FamilyFilesView?.Refresh();
        }

        private void RefreshFromProjectFamilies(LibraryTreeNodeViewModel selectedNode)
        {
            var families = _projectFamilies.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(selectedNode.CategoryName))
                families = families.Where(f => string.Equals(f.CategoryName, selectedNode.CategoryName, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(selectedNode.FamilyName))
                families = families.Where(f => string.Equals(f.FamilyName, selectedNode.FamilyName, StringComparison.OrdinalIgnoreCase));

            foreach (var family in families)
            {
                var displayName = !string.IsNullOrWhiteSpace(family.FamilyName) ? family.FamilyName : family.Name;
                var vm = new FamilyThumbItemViewModel(family.FullPath, displayName);
                FamilyFiles.Add(vm);
            }
        }

        private void RefreshFromLocalFoler(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            {
                FamilyFilesView?.Refresh();
                return;
            }

            int myVersion = ++_scanVersion; // 版本号，确保异步结果不会覆盖后续的刷新

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

                    FamilyFilesView?.Refresh();
                }));
            });
        }

        private void ReadSelectedFamilyParameters(FamilyThumbItemViewModel item)
        {
            if (item == null) return;

            item.ParametersStatus = "正在读取参数...";
            item.Parameters.Clear();

            RevitFamilyParameterReader.RequestRead(item.FileName, result =>
            {
                _ui.BeginInvoke(new Action(() =>
                {
                    if (!ReferenceEquals(SelectedFamily, item)) return;

                    item.Parameters.Clear();

                    if (result == null)
                    {
                        item.ParametersStatus = "读取参数失败";
                        return;
                    }

                    if (result.Parameters != null)
                    {
                        foreach (var p in result.Parameters)
                        {
                            item.Parameters.Add(new FamilyParameterItemViewModel
                            {
                                Name = p.Name,
                                Value = p.Value,
                                Source = p.Source
                            });
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(result.StatusMessage))
                        item.ParametersStatus = result.StatusMessage;
                    else
                        item.ParametersStatus = item.Parameters.Count > 0 ? $"共 {item.Parameters.Count} 个参数" : "没有参数";
                }));
            });
        }

        private void UpdateStatusText(string message)
        {
            var modePrefix = CurrentLibraryMode == LibraryMode.Project
                ? "当前：项目族库（来自活动文档）"
                : "当前：本地族库";

            StatusText = string.IsNullOrWhiteSpace(message)
                ? modePrefix
                : modePrefix + "|" + message;
        }
        public event PropertyChangedEventHandler PropertyChanged;
        private void OnpropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
    public class ProjectFamilyNodeItem
    {
        public string Name { get; set; }
        public string CategoryName { get; set; }
        public string FamilyName { get; set; }
        public string FullPath { get; set; }
    }
}
