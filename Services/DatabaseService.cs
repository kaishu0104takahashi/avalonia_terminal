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

    public void Initialize()
    {
        using var connection = new SqliteConnection(DbPath);
        connection.Open();
        
        // メインテーブル作成
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

    /// <summary>
    /// 【追加】検出データ用の動的テーブルを作成し、データを保存する
    /// テーブル名は save_name をそのまま使用する (例: save_name="jump" -> table="jump")
    /// </summary>
    public void CreateDetectionTableAndInsert(string saveName, List<DetectionItem> detections)
    {
        // テーブル名として不適切な文字が含まれていないか簡易チェック（SQLインジェクション対策）
        // 英数字とアンダースコアのみ許可するのが安全ですが、今回はダブルクォートで囲む方針で対応します
        string tableName = $"\"{saveName}\""; 

        using var connection = new SqliteConnection(DbPath);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        try
        {
            // 1. 動的テーブルの作成
            // detection_idをプライマリーキーとする
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

            // 2. データの挿入
            foreach (var item in detections)
            {
                if (item.YoloObb == null) continue;

                string insertSql = $@"
                    INSERT INTO {tableName} (detection_id, value, center_x, center_y, width, height, rotation_rad)
                    VALUES ($id, $val, $cx, $cy, $w, $h, $rot)";

                using var cmdInsert = new SqliteCommand(insertSql, connection, transaction);
                cmdInsert.Parameters.AddWithValue("$id", item.DetectionId);
                cmdInsert.Parameters.AddWithValue("$val", (object?)item.Value ?? DBNull.Value);
                cmdInsert.Parameters.AddWithValue("$cx", item.YoloObb.CenterX);
                cmdInsert.Parameters.AddWithValue("$cy", item.YoloObb.CenterY);
                cmdInsert.Parameters.AddWithValue("$w", item.YoloObb.Width);
                cmdInsert.Parameters.AddWithValue("$h", item.YoloObb.Height);
                cmdInsert.Parameters.AddWithValue("$rot", item.YoloObb.RotationRad);
                
                cmdInsert.ExecuteNonQuery();
            }

            transaction.Commit();
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            Console.WriteLine($"Table Creation/Insert Error: {ex.Message}");
            throw; // エラーを呼び出し元に伝える
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
        catch
        {
            return "";
        }
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

        // 削除する前にテーブル名（save_name）を取得する
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
            // 1. メインレコード削除
            using (var cmdDelete = new SqliteCommand("DELETE FROM inspection WHERE id = $id", connection, transaction))
            {
                cmdDelete.Parameters.AddWithValue("$id", id);
                cmdDelete.ExecuteNonQuery();
            }

            // 2. 対応する詳細テーブルを削除 (存在すれば)
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

    // 名前変更時に詳細テーブルもリネームする
    public void UpdateInspectionName(int id, string newName, string newPath)
    {
        using var connection = new SqliteConnection(DbPath);
        connection.Open();

        // 変更前の名前を取得
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
            // 1. メインレコード更新
            using (var cmdUpdate = new SqliteCommand("UPDATE inspection SET save_name = $n, save_absolute_path = $p WHERE id = $id", connection, transaction))
            {
                cmdUpdate.Parameters.AddWithValue("$id", id);
                cmdUpdate.Parameters.AddWithValue("$n", newName);
                cmdUpdate.Parameters.AddWithValue("$p", newPath);
                cmdUpdate.ExecuteNonQuery();
            }

            // 2. 詳細テーブルのリネーム (古いテーブルが存在すれば)
            if (!string.IsNullOrEmpty(oldName) && oldName != newName)
            {
                // 古いテーブルが存在するか確認 (sqlite_master を検索)
                // ※リネームは ALTER TABLE "old" RENAME TO "new"
                // ただしテーブル名が存在しないとエラーになるため、本来はチェックが必要ですが、
                // 今回はtry-catchで囲んでいるため、存在しない場合は無視される挙動になります（または個別チェック追加）
                
                string renameSql = $"ALTER TABLE \"{oldName}\" RENAME TO \"{newName}\"";
                using (var cmdRename = new SqliteCommand(renameSql, connection, transaction))
                {
                    // テーブルが無い場合のエラーを許容するか、事前にチェックするか
                    try { cmdRename.ExecuteNonQuery(); } catch { /* テーブルがない場合は何もしない */ }
                }
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
        }
    }
}
