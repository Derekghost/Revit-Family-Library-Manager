using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace RevitFamilyBrowser.Data
{
    public class SQLiteLibraryRepository
    {
        private readonly string _connectionString;
        private readonly object _writeLock = new object();

        public SQLiteLibraryRepository(string dbPath)
        {
            SQLiteDatabase.EnsureDatabase(dbPath);
            _connectionString = SQLiteDatabase.BuildConnectionString(dbPath);
        }

        public void UpsertFamily(string filePath, string fileName, string folderPath, DateTime lastWriteUtc, long fileSize)
        {
            lock (_writeLock)
            {
                using (var conn = new SQLiteConnection(_connectionString))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
INSERT INTO families(file_path, file_name, folder_path, last_write_utc, file_size)
VALUES(@file_path, @file_name, @folder_path, @last_write_utc, @file_size)
ON CONFLICT(file_path) DO UPDATE SET
    file_name = excluded.file_name,
    folder_path = excluded.folder_path,
    last_write_utc = excluded.last_write_utc,
    file_size = excluded.file_size;";

                        cmd.Parameters.AddWithValue("@file_path", filePath ?? string.Empty);
                        cmd.Parameters.AddWithValue("@file_name", fileName ?? string.Empty);
                        cmd.Parameters.AddWithValue("@folder_path", folderPath ?? string.Empty);
                        cmd.Parameters.AddWithValue("@last_write_utc", lastWriteUtc.ToString("o"));
                        cmd.Parameters.AddWithValue("@file_size", fileSize);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
        }

        public List<SQLiteFamilyRecord> SearchByFolder (string folderPath, string keyword)
        {
            var result = new List<SQLiteFamilyRecord>();

            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
SELECT file_path, file_name, folder_path, last_write_utc, file_size, created_utc
FROM families
WHERE folder_path = @folder_path
  AND (@keyword = '' OR file_name LIKE @keyword_like)
ORDER BY file_name;";

                    var kw = keyword ?? string.Empty;
                    cmd.Parameters.AddWithValue("@folder_path", folderPath ?? string.Empty);
                    cmd.Parameters.AddWithValue("@keyword", kw);
                    cmd.Parameters.AddWithValue("@keyword_like", "%" + kw + "%");

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            result.Add(new SQLiteFamilyRecord
                            {
                                FilePath = reader ["file_path"] as string,
                                FileName = reader ["file_name"] as string,
                                FolderPath = reader["folder_path"] as string,
                                LastWriteUtc = reader["last_write_utc"] as string,
                                FileSize = reader["file_size"] == DBNull.Value ? "0" : Convert.ToString(reader["file_size"]),
                                CreateUtc = reader["created_utc"] as string
                            });
                        }
                    }
                }
            }
            return result;
        }
    }
}
