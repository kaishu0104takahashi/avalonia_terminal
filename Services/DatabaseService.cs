using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using avalonia_terminal.Models;
namespace avalonia_terminal.Services;

public class DatabaseService
{
    private const string DbPath = "Data Source=/home/shikoku-pc/db/pcb_inspection.db";
    private const string DbDir = "/home/shikoku-pc/db";

    public void Initialize()
    {
        // ディレクトリが存在しない場合は作成
        if (!Directory.Exists(DbDir))
        {
            Directory.CreateDirectory(DbDir);
        }

        using var connection = new SqliteConnection(DbPath);
        connection.Open();
        
        string createTableCmd = @"
            CREATE TABLE IF NOT EXISTS inspection (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                save_name TEXT NOT NULL,
                save_absolute_path TEXT NOT NULL,
                date TEXT NOT NULL
            )";
        using var command = new SqliteCommand(createTableCmd, connection);
        command.ExecuteNonQuery();
    }

    public void InsertInspection(InspectionRecord record)
    {
        Initialize(); 

        using var connection = new SqliteConnection(DbPath);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO inspection (save_name, save_absolute_path, date)
            VALUES ($saveName, $savePath, $date)";
        command.Parameters.AddWithValue("$saveName", record.SaveName);
        command.Parameters.AddWithValue("$savePath", record.SaveAbsolutePath);
        command.Parameters.AddWithValue("$date", record.Date);

        command.ExecuteNonQuery();
    }

