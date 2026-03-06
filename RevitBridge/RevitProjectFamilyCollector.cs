using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitFamilyBrowser.RevitBridge
{
    public static class RevitProjectFamilyCollector
    {
        private static ExternalEvent _exEvent;
        private static Handler _handler;

        public static void Initialize()
        {
            if (_exEvent != null) return;
            _handler = new Handler();
            _exEvent = ExternalEvent.Create(_handler);
        }

        public static void RequestCollect(Action<ProjectFamilySnapshot> callback)
        {
            if (_exEvent == null || callback == null) return;
            _handler.SetRequest(callback);
            _exEvent.Raise();
        }

        private class Handler : IExternalEventHandler
        {
            private Action<ProjectFamilySnapshot> _callback;
            public void SetRequest(Action<ProjectFamilySnapshot> callback)
            {
                _callback = callback;
            }
            public void Execute(UIApplication app)
            {
                var cb = _callback;
                _callback = null;

                if (cb == null) return;

                var doc = app.ActiveUIDocument != null ? app.ActiveUIDocument.Document : null;

                if (doc == null)
                {
                    cb(new ProjectFamilySnapshot
                    {
                        Families = new List<ProjectFamilyItem>(),
                        StatusMessage = "当前没有活动项目"
                    });
                    return;
                }

                try
                {
                    var families = new FilteredElementCollector(doc)
                        .OfClass(typeof(Family))
                        .Cast<Family>()
                        .Select(f => new ProjectFamilyItem
                        {
                            FamilyName = f.Name,
                            CategoryName = f.FamilyCategory != null ? f.FamilyCategory.Name : string.Empty,
                            FamilyId = f.Id != null ? f.Id.IntegerValue : 0
                        })
                        .OrderBy(f => f.CategoryName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(f => f.FamilyName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    cb(new ProjectFamilySnapshot
                    {
                        Families = families,
                        StatusMessage = families.Count == 0 ? "当前项目中未找到可用族" : null
                    });
                }
                catch (Exception ex)
                {
                    cb(new ProjectFamilySnapshot
                    {
                        Families = new List<ProjectFamilyItem>(),
                        StatusMessage = "收集项目族失败：" + ex.Message
                    });
                }
            }

            public string GetName() => "Project Family Collector ExternalEvent";
        }
    }

    public class ProjectFamilySnapshot
    {
        public List<ProjectFamilyItem> Families { get; set; } = new List<ProjectFamilyItem> ();
        public string StatusMessage { get; set; }
    }

    public class ProjectFamilyItem
    {
        public string FamilyName { get; set; }
        public string CategoryName { get; set; }
        public int FamilyId { get; set; }
    }
}
