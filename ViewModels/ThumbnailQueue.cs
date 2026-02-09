using RevitFamilyBrowser.Infrastructure;
using RevitFamilyBrowser.Views;
using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace RevitFamilyBrowser.ViewModels
{
    public static class ThumbnailQueue
    {
        private class WorkItem
        {
            public string Path;
            public int Size;
            public Action<ImageSource> Done;
        }

        private static readonly BlockingCollection<WorkItem> _queue = new BlockingCollection<WorkItem>();
        private static Thread _worker;
        private static bool _started;

        // ✅ 由窗口初始化传进来
        private static Dispatcher _uiDispatcher;

        public static void Initialize(Dispatcher uiDispatcher)
        {
            _uiDispatcher = uiDispatcher;
        }

        public static void EnsureStarted()
        {
            if (_started) return;
            _started = true;

            _worker = new Thread(WorkerLoop);
            _worker.IsBackground = true;
            _worker.SetApartmentState(ApartmentState.STA);
            _worker.Start();
        }

        public static void Enqueue(string path, int size, Action<ImageSource> done)
        {
            EnsureStarted();
            _queue.Add(new WorkItem { Path = path, Size = size, Done = done });
        }

        [DllImport("ole32.dll")]
        private static extern int CoInitializeEx(IntPtr pvReserved, int dwCoInit);

        [DllImport("ole32.dll")]
        private static extern void CoUninitialize();

        private const int COINIT_APARTMENTTHREADED = 0x2;
        private const int RPC_E_CHANGED_MODE = unchecked((int)0x80010106);
        private static void WorkerLoop()
        {
            int hr = CoInitializeEx(IntPtr.Zero, COINIT_APARTMENTTHREADED);
            bool inited = (hr == 0);

            // 如果线程已被初始化为其他模式，仍然继续跑（不要直接崩）
            // RPC_E_CHANGED_MODE 表示 COM 已经初始化过了（模式不同），此时别 CoUninitialize
            try
            {
                foreach (var item in _queue.GetConsumingEnumerable())
                {
                    ImageSource img = null;
                    try
                    {
                        img = ShellThumbnailProvider.GetThumbnail(item.Path, item.Size, item.Size);
                    }
                    catch
                    {
                        img = null;
                    }

                    try
                    {
                        if (_uiDispatcher != null)
                        {
                            _uiDispatcher.BeginInvoke(new Action(() =>
                            {
                                item.Done?.Invoke(img);
                            }));
                        }
                    }
                    catch { }
                }
            }
            finally
            {
                if (inited) CoUninitialize();
            }
        }
    }
}
