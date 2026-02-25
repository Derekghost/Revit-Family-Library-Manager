using RevitFamilyBrowser.ViewModels;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Media.Animation;

namespace RevitFamilyBrowser.Views
{
    public partial class FamilyLibraryWindow : Window
    {
        private DispatcherTimer _topmostTimer;
        private IntPtr _revitHwnd;
        public FamilyLibraryWindow(IntPtr revitHwnd)
        {
            InitializeComponent();
            _revitHwnd = revitHwnd;

            Loaded += (s, e) => RevitFamilyBrowser.ViewModels.FamilyBrowserWindowHost.Register(this);
            Closed += (s, e) => RevitFamilyBrowser.ViewModels.FamilyBrowserWindowHost.Unregister(this);

            // 只在 Revit 前台时置顶
            _topmostTimer = new DispatcherTimer();
            _topmostTimer.Interval = TimeSpan.FromMilliseconds(300);
            _topmostTimer.Tick += (s, e) =>
            {
                var fg = GetForegroundWindow();
                bool revitActive = (fg == _revitHwnd) || IsChild(_revitHwnd, fg);

                // 只有变化时才改，避免闪烁
                if (this.Topmost != revitActive)
                    this.Topmost = revitActive;
            };
            _topmostTimer.Start();

            // ✅ 让缩略图队列知道“UI线程是谁”
            ThumbnailQueue.Initialize(this.Dispatcher); // ✅ 关键
            //DataContext = new FamilyLibraryViewModel(@"D:\User\Family");//
            DataContextChanged += OnDataContextChanged;
            DataContext = new FamilyLibraryViewModel();
            OnDataContextChanged(this, new DependencyPropertyChangedEventArgs(DataContextProperty, null, DataContext));
            _revitHwnd = revitHwnd;
        }

        private FamilyLibraryViewModel _viewModel;

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if(_viewModel != null)
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

            _viewModel = DataContext as FamilyLibraryViewModel;
            if(_viewModel != null)
            {
                _viewModel.PropertyChanged += OnViewModelPropertyChanged;
                UpdateDetailsPanelState(false);
            }
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FamilyLibraryViewModel.IsDetailsPaneOpen))
                UpdateDetailsPanelState(true);
        }

        private void UpdateDetailsPanelState(bool animate)
        {
            var target = (_viewModel != null && _viewModel.IsDetailsPaneOpen) ? 0 : 340;
            if (!animate)
            {
                DetailsPanelTransform.X = target;
                return;
            }

            var ani = new DoubleAnimation
            {
                To = target,
                Duration = TimeSpan.FromMilliseconds(220),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            DetailsPanelTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, ani);
        }

        protected override void OnClosed(EventArgs e)
        {
            if(_topmostTimer != null) _topmostTimer.Stop();
            if(_viewModel != null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            base.OnClosed(e);
        }

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);
    }
}
