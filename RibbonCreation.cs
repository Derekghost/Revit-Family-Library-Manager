using Autodesk.Revit.UI;
using System;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace RevitFamilyBrowser
{
    public class RibbonCreation : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication app)
        {
            try
            {
                const string tabName = "COIT";
                const string panelName = "族库";

                // 1) Tab 可能已存在：忽略异常
                try { app.CreateRibbonTab(tabName); } catch { }

                // 2) Panel：存在就复用，不存在才创建
                RibbonPanel panel = null;
                foreach (var p in app.GetRibbonPanels(tabName))
                {
                    if (p.Name == panelName) { panel = p; break; }
                }
                if (panel == null)
                    panel = app.CreateRibbonPanel(tabName, panelName);

                // 3) 命令绑定：不要 new，直接 typeof
                var asmPath = Assembly.GetExecutingAssembly().Location;
                var className = typeof(CmdOpenFamilyBrowser).FullName; // 确保 CmdOpenFamilyBrowser 是 public

                var pbd = new PushButtonData("FamilyBrowser","族库", asmPath, className);

                // 4) 图标：存在才加载（不存在也不要抛异常）
                var imgPath = Path.Combine(Path.GetDirectoryName(asmPath) ?? "", "Images", "Box.png");
                if (File.Exists(imgPath))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(imgPath, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    pbd.LargeImage = bmp;
                }

                panel.AddItem(pbd);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                // 启动阶段异常一定要兜住，否则 Revit 可能直接退出/插件失效
                try { TaskDialog.Show("FamilyBrowser OnStartup Error", ex.ToString()); } catch { }
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication app) => Result.Succeeded;
    }
}
