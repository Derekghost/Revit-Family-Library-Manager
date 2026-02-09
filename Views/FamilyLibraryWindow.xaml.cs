using RevitFamilyBrowser.ViewModels;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

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
            DataContext = new FamilyLibraryViewModel();
            _revitHwnd = revitHwnd;
        }

        protected override void OnClosed(EventArgs e)
        {
            if(_topmostTimer != null) _topmostTimer.Stop();
            base.OnClosed(e);
        }

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);
    }
}
