using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RevitFamilyBrowser.ViewModels
{
    public enum LibraryTreeNodeType
    {
        Folder,
        FamilyCategory,
        Family
    }
    public class LibraryTreeNodeViewModel : TreeNodeViewModel
    {
        private bool _childrenLoaded;
        private static readonly DummyNodeViewModel Dummy = new DummyNodeViewModel();

        public string Name { get; }
        public LibraryTreeNodeType NodeType { get; }
        public string FullPath { get; }
        public string FamilyName { get; }
        public string CategoryName { get; }

        private readonly Action<LibraryTreeNodeViewModel> _onSelected;
        public LibraryTreeNodeViewModel(string name,
            LibraryTreeNodeType nodeType,
            TreeNodeViewModel parent = null,
            string fullPath = null,
            string familyName = null,
            string categoryName = null,
            Action<LibraryTreeNodeViewModel> onSelected = null,
            Action<LibraryTreeNodeViewModel> loadChildrenOnExpand = null) : base(parent)
        {
            Name = name;
            NodeType = nodeType;
            FullPath = fullPath;
            FamilyName = familyName;
            CategoryName = categoryName;
            _onSelected = onSelected;

            if (loadChildrenOnExpand != null)
            {
                ExpandChildrenCallback = _ =>
                {
                    if (_childrenLoaded) return;
                    _childrenLoaded = true;
                    loadChildrenOnExpand(this);
                };
            }

            if (NodeType == LibraryTreeNodeType.Folder && !string.IsNullOrWhiteSpace(FullPath) && HasSubDirectoriesSafe(FullPath))
                Children.Add(Dummy);
        }

        protected override void OnSelected()
        {
            _onSelected?.Invoke(this);
        }

        public void ReplaceChildren(IEnumerable<LibraryTreeNodeViewModel> children)
        {
            Children.Clear();
            foreach (var child in children ?? Enumerable.Empty<LibraryTreeNodeViewModel>()) 
                Children.Add(child);
        }

        public void ClearDummyChild()
        {
            if (Children.Count == 1 && ReferenceEquals(Children[0], Dummy))
                Children.Clear();
        }

        private static bool HasSubDirectoriesSafe(string path)
        {
            try
            {
                return Directory.EnumerateDirectories(path).Any();
            }
            catch { return false; }
        }

        private sealed class DummyNodeViewModel : TreeNodeViewModel
        {
            public DummyNodeViewModel() : base(null) { }
        }
    }
}
