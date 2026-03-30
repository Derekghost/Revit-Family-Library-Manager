using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.Windows.Data;
using System.Diagnostics;
using RevitFamilyBrowser.ViewModels;
using RevitFamilyBrowser.Properties;
using RevitFamilyBrowser.RevitBridge;
using System.Data;
using System.Windows.Media.Imaging;
using RevitFamilyBrowser.Data;
using System.Windows;

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
        private readonly object _scanLock = new object();
        private readonly List<DispatcherOperation> _pendingScanUiOperations = new List<DispatcherOperation>();
        private CancellationTokenSource _localScanCancellation;
        private int _localScanRunId;
        private readonly SQLiteLibraryRepository _libraryRepository;

        private bool _isLocalScanInProgress;
        public bool IsLocalScanInProgress
        {
            get => _isLocalScanInProgress;
            private set
            {
                if (_isLocalScanInProgress == value) return;
                _isLocalScanInProgress = value;
                OnpropertyChanged();
            }
        }

        private string _localScanProgressText = "等待扫描";
        public string LocalScanProgressText
        {
            get => _localScanProgressText;
            private set
            {
                if (_localScanProgressText == value) return;
                _localScanProgressText = value;
                OnpropertyChanged();
            }
        }
        public bool ShowLocalScanStatus => CurrentLibraryMode == LibraryMode.Local;

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
        public RelayCommand DeleteFolderDbRecordsCommand { get; }

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
                OnpropertyChanged(nameof(ToggleLibraryModeText));
                OnpropertyChanged(nameof(ShowLocalScanStatus));
                BrowseRootCommand?.RaiseCanExecuteChanged();
                OpenLastRootCommand?.RaiseCanExecuteChanged();
                DeleteFolderDbRecordsCommand?.RaiseCanExecuteChanged();
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
                CancelLocalScan("节点切换，已取消上一次扫描");
                _selectedNode = value;
                OnpropertyChanged();
                DeleteFolderDbRecordsCommand?.RaiseCanExecuteChanged();
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
            DeleteFolderDbRecordsCommand = new RelayCommand(_ => DeleteSelectedFolderRecordsFromDatabase(), _ => CanDeleteSelectedFolderRecordsFromDatabase());
            ToggleLibraryModeCommand = new RelayCommand(_ => ToggleLibraryMode());
            OpenDetailsCommand = new RelayCommand(OpenDetails);
            CloseDetailsCommand = new RelayCommand(_ => IsDetailsPaneOpen = false);

            // ✅ 固定路径（先注释保留，后续想切回直接取消注释即可）
            // SetRootPath(@"D:\User\Family");

            // ✅ 当前：不固定路径（rootPath 传 null 就等待用户选择）
            var initialPath = string.IsNullOrWhiteSpace(rootPath) ? null : rootPath;
            SetRootPath(string.IsNullOrWhiteSpace(initialPath) ? null : initialPath);

            try
            {
                var dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RevitFamilyBrowser", "library.db");
                _libraryRepository = new SQLiteLibraryRepository(dbPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SQLite] Init failed: {ex.Message}");
            }

            DeleteFolderDbRecordsCommand.RaiseCanExecuteChanged();
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
            CancelLocalScan("切换到本地族库，已取消上一次扫描");
            CurrentLibraryMode = LibraryMode.Local;

            var pathToUse = RootPath;
            if (string.IsNullOrWhiteSpace(pathToUse) || !Directory.Exists(pathToUse))
                pathToUse = GetLastRootPath();

            SetRootPath(string.IsNullOrWhiteSpace(pathToUse) ? null : pathToUse);
            OpenLastRootCommand.RaiseCanExecuteChanged();
        }
        private void SwitchToProjectLibrary()
        {
            CancelLocalScan("切换到项目族库，已取消上一次扫描");
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
                    FullPath = null,
                    TypeName = family.TypeName,
                    ElementTypeId = family.ElementTypeId,
                    IsLoadableFamily = family.IsLoadableFamily
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

                    var familyNodes = group
                        .GroupBy(f => string.IsNullOrWhiteSpace(f.FamilyName) ? "未命名族" : f.FamilyName)
                        .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(familyGroup => new LibraryTreeNodeViewModel(
                            familyGroup.Key,
                            LibraryTreeNodeType.Family,
                            categoryNode,
                            familyName : familyGroup.Key,
                            categoryName:group.Key,
                            onSelected: node => SelectedNode = node));

                    categoryNode.ReplaceChildren(familyNodes);
                    return categoryNode;
                });

            projectRoot.ReplaceChildren(categoryNodes);
            RootNodes.Add(projectRoot);
            projectRoot.IsExpanded = true;
            SelectedNode = (LibraryTreeNodeViewModel)(projectRoot.Children.FirstOrDefault() ?? projectRoot);

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
            CancelLocalScan("目录已变更，已取消上一次扫描");
            RootPath = rootPath;

            // 重置界面数据
            RootNodes.Clear();
            FamilyFiles.Clear();
            _selectedNode = null;
            OnpropertyChanged(nameof(SelectedNode));

            if (string.IsNullOrWhiteSpace(rootPath))
            {
                ResetLocalScanProgress("等待选择目录");
                UpdateStatusText("请选择族库目录");
                FamilyFilesView?.Refresh();
                return;
            }

            if (!Directory.Exists(rootPath))
            {
                ResetLocalScanProgress("目录无效");
                UpdateStatusText("目录不存在或无权限访问： " + rootPath);
                // 仍然显示一个根节点，让用户知道你在看哪个路径
                var invalidRoot = new FolderNodeViewModel(rootPath, null, true, this);
                RootNodes.Add(CreateFolderNode(rootPath, null, true));
                return;
            }

            UpdateStatusText ("Root: " + rootPath);
            ResetLocalScanProgress("等待扫描");
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
            CancelLocalScan("视图已切换，已取消上一次扫描");
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
            var filtered = _projectFamilies.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(selectedNode.CategoryName))
                filtered = filtered.Where(f => string.Equals(f.CategoryName, selectedNode.CategoryName, StringComparison.OrdinalIgnoreCase));

            if (selectedNode.NodeType == LibraryTreeNodeType.Family && !string.IsNullOrWhiteSpace(selectedNode.FamilyName))
            {
                filtered = filtered.Where(f => string.Equals(f.FamilyName, selectedNode.FamilyName, StringComparison.OrdinalIgnoreCase));

                var typeItems = filtered
                    .Where(f => !string.IsNullOrWhiteSpace(f.FamilyName))
                    .GroupBy(f => (f.TypeName ?? string.Empty) + "|" + (f.FamilyName ?? string.Empty), StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .OrderBy(f => f.TypeName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (typeItems.Count == 0)
                {
                    AddProjectFamilyCard(new ProjectFamilyNodeItem
                    {
                        Name = selectedNode.FamilyName,
                        FamilyName = selectedNode.FamilyName,
                        CategoryName = selectedNode.CategoryName,
                        TypeName = null,
                        FullPath = null,
                        ElementTypeId = 0,
                        IsLoadableFamily = true,
                    }, selectedNode);
                    return;
                }

                foreach (var item in typeItems)
                    AddProjectFamilyCard(item,selectedNode);

                return;
            }

            var familyItems = filtered
                .GroupBy(f => (f.FamilyName ?? string.Empty) + "|" + (f.FamilyName ?? string.Empty), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(x => x.IsLoadableFamily).First())
                .OrderBy(f => f.TypeName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var family in familyItems)
                AddProjectFamilyCard(family, selectedNode);
        }

        private void AddProjectFamilyCard(ProjectFamilyNodeItem family, LibraryTreeNodeViewModel selectedNode)
        {
            var displayName = !string.IsNullOrWhiteSpace(family.TypeName)
                ? family.TypeName
                : (!string.IsNullOrWhiteSpace(family.FamilyName) ? family.FamilyName : family.Name);

            var vm = new FamilyThumbItemViewModel(family.FamilyName, displayName)
            {
                ShowLoadPlaceActions = false,
                LoadCommand = null,
                PlaceCommand = null,
                CanReadProjectParameters = family.IsLoadableFamily && string.IsNullOrWhiteSpace(family.FamilyName)
            };

            if (!vm.CanReadProjectParameters)
                vm.ParametersStatus = "系统类型/类型节点，暂不支持族参数读取";

            FamilyFiles.Add(vm);

            if (family.ElementTypeId > 0)
            {
                var expectedSelectedNode = selectedNode;
                RevitProjectFamilyThumbnailProvider.RequestThumbnail(family.ElementTypeId, family.FamilyName, 256, 256, result =>
                {
                    _ui.BeginInvoke(new Action(() =>
                    {
                        if (CurrentLibraryMode != LibraryMode.Project) return;
                        if (!ReferenceEquals(SelectedNode, expectedSelectedNode)) return;
                        if (result == null || result.ImageBytes == null || result.ImageBytes.Length == 0) return;

                        var image = TryCreateBitmap(result.ImageBytes);
                        if (image != null)
                            vm.Thumbnail = image;
                    }));
                });
            }
        }

        private static BitmapImage TryCreateBitmap(byte[] imageBytes)
        {
            if (imageBytes == null || imageBytes.Length == 0) return null;

            try
            {
                using (var ms = new MemoryStream(imageBytes))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.StreamSource = ms;
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    return bitmap;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TryCreateBitmap] Failed to create thumbnail bitmap. Bytes={imageBytes.Length}, Error={ex.Message}");
                return null;
            }
        }

        private void RefreshFromLocalFoler(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            {
                FamilyFilesView?.Refresh();
                return;
            }

            var loadedFromIndex = LoadFolderCardsFromDatabase(dir);
            if (loadedFromIndex > 0)
            {
                FamilyFilesView?.Refresh();
                UpdateStatusText($"索引命中：已加载 {loadedFromIndex} 个文件，正在执行增量扫描... | 当前目录：{dir}");
            }
            int myVersion = ++_scanVersion; // 版本号，确保异步结果不会覆盖后续的刷新
            CancellationToken token;
            int runId;

            lock (_scanLock)
            {
                _localScanCancellation = new CancellationTokenSource();
                token = _localScanCancellation.Token;
                runId = ++_localScanRunId;
            }

            UpdateStatusText($"正在扫描：已发现 0 个文件 | 当前目录：{dir}");
            IsLocalScanInProgress = true;
            LocalScanProgressText = "正在扫描：一发现0个文件";

            const int batchSize = 100;
            var pendingBatch = new List<string>(batchSize);
            var discoveredCount = 0;
            var currentDir = dir;

            Task.Run(() =>
            {
                try
                {
                    foreach (var file in EnumerateRfaFiles(dir, token, d => currentDir = d))
                    {
                        token.ThrowIfCancellationRequested();
                        pendingBatch.Add(file);
                        discoveredCount++;

                        if (pendingBatch.Count >= batchSize)
                        {
                            var batch = pendingBatch.ToArray();
                            pendingBatch.Clear();
                            ScheduleScanUiUpdate(myVersion, runId, token, batch, discoveredCount, currentDir, false);
                        }
                    }

                    if (pendingBatch.Count > 0)
                    {
                        var batch = pendingBatch.ToArray();
                        pendingBatch.Clear();
                        ScheduleScanUiUpdate(myVersion, runId, token, batch, discoveredCount, currentDir, true);
                    }
                    else
                    {
                        ScheduleScanUiStatus(myVersion, runId, token, $"扫描完成：共发现 {discoveredCount} 个文件 | 当前目录：{currentDir}");
                    }
                }
                catch (OperationCanceledException)
                {
                    ScheduleScanUiStatus(myVersion, runId, token, $"扫描已取消：已发现 {discoveredCount} 个文件 | 当前目录：{currentDir} | 已取消");
                }
                catch
                {
                    ScheduleScanUiStatus(myVersion, runId, token, $"扫描中断：已发现 {discoveredCount} 个文件 | 当前目录：{currentDir}");
                }
            }, token);
        }

        private int LoadFolderCardsFromDatabase(string dir)
        {
            if (_libraryRepository == null || string.IsNullOrWhiteSpace(dir)) return 0;

            try
            {
                var records = _libraryRepository.SearchByFolder(dir, string.Empty);
                if (records == null || records.Count == 0) return 0;

                foreach (var record in records)
                {
                    if (record == null || string.IsNullOrWhiteSpace(record.FilePath)) continue;
                    if (!File.Exists(record.FilePath)) continue;
                    AddLocalFamilyCard(record.FilePath, skipDatabaseUpsert: true);
                }

                return FamilyFiles.Count;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SQLite] Query by folder failed. Folder={dir}, Error={ex.Message}");
                return 0;
            }
        }
        private IEnumerable<string> EnumerateRfaFiles(string rootDir, CancellationToken token, Action<string> onDirChanged)
        {
            var stack = new Stack<string>();
            stack.Push(rootDir);

            while (stack.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                var current = stack.Pop();
                onDirChanged?.Invoke(current);

                IEnumerable<string> files;

                try
                {
                    files = Directory.EnumerateFiles(current, "*.rfa", SearchOption.TopDirectoryOnly);
                }
                catch
                {
                    files = Array.Empty<string>();
                }

                foreach (var file in files)
                {
                    token.ThrowIfCancellationRequested();
                    yield return file;
                }

                IEnumerable<string> subDirs;
                try
                {
                    subDirs = Directory.EnumerateDirectories(current);
                }
                catch
                {
                    subDirs = Array.Empty<string>();
                }

                foreach (var subDir in subDirs)
                    stack.Push(subDir);
            }
        }

        private void ScheduleScanUiUpdate(int scanVersion, int runId, CancellationToken token, string[] files, int discoveredCount,string currentDir, bool isFinalBatch)
        {
            var op = _ui.BeginInvoke(new Action(() =>
            {
                if (!IsScanStillValid(scanVersion, runId, token)) return;

                foreach (var file in files)
                    AddLocalFamilyCard(file);

                FamilyFilesView?.Refresh();
                var tail = isFinalBatch ? " | 扫描完成" : "";
                UpdateStatusText($"正在扫描：已发现 {discoveredCount} 个文件 | 当前目录：{currentDir}{tail}");
                LocalScanProgressText = isFinalBatch
                    ? $"扫描完成：共发现 {discoveredCount} 个文件"
                    : $"正在扫描：已发现 {discoveredCount} 个文件";
                IsLocalScanInProgress = !isFinalBatch;
            }));

            TrackPendingUiOperation(op);
        }

        private void ScheduleScanUiStatus(int scanVersion, int runId, CancellationToken token, string message)
        {
            var op = _ui.BeginInvoke(new Action(() =>
            {
                if (!IsScanStillValid(scanVersion, runId, token)) return;
                FamilyFilesView?.Refresh();
                UpdateStatusText(message);

                var finished = message.Contains("完成") || message.Contains("取消") || message.Contains("中断");
                LocalScanProgressText = message;
                if (finished)
                    IsLocalScanInProgress = false;
            }));

            TrackPendingUiOperation(op);
        }

        private void TrackPendingUiOperation(DispatcherOperation operation)
        {
            if (operation == null) return;

            lock (_scanLock)
            {
                _pendingScanUiOperations.Add(operation);
            }

            operation.Completed += (_, __) =>
            {
                lock (_scanLock)
                {
                    _pendingScanUiOperations.Remove(operation);
                }
            };
            operation.Aborted += (_, __) =>
            {
                lock (_scanLock)
                {
                    _pendingScanUiOperations.Remove(operation);
                }
            };
        }

        private bool IsScanStillValid(int scanVersion, int runId, CancellationToken token)
        {
            if(token.IsCancellationRequested) return false;
            if(scanVersion != _scanVersion) return false;

            lock (_scanLock)
            {
                return runId == _localScanRunId;
            }
        }

        private void CancelLocalScan(string reason)
        {
            CancellationTokenSource cts = null;
            DispatcherOperation[] pendingOperations;

            lock (_scanLock)
            {
                cts = _localScanCancellation;
                _localScanCancellation = null;

                pendingOperations = _pendingScanUiOperations.ToArray();
                _pendingScanUiOperations.Clear();
            }

            if (cts != null)
            {
                try
                {
                    if (!cts.IsCancellationRequested)
                        cts.Cancel();
                }
                catch
                {
                }
                finally
                {
                    cts.Dispose();
                }
            }

            foreach (var op in pendingOperations)
            {
                try
                {
                    if (op != null && op.Status == DispatcherOperationStatus.Pending)
                        op.Abort();
                }
                catch
                {
                }
            }

            if (!string.IsNullOrWhiteSpace(reason))
                UpdateStatusText(reason);

            IsLocalScanInProgress = false;
            LocalScanProgressText = reason ?? "扫描已停止";
        }

        private void ResetLocalScanProgress(string text)
        {
            IsLocalScanInProgress = false;
            LocalScanProgressText = text;
        }

        private bool CanDeleteSelectedFolderRecordsFromDatabase()
        {
            if (CurrentLibraryMode != LibraryMode.Local) return false;
            if (_libraryRepository == null) return false;
            if (SelectedNode == null || SelectedNode.NodeType != LibraryTreeNodeType.Folder) return false;
            if (string.IsNullOrWhiteSpace(SelectedNode.FullPath)) return false;
            return true;
        }

        private void DeleteSelectedFolderRecordsFromDatabase()
        {
            if (!CanDeleteSelectedFolderRecordsFromDatabase()) return;

            var selectedFolder = SelectedNode.FullPath;
            var confirm = MessageBox.Show(
                $"确定删除数据库中该路径及其子目录的索引记录吗？\n{selectedFolder}",
                "清理数据库记录",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            UpdateStatusText("正在清理数据库记录...");
            Task.Run(() =>
            {
                int deletedRows = 0;
                string error = null;
                try
                {
                    deletedRows = _libraryRepository.DeleteByFolderPath(selectedFolder, includeSubfolders: true);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    Debug.WriteLine($"[SQLite] Delete failed. Folder={selectedFolder}, Error={ex.Message}");
                }

                _ui.BeginInvoke(new Action(() =>
                {
                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        UpdateStatusText($"数据库清理失败：{error}");
                        MessageBox.Show($"数据库清理失败：{error}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    UpdateStatusText($"数据库清理完成：删除 {deletedRows} 条记录 | 路径：{selectedFolder}");
                    MessageBox.Show($"已删除 {deletedRows} 条数据库记录。", "清理完成", MessageBoxButton.OK, MessageBoxImage.Information);
                }));
            });
        }

        private void AddLocalFamilyCard(string fullPath,bool skipDatabaseUpsert = false)
        {
            var name = Path.GetFileNameWithoutExtension(fullPath);
            var vm = new FamilyThumbItemViewModel(fullPath, name)
            {
                ShowLoadPlaceActions = true
            };

            vm.LoadCommand = new RelayCommand(_ =>
            {
                RevitBridge.RevitFamilyLoader.RequestLoad(vm.FullPath);
            });

            vm.PlaceCommand = new RelayCommand(_ =>
            {
                RevitBridge.RevitFamilyPlacer.RequestPlace(vm.FileName, vm.FullPath);
            });

            FamilyFiles.Add(vm);

            if (!skipDatabaseUpsert)
                TryUpsertLocalFamily(fullPath, name);

            ThumbnailQueue.Enqueue(fullPath, 256, img =>
            {
                if (img != null) vm.Thumbnail = img;
            });
        }

        private void TryUpsertLocalFamily(string fullPath, string familyName)
        {
            if (_libraryRepository == null || string.IsNullOrWhiteSpace(fullPath)) return;

            Task.Run(() =>
            {
                try
                {
                    var fileInfo = new FileInfo(fullPath);
                    var folder = fileInfo.DirectoryName ?? string.Empty;
                    var lastWriteUtc = fileInfo.Exists ? fileInfo.LastWriteTimeUtc : DateTime.UtcNow;
                    var fileSize = fileInfo.Exists ? fileInfo.Length : 0L;

                    _libraryRepository.UpsertFamily(fullPath, familyName, folder, lastWriteUtc, fileSize);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SQLite] Upsert failed. File={fullPath}, Error={ex.Message}");
                }
            });
        }

        private void ReadSelectedFamilyParameters(FamilyThumbItemViewModel item)
        {
            if (item == null) return;

            if(CurrentLibraryMode == LibraryMode.Project && !item.CanReadProjectParameters)
            {
                item.Parameters.Clear();
                item.ParametersStatus = "系统类型，暂不支持族参数读取";
                return;
            }

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
        public int ElementTypeId { get; set; }
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public string FullPath { get; set; }
        public bool IsLoadableFamily { get; set; }
    }
}
