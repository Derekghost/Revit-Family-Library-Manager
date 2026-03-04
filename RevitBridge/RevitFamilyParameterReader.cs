using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitFamilyBrowser.RevitBridge
{
    public static class RevitFamilyParameterReader
    {
        private static ExternalEvent _exEvent;
        private static Handler _handler;

        public static void Initialize()
        {
            if (_exEvent != null) return;
            _handler = new Handler();
            _exEvent = ExternalEvent.Create(_handler);
        }

        public static void RequestRead(string familyName, Action<FamilyParameterReadResult> callback)
        {
            if (_exEvent == null || string.IsNullOrWhiteSpace(familyName) || callback == null) return;
            _handler.SetRequest(familyName.Trim(), callback);
            _exEvent.Raise();
        }

        private class Handler : IExternalEventHandler
        {
            private string _familyName;
            private Action<FamilyParameterReadResult> _callback;

            public void SetRequest(string familyName,Action<FamilyParameterReadResult> callback)
            {
                _familyName = familyName;
                _callback = callback;
            }
            public void Execute(UIApplication app)
            {
                var cb = _callback;
                var name = _familyName;

                _callback = null;
                _familyName = null;

                if (cb == null || string.IsNullOrWhiteSpace(name)) return;

                var doc = app.ActiveUIDocument != null ? app.ActiveUIDocument.Document: null;
                if(doc == null)
                {
                    cb(new FamilyParameterReadResult { IsLoaded = false, StatusMessage = "当前没有活动文档，无法读取参数" });
                    return;
                }

                try
                {
                    var fam = new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>().FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

                    if (fam == null)
                    {
                        cb(new FamilyParameterReadResult { IsLoaded = false, StatusMessage = "该族尚未载入当前项目，请先载入后再查看参数" });
                        return;
                    }

                    var list = new List<FamilyParameterInfo>();

                    var familyDoc =doc.EditFamily(fam);
                    try
                    {
                        var familyManager = familyDoc.FamilyManager;
                        if (familyManager != null)
                        {
                            if(familyManager.CurrentType == null)
                            {
                                var firstType = familyManager.Types.Cast<FamilyType>().FirstOrDefault();
                                if(firstType != null)
                                    familyManager.CurrentType = firstType;
                            }
                        }

                        foreach (FamilyParameter fp in familyManager.Parameters)
                        {
                            if(fp == null || fp.Definition == null) continue;

                            list.Add(new FamilyParameterInfo
                            {
                                Name = fp.Definition.Name,
                                Value = FormatFamilyParameterValue(familyManager, fp, familyDoc),
                                Source = fp.IsShared ? "共享参数" : (fp.IsInstance ? "实例参数" : "类型参数")
                            });
                        }
                    }
                    finally
                    {
                        familyDoc?.Close(false);
                    }

                    var distinct = list
                        .GroupBy(x => x.Name + "|" + x.Value)
                        .Select(g => g.First())
                        .OrderBy(x => x.Name)
                        .ToList();

                    cb(new FamilyParameterReadResult
                    {
                        IsLoaded = true,
                        Parameters = distinct,
                        StatusMessage = distinct.Count == 0 ? "族已载入，但未读取到可展示参数" : null
                    });
                }
                catch (Exception ex)
                {
                    cb(new FamilyParameterReadResult { IsLoaded = false, StatusMessage = "读取参数时发生错误：" + ex.Message });
                }
            }

            public string GetName() => "Family Parameter Reader ExternalEvent";

            private static string FormatFamilyParameterValue(FamilyManager familyManager, FamilyParameter familyParameter, Document familyDoc)
            {
                if(familyManager?.CurrentType == null || familyParameter == null) return "";

                var familyType = familyManager.CurrentType;
                switch (familyParameter.StorageType)
                {
                    case StorageType.String:
                        return familyType.AsString(familyParameter) ?? "";
                    case StorageType.Integer:
                        return familyType.AsInteger(familyParameter).ToString();
                    case StorageType.Double:
                        return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.###}", familyType.AsDouble(familyParameter));
                    case StorageType.ElementId:
                        var id = familyType.AsElementId(familyParameter);
                        if(id == null || id == ElementId.InvalidElementId) return "<None>";
                        var e = familyDoc.GetElement(id);
                        return e != null ? e.Name : id.IntegerValue.ToString();
                    default:
                        return "";
                }
            }
        }
    }

    public class FamilyParameterReadResult
    {
        public bool IsLoaded { get; set; } 
        public string StatusMessage { get; set; }
        public List<FamilyParameterInfo> Parameters { get; set; } = new List<FamilyParameterInfo>();
    }

    public class FamilyParameterInfo
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public string Source { get; set; }
    }
}
