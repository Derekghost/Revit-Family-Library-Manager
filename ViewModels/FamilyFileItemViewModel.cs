using System.IO;
using System;

namespace RevitFamilyBrowser.ViewModels
{
    public class FamilyFileItemViewModel
    {
        public string FullPath { get; private set; }
        public string FileName { get; private set; }

        public FamilyFileItemViewModel(string fullPath)
        {
            FullPath = fullPath;
            FileName = Path.GetFileNameWithoutExtension(fullPath);
        }

        public string Extension
        {
            get { return Path.GetExtension(FullPath); }
        }
    }
}
