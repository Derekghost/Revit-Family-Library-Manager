using System;
using System.Windows;

namespace RevitFamilyBrowser.ViewModels
{
    public static class FamilyBrowserWindowHost
    {
        private static Window _window;

        public static void Register(Window win)
        {
            _window = win;
        }

        public static void Unregister(Window win)
        {
            if (ReferenceEquals(_window, win)) _window = null;
        }

        public static void BeginPlacement()
        {
            if (_window == null) return;
            
            void action()
            {
                // 放置时：让出视图（不挡鼠标）
                _window.Topmost = false;
                _window.Hide();
            }

            if (_window.Dispatcher.CheckAccess()) action();
            else _window.Dispatcher.Invoke(action);
        }

        public static void EndPlacement()
        {
            if (_window == null) return;

            void action()
            {
                // 放置结束：恢复显示并前置
                _window.Show();
                _window.WindowState = WindowState.Normal;

                // 强制拉到前面（Topmost 小技巧）
                _window.Topmost = true;
                _window.Topmost = false;
                _window.Topmost = true;

                _window.Activate();
            }

            _window.Dispatcher.BeginInvoke((Action)action);
        }
    }
}
