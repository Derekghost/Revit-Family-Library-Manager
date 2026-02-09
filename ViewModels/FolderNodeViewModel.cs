using System;
using System.IO;
using System.Linq;

namespace RevitFamilyBrowser.ViewModels
{
    public sealed class FolderNodeViewModel : TreeNodeViewModel
    {
        private bool _childrenLoaded;
        private string newRoot;
        private object parent;
        private bool isRoot;

        public string FullPath { get; private set; }
        public string Name { get; private set; }

        private static readonly DummyNodeViewModel Dummy = new DummyNodeViewModel();

        private readonly FamilyLibraryViewModel _owner;
        public FolderNodeViewModel(string fullPath, TreeNodeViewModel parent, bool isRoot, FamilyLibraryViewModel owner) : base(parent)
        {
            _owner = owner;
            FullPath = fullPath;
            Name = isRoot ? fullPath : Path.GetFileName(fullPath);

            if (HasSubDirectoriesSafe(FullPath))
            {
                Children.Add(Dummy);
            }

            _owner = owner;
        }

        public FolderNodeViewModel(string newRoot, object parent, bool isRoot)
            : base(parent as TreeNodeViewModel)
        {
            this.newRoot = newRoot;
            this.parent = parent;
            this.isRoot = isRoot;
        }

        protected override void OnSelected()
        {
            if(_owner != null)
                _owner.SelectedFolder = this;
        }
        protected override void OnExpanded()
        {
            EnsureChildrenLoaded();
        }

        public void EnsureChildrenLoaded()
        {
            if (_childrenLoaded) return;
            _childrenLoaded = true;
            if (Children.Count == 1 && ReferenceEquals(Children[0], Dummy))
                Children.Clear();
            foreach (var dir in SafeEnumerateDirectories(FullPath))
            {
                Children.Add(new FolderNodeViewModel(dir, this, false, _owner));
            }
        }

        private static bool HasSubDirectoriesSafe(string path)
        {
            try
            {
                return Directory.EnumerateDirectories(path).Any();
            }
            catch
            {
                return false;
            }
        }

        private static string[] SafeEnumerateDirectories(string path)
        {
            try
            {
                return Directory.GetDirectories(path);
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private sealed class DummyNodeViewModel : TreeNodeViewModel
        {
            public DummyNodeViewModel() : base(null) { }
        }
    }
}

