using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Windows;
using System.Windows.Interop;

namespace RevitFamilyBrowser
{
    [Transaction(TransactionMode.Manual)]
    public class CmdOpenFamilyBrowser : IExternalCommand
    {
        private static Views.FamilyLibraryWindow _win;

        public Result Execute(ExternalCommandData c, ref string m, ElementSet e)
        {
            try
            {
                // 已经打开就激活，不重复创建
                if (_win != null)
                {
                    if (_win.IsVisible)
                    {
                        _win.Activate();
                        return Result.Succeeded;
                    }
                    _win = null;
                }
                IntPtr revitHwnd = c.Application.MainWindowHandle;
                _win = new Views.FamilyLibraryWindow(revitHwnd);

                // 绑定到 Revit 主窗口，避免窗口飘走/被遮挡
                IntPtr revitHandle = c.Application.MainWindowHandle;
                new WindowInteropHelper(_win).Owner = revitHandle;

                // 关闭时清掉静态引用，方便再次打开
                _win.Closed += (s, ea) => { _win = null; };

                // modeless：不阻塞 Revit
                RevitFamilyBrowser.RevitBridge.RevitFamilyLoader.Initialize();
                RevitFamilyBrowser.RevitBridge.RevitFamilyPlacer.Initialize();
                RevitFamilyBrowser.RevitBridge.RevitFamilyParameterReader.Initialize();
                new WindowInteropHelper(_win).Owner = revitHwnd;
                _win.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                m = ex.ToString();
                return Result.Failed;
            }
        }
    }
}

