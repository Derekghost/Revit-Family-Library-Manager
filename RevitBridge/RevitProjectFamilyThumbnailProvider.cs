using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace RevitFamilyBrowser.RevitBridge
{
    public static class RevitProjectFamilyThumbnailProvider
    {
        private static ExternalEvent _externalEvent;
        private static Handler _handler;

        public static void Initialize()
        {
            if (_externalEvent != null) return;
            _handler = new Handler();
            _externalEvent = ExternalEvent.Create(_handler);
        }

        public static void RequestThumbnail(string familyName, int width, int height, Action<ProjectFamilyThumbnailResult> callback)
        {
            if(_externalEvent == null || string.IsNullOrWhiteSpace(familyName) || callback == null) return;

            var safeWidth = width > 0 ? width : 256;
            var safeHeight = height > 0 ? height : 256;

            _handler.SetRequest(familyName.Trim(), safeWidth, safeHeight, callback);
            _externalEvent.Raise();
        }

        private class Handler : IExternalEventHandler
        {
            private readonly Queue<ThumbnailRequest> _requests = new Queue<ThumbnailRequest>();

            private class ThumbnailRequest
            {
                public string FamilyName { get; set; }
                public int Width { get; set; }
                public int Height { get; set; }
                public Action<ProjectFamilyThumbnailResult> Callback { get; set; }
            }

            public void SetRequest(string familyName, int width, int height, Action<ProjectFamilyThumbnailResult> callback)
            {
                lock (_requests)
                {
                    _requests.Enqueue(new ThumbnailRequest
                    {
                        FamilyName = familyName,
                        Width = width,
                        Height = height,
                        Callback = callback
                    });
                }
            }

            public void Execute(UIApplication app)
            {
                ThumbnailRequest request = null;
                lock (_requests)
                {
                    if(_requests.Count > 0) 
                        request = _requests.Dequeue();
                }

                if (request == null || request.Callback == null || string.IsNullOrWhiteSpace(request.FamilyName)) return;

                var cb = request.Callback;
                var familyName = request.FamilyName;
                var width = request.Width;
                var height = request.Height;
                try
                {
                    var doc = app.ActiveUIDocument != null ? app.ActiveUIDocument.Document : null;
                    if (doc == null)
                    {
                        cb(new ProjectFamilyThumbnailResult
                        {
                            FamilyName = familyName,
                            StatusMessage = "当前没有活动项目"
                        });
                        return;
                    }

                    var family = new FilteredElementCollector(doc)
                        .OfClass(typeof(Family))
                        .Cast<Family>()
                        .FirstOrDefault(f => string.Equals(f.Name, familyName, StringComparison.OrdinalIgnoreCase));

                    if(family == null)
                    {
                        cb(new ProjectFamilyThumbnailResult
                        {
                            FamilyName = familyName,
                            StatusMessage = "未找到项目族"
                        });
                        return;
                    }

                    var symbolId = family.GetFamilySymbolIds().FirstOrDefault();
                    if (symbolId == null || symbolId == ElementId.InvalidElementId)
                    {
                        cb(new ProjectFamilyThumbnailResult
                        {
                            FamilyName = familyName,
                            StatusMessage = "该族没有可预览类型"
                        });
                        return;
                    }

                    var type = doc.GetElement(symbolId) as ElementType;
                    if (type == null)
                    {
                        cb(new ProjectFamilyThumbnailResult
                        {
                            FamilyName = familyName,
                            StatusMessage = "无法获取族类型"
                        });
                        return;
                    }
                    
                    using (var bitmap = type.GetPreviewImage(new Size(width, height)))
                    {
                        if(bitmap == null)
                        {
                            cb(new ProjectFamilyThumbnailResult
                            {
                                FamilyName = familyName,
                                StatusMessage = "未返回缩略图"
                            });
                            return;
                        }

                        using (var ms = new MemoryStream())
                        {
                            bitmap.Save(ms, ImageFormat.Png);
                            cb(new ProjectFamilyThumbnailResult
                            {
                                FamilyName = familyName,
                                ImageBytes = ms.ToArray(),
                                StatusMessage = "ok"
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    cb(new ProjectFamilyThumbnailResult
                    {
                        FamilyName = familyName,
                        StatusMessage = "读取项目族缩略图失败：" +  ex.Message
                    });
                }
                finally
                {
                    lock (_requests)
                    {
                        if (_requests.Count > 0)
                            _externalEvent.Raise();
                    }
                }
            }

            public string GetName() => "Project Family Thumbnail Provider ExternalEvent";
        }
    }
    public class ProjectFamilyThumbnailResult
    {
        public string FamilyName { get; set; }
        public byte[] ImageBytes { get; set; }
        public string StatusMessage { get; set; }
    }
}
