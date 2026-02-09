using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.IO;
using System.Linq;

namespace RevitFamilyBrowser.RevitBridge
{
    public static class RevitFamilyPlacer
    {
        private static ExternalEvent _exEvent;
        private static Handler _handler;

        // ✅ 只能在 ExternalCommand.Execute 里调用一次
        public static void Initialize()
        {
            if (_exEvent != null) return;
            _handler = new Handler();
            _exEvent = ExternalEvent.Create(_handler);
        }

        /// <summary>
        /// 请求进入“持续放置直到 ESC”的放置模式。
        /// familyName：你显示在UI上的族名（一般用文件名不带后缀）
        /// sourcePath：仅用于提示/调试（可为空）
        /// </summary>
        
        public static void RequestPlace(string familyName, string sourcePath)
        {
            if(_exEvent == null) return;
            if (string.IsNullOrWhiteSpace(familyName)) return;

            _handler.SetRequest(familyName.Trim(), sourcePath);
            _exEvent.Raise();
        }

        private class Handler : IExternalEventHandler
        {
            private string _familyName;
            private string _sourcePath;

            public void SetRequest(string familyName, string sourcePath)
            {
                _familyName = familyName;
                _sourcePath = sourcePath;
            }

                public void Execute(UIApplication app)
            {
                var uidoc = app.ActiveUIDocument;
                var doc = uidoc != null ? uidoc.Document : null;
                if (doc == null) return;

                string name= _familyName;
                string path = _sourcePath;
                
                if(string.IsNullOrWhiteSpace(name)) return;

                // 1) 找同名族（按族名匹配）
                var fam = new FilteredElementCollector(doc)
                    .OfClass(typeof(Autodesk.Revit.DB.Family))
                    .FirstOrDefault(e =>
                    {
                        var f = e as Autodesk.Revit.DB.Family;
                        return f != null && string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase);
                    }) as Autodesk.Revit.DB.Family;

                if (fam == null)
                {
                    // 没载入：提示“请先载入族”
                    TaskDialog.Show("Place Family",
                        "请先载入族后再放置。");
                    return;
                }

                // 2) 取一个可放置类型（FamilySymbol）
                var symbolId = fam.GetFamilySymbolIds().FirstOrDefault();
                if (symbolId == ElementId.InvalidElementId)
                {
                    TaskDialog.Show("Place Family",
                        "该族没有可用类型（FamilySymbol），无法进入放置模式：\n" + name);
                    return;
                }

                var symbol = doc.GetElement(symbolId) as FamilySymbol;
                if (symbol == null)
                {
                    TaskDialog.Show("Place Family",
                        "未能获取族类型（FamilySymbol），无法放置：\n" + name);
                    return;
                }

                // 3) 确保类型激活（激活必须在 Transaction 内）
                try
                {
                    if (!symbol.IsActive)
                    {
                        using (var t = new Transaction(doc, "Activate Symbol: " + name))
                        {
                            t.Start();
                            symbol.Activate();
                            t.Commit();
                        }
                    }
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("Place Family",
                        "激活族类型失败：\n" + name + "\n\n" + ex.Message);
                    return;
                }

                // 4) 进入 Revit 原生“持续放置直到 ESC”
                try
                {
                    // ✅ 进入放置前：让族库窗口退到后面（隐藏）
                    RevitFamilyBrowser.ViewModels.FamilyBrowserWindowHost.BeginPlacement();
                    // 进入原生持续放置（直到 ESC）
                    uidoc.PromptForFamilyInstancePlacement(symbol);
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    // ✅ 用户按 ESC 取消：这是正常路径，静默即可
                }
                catch
                {
                    // ✅ 标注/图框/某些族不支持这种放置：也静默，不弹窗
                }
                finally
                {
                    // ✅ 放置结束（你按两次ESC退出命令后这里才会执行）：恢复前置
                    RevitFamilyBrowser.ViewModels.FamilyBrowserWindowHost.EndPlacement();
                }
            }

            public string GetName() => "Family Placer ExternalEvent";
        }
    }
}
