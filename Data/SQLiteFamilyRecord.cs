using System;

namespace RevitFamilyBrowser.Data
{
    public class SQLiteFamilyRecord
    {
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public string FolderPath {  get; set; }
        public string LastWriteUtc { get; set;}
        public string FileSize { get; set;}
        public string CreateUtc { get; set; }
    }
}
