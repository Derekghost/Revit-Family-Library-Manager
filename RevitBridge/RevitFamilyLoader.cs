using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace RevitFamilyBrowser.RevitBridge
{
    public static class RevitFamilyLoader
    {
        private static ExternalEvent _exEvent;
        private static LoaderHandler _handler;

        // ✅ 必须在 ExternalCommand.Execute 里调用一次
        public static void Initialize()
        {
            if (_exEvent != null) return;
            _handler = new LoaderHandler();
            _exEvent = ExternalEvent.Create(_handler);
        }

        public static void RequestLoad(string familyPath)
        {
            if (string.IsNullOrEmpty(familyPath)) return;
            if (_exEvent == null) return;

            _handler.Enqueue(familyPath);
            _exEvent.Raise();
        }

        private class LoaderHandler : IExternalEventHandler
        {
            private readonly ConcurrentQueue<string> _queue = new ConcurrentQueue<string>();

            public void Enqueue(string path) => _queue.Enqueue(path);
            public void Execute(UIApplication app)
            {
                UIDocument uidoc = app.ActiveUIDocument;
                Document doc = uidoc != null ? uidoc.Document : null;

                if (doc == null) return;

                if (!_queue.TryDequeue(out string path)) return;

                if (!File.Exists(path))
                {
                    TaskDialog.Show("Family Loader", "文件不存在:\n" + path);
                    return;
                }

                string shortName = Path.GetFileNameWithoutExtension(path);

                Transaction t = null;
                try
                {
                    t = new Transaction(doc, "Load Family:" + shortName);
                    t.Start();

                    Family fam;
                    bool ok = doc.LoadFamily(path, new AlwaysOverwriteFamilyLoadOptions(), out fam);

                    t.Commit();
                }
                catch (Exception ex)
                {
                    try
                    {
                        if (t != null && t.HasStarted() && t.GetStatus() == TransactionStatus.Started)
                            t.RollBack();
                    }
                    catch { }

                    TaskDialog.Show("Family Loader Error",
                        shortName + "\n" + ex.GetType().Name + ": " + ex.Message);
                }
                finally
                {
                    if( t != null) t.Dispose();
                }

                if (!_queue.IsEmpty)
                {
                    _exEvent.Raise();
                }
            }
            public string GetName() => "Family Loader ExternalEvent";
        }

        private class AlwaysOverwriteFamilyLoadOptions : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                overwriteParameterValues = true;
                return true;
            }
            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
            {
                source = FamilySource.Family;
                overwriteParameterValues = true;
                return true;
            }
        }
    }
}