    public void CreateDetectionTableAndInsert(string saveName, List<detection_data> detections)
    {
        string tableName = $"\"{saveName}\"";
        using var connection = new SqliteConnection(DbPath);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            string createSql = $@"
                CREATE TABLE IF NOT EXISTS {tableName} (
                    detection_id INTEGER PRIMARY KEY,
                    value VARCHAR(10),
                    center_x FLOAT,
                    center_y FLOAT,
                    width FLOAT,
                    height FLOAT,
                    rotation_rad FLOAT
                );";
            using (var cmdCreate = new SqliteCommand(createSql, connection, transaction))
            {
                cmdCreate.ExecuteNonQuery();
            }

            foreach (var item in detections)
            {
                if (item.yolo_obb == null) continue;
                string insertSql = $@"
                    INSERT INTO {tableName} (detection_id, value, center_x, center_y, width, height, rotation_rad)
                    VALUES ($id, $val, $cx, $cy, $w, $h, $rot)";
                using (var cmdInsert = new SqliteCommand(insertSql, connection, transaction))
                {
                    cmdInsert.Parameters.AddWithValue("$id", item.detection_id);
                    cmdInsert.Parameters.AddWithValue("$val", (object?)item.value ?? DBNull.Value);
                    cmdInsert.Parameters.AddWithValue("$cx", item.yolo_obb.center_x);
                    cmdInsert.Parameters.AddWithValue("$cy", item.yolo_obb.center_y);
                    cmdInsert.Parameters.AddWithValue("$w", item.yolo_obb.width);
                    cmdInsert.Parameters.AddWithValue("$h", item.yolo_obb.height);
                    cmdInsert.Parameters.AddWithValue("$rot", item.yolo_obb.rotation_rad);
                    cmdInsert.ExecuteNonQuery();
                }
            }
            transaction.Commit();
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            Console.WriteLine($"Table Creation/Insert Error: {ex.Message}");
            throw;
        }
    }

    public List<DetectionItem> GetDetections(string saveName)
    {
        var list = new List<DetectionItem>();
        string tableName = $"\"{saveName}\"";
        
        try 
        {
            using var connection = new SqliteConnection(DbPath);
            connection.Open();

            string query = $"SELECT detection_id, value, center_x, center_y, width, height, rotation_rad FROM {tableName} ORDER BY detection_id";
            using var command = new SqliteCommand(query, connection);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var item = new DetectionItem
                {
                    DetectionId = reader.GetInt32(0),
                    Value = reader.IsDBNull(1) ? null : reader.GetString(1),
                    YoloObb = new YoloObb
                    {
                        CenterX = reader.GetFloat(2),
                        CenterY = reader.GetFloat(3),
                        Width = reader.GetFloat(4),
                        Height = reader.GetFloat(5),
                        RotationRad = reader.GetFloat(6)
                    }
                };
                list.Add(item);
            }
        }
        catch (Exception)
        {
        }
        return list;
    }

    public void UpdateDetectionValue(string saveName, int detectionId, string newValue)
    {
        string tableName = $"\"{saveName}\"";
        try
        {
            using var connection = new SqliteConnection(DbPath);
            connection.Open();
            
            string updateSql = $"UPDATE {tableName} SET value = $val WHERE detection_id = $id";
            using var command = new SqliteCommand(updateSql, connection);
            command.Parameters.AddWithValue("$val", newValue);
            command.Parameters.AddWithValue("$id", detectionId);
            
            command.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Update Value Error: {ex.Message}");
            throw;
        }
    }

    public List<InspectionRecord> GetAllRecords()
    {
        var list = new List<InspectionRecord>();
        if (!File.Exists("/home/shikoku-pc/db/pcb_inspection.db")) return list;

        try
        {
            using var connection = new SqliteConnection(DbPath);
            connection.Open();

            string query = "SELECT id, save_name, save_absolute_path, date FROM inspection ORDER BY id DESC";
            using var command = new SqliteCommand(query, connection);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var r = new InspectionRecord();
                r.Id = reader.GetInt32(0);
                r.SaveName = reader.GetString(1);
                r.SaveAbsolutePath = reader.GetString(2);
                r.Date = reader.GetString(3);
                if (!string.IsNullOrEmpty(r.SaveAbsolutePath) && Directory.Exists(r.SaveAbsolutePath))
                {
                    string folderName = Path.GetFileName(r.SaveAbsolutePath);
                    r.SimpleOmotePath = FindImageFile(r.SaveAbsolutePath, $"{folderName}_omote", "simple_omote");
                    r.SimpleUraPath   = FindImageFile(r.SaveAbsolutePath, $"{folderName}_ura", "simple_ura");
                    r.PrecisionPcbOmotePath = FindImageFile(r.SaveAbsolutePath, $"{folderName}_PCB_omote", "pcb_omote");
                    r.PrecisionPcbUraPath   = FindImageFile(r.SaveAbsolutePath, $"{folderName}_PCB_ura", "pcb_ura");
                    r.PrecisionCircuitOmotePath = FindImageFile(r.SaveAbsolutePath, $"{folderName}_circuit_omote", "circuit_omote");
                    r.PrecisionCircuitUraPath   = FindImageFile(r.SaveAbsolutePath, $"{folderName}_circuit_ura", "circuit_ura");
                    r.ThumbnailPath = FindImageFile(r.SaveAbsolutePath, "thumb");
                    if (string.IsNullOrEmpty(r.ThumbnailPath)) r.ThumbnailPath = r.PrecisionPcbOmotePath;
                    if (string.IsNullOrEmpty(r.ThumbnailPath)) r.ThumbnailPath = r.SimpleOmotePath;
                    if (string.IsNullOrEmpty(r.ThumbnailPath)) r.ThumbnailPath = FindFirstImageFile(r.SaveAbsolutePath);
                }

                list.Add(r);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DB Load Error: {ex.Message}");
        }

        return list;
    }

    private string FindImageFile(string dir, params string[] keywords)
    {
        try
        {
            var files = Directory.GetFiles(dir);
            if (files.Length == 0) return "";

            foreach (var keyword in keywords)
            {
                var match = files.FirstOrDefault(f =>
                    Path.GetFileName(f).Contains(keyword, StringComparison.OrdinalIgnoreCase) &&
                    (f.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase) ||
                     f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                     f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)));
                if (match != null) return match;
            }
            return "";
        }
        catch { return ""; }
    }

    private string FindFirstImageFile(string dir)
    {
        try {
            var files = Directory.GetFiles(dir);
            return files.FirstOrDefault(f =>
                 f.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase) ||
                 f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                 f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) ?? "";
        } catch { return ""; }
    }

    public void DeleteInspection(int id)
    {
        using var connection = new SqliteConnection(DbPath);
        connection.Open();

        string targetName = "";
        using (var cmdSelect = new SqliteCommand("SELECT save_name FROM inspection WHERE id = $id", connection))
        {
            cmdSelect.Parameters.AddWithValue("$id", id);
            var result = cmdSelect.ExecuteScalar();
            if (result != null) targetName = result.ToString() ?? "";
        }

        using var transaction = connection.BeginTransaction();
        try
        {
            using (var cmdDelete = new SqliteCommand("DELETE FROM inspection WHERE id = $id", connection, transaction))
            {
                cmdDelete.Parameters.AddWithValue("$id", id);
                cmdDelete.ExecuteNonQuery();
            }

            if (!string.IsNullOrEmpty(targetName))
            {
                string dropTableSql = $"DROP TABLE IF EXISTS \"{targetName}\"";
                using (var cmdDrop = new SqliteCommand(dropTableSql, connection, transaction))
                {
                    cmdDrop.ExecuteNonQuery();
                }
            }
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
        }
    }

    public void UpdateInspectionName(int id, string newName, string newPath)
    {
        using var connection = new SqliteConnection(DbPath);
        connection.Open();

        string oldName = "";
        using (var cmdSelect = new SqliteCommand("SELECT save_name FROM inspection WHERE id = $id", connection))
        {
            cmdSelect.Parameters.AddWithValue("$id", id);
            var result = cmdSelect.ExecuteScalar();
            if (result != null) oldName = result.ToString() ?? "";
        }

        using var transaction = connection.BeginTransaction();
        try
        {
            // 1. メインレコードの更新
            using (var cmdUpdate = new SqliteCommand("UPDATE inspection SET save_name = $n, save_absolute_path = $p WHERE id = $id", connection, transaction))
            {
                cmdUpdate.Parameters.AddWithValue("$id", id);
                cmdUpdate.Parameters.AddWithValue("$n", newName);
                cmdUpdate.Parameters.AddWithValue("$p", newPath);
                cmdUpdate.ExecuteNonQuery();
            }

            // 2. 詳細テーブルのリネーム
            if (!string.IsNullOrEmpty(oldName) && oldName != newName)
            {
                string checkTableSql = "SELECT name FROM sqlite_master WHERE type='table' AND name=@oldName";
                bool tableExists = false;
                using (var cmdCheck = new SqliteCommand(checkTableSql, connection, transaction))
                {
                    cmdCheck.Parameters.AddWithValue("@oldName", oldName);
                    var result = cmdCheck.ExecuteScalar();
                    tableExists = (result != null);
                }

                if (tableExists)
                {
                    bool isCaseChangeOnly = string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase);
                    if (isCaseChangeOnly)
                    {
                        // 大文字小文字のみの変更の場合、一時名を経由する
                        string tempName = oldName + "_TEMP_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                        string renameTempSql = $"ALTER TABLE \"{oldName}\" RENAME TO \"{tempName}\"";
                        using (var cmdTemp = new SqliteCommand(renameTempSql, connection, transaction))
                        {
                            cmdTemp.ExecuteNonQuery();
                        }

                        string renameFinalSql = $"ALTER TABLE \"{tempName}\" RENAME TO \"{newName}\"";
                        using (var cmdFinal = new SqliteCommand(renameFinalSql, connection, transaction))
                        {
                            cmdFinal.ExecuteNonQuery();
                        }
                    }
                    else
                    {
                        string renameSql = $"ALTER TABLE \"{oldName}\" RENAME TO \"{newName}\"";
                        using (var cmdRename = new SqliteCommand(renameSql, connection, transaction))
                        {
                            cmdRename.ExecuteNonQuery();
                        }
                    }
                }
            }
            transaction.Commit();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Update Name Error: {ex.Message}");
            transaction.Rollback();
            throw; 
        }
    }
}
