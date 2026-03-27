using System;
using System.Data.SQLite;
using System.IO;

namespace RevitFamilyBrowser.Data
{
    public static class SQLiteDatabase
    {
        public static string BuildConnectionString(string dbPath)
        {
            return $"Data Source={dbPath};Version=3;";
        }

        public static void EnsureDatabase(string dbPath)
        {
            if (string.IsNullOrEmpty(dbPath))
            {
                throw new ArgumentNullException("Database path cannot be empty.", nameof(dbPath));
            }

            var folder = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrWhiteSpace(folder) && !Directory.Exists(folder))
                Directory.CreateDirectory(folder);
            using (var conn = new SQLiteConnection(BuildConnectionString(dbPath)))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS families (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    file_path TEXT NOT NULL UNIQUE,
    file_name TEXT NOT NULL,
    folder_path TEXT NOT NULL,
    last_write_utc TEXT,
    file_size INTEGER,
    created_utc TEXT NOT NULL DEFAULT (datetime('now'))
);
CREATE INDEX IF NOT EXISTS idx_families_folder_path ON families(folder_path);
CREATE INDEX IF NOT EXISTS idx_families_file_name ON families(file_name);";
                    cmd.ExecuteNonQuery();
                }
            }
        }
    }
}
