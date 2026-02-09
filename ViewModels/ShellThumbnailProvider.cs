using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace RevitFamilyBrowser.Infrastructure
{
    public static class ShellThumbnailProvider
    {
        public static BitmapSource GetThumbnail(string filePath, int width, int height)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return null;

            IShellItemImageFactory factory = null;
            IntPtr hBitmap = IntPtr.Zero;

            try
            {
                // 直接拿 IShellItemImageFactory，比先拿 IShellItem 再 cast 更稳
                Guid iidFactory = new Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B");
                int hr = SHCreateItemFromParsingName(filePath, IntPtr.Zero, ref iidFactory, out factory);
                if (hr != 0 || factory == null) return null;

                SIZE size;
                size.cx = width;
                size.cy = height;

                // flags：THUMBNAILONLY + RESIZETOFIT + BIGGERSIZEOK 常见成功率更高
                hr = factory.GetImage(size,
                    SIIGBF.SIIGBF_THUMBNAILONLY | SIIGBF.SIIGBF_RESIZETOFIT | SIIGBF.SIIGBF_BIGGERSIZEOK,
                    out hBitmap);

                if (hr != 0 || hBitmap == IntPtr.Zero) return null;

                var bmp = Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

                bmp.Freeze(); // 允许跨线程
                return bmp;
            }
            finally
            {
                if (hBitmap != IntPtr.Zero)
                    DeleteObject(hBitmap);

                if (factory != null)
                    Marshal.ReleaseComObject(factory);
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE
        {
            public int cx;
            public int cy;
        }

        [Flags]
        private enum SIIGBF
        {
            SIIGBF_RESIZETOFIT = 0x00,
            SIIGBF_BIGGERSIZEOK = 0x01,
            SIIGBF_MEMORYONLY = 0x02,
            SIIGBF_ICONONLY = 0x04,
            SIIGBF_THUMBNAILONLY = 0x08,
            SIIGBF_INCACHEONLY = 0x10
        }

        [ComImport]
        [Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            [PreserveSig]
            int GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
        }
    }
}
