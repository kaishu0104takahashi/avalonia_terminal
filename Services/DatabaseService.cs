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
        
        string createTableCmd = @"
            CREATE TABLE IF NOT EXISTS inspection (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                save_name TEXT NOT NULL,
                save_absolute_path TEXT NOT NULL,
                date TEXT NOT NULL,
                type INTEGER NOT NULL
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
            INSERT INTO inspection (save_name, save_absolute_path, date, type)
            VALUES ($saveName, $savePath, $date, $type)";

        command.Parameters.AddWithValue("$saveName", record.SaveName);
        command.Parameters.AddWithValue("$savePath", record.SaveAbsolutePath);
        command.Parameters.AddWithValue("$date", record.Date);
        command.Parameters.AddWithValue("$type", record.Type);

        command.ExecuteNonQuery();
    }

    public List<InspectionRecord> GetAllRecords()
    {
        var list = new List<InspectionRecord>();
        
        if (!File.Exists("/home/shikoku-pc/db/pcb_inspection.db")) return list;

        try
        {
            using var connection = new SqliteConnection(DbPath);
            connection.Open();

            string query = "SELECT id, save_name, save_absolute_path, date, type FROM inspection ORDER BY id DESC";
            using var command = new SqliteCommand(query, connection);
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                var r = new InspectionRecord();

                r.Id = reader.GetInt32(0);
                r.SaveName = reader.GetString(1);
                r.SaveAbsolutePath = reader.GetString(2);
                r.Date = reader.GetString(3);
                r.Type = reader.GetInt32(4);

                // 【修正】フォルダ内にある様々なパターンの画像をすべて割り当てる
                if (!string.IsNullOrEmpty(r.SaveAbsolutePath) && Directory.Exists(r.SaveAbsolutePath))
                {
                    string folderName = Path.GetFileName(r.SaveAbsolutePath); // 例: jump

                    // 1. 簡易検査画像 (Simple)
                    r.SimpleOmotePath = FindImageFile(r.SaveAbsolutePath, $"{folderName}_omote", "simple_omote");
                    r.SimpleUraPath   = FindImageFile(r.SaveAbsolutePath, $"{folderName}_ura", "simple_ura");

                    // 2. 精密検査・基板画像 (PCB)
                    r.PrecisionPcbOmotePath = FindImageFile(r.SaveAbsolutePath, $"{folderName}_PCB_omote", "pcb_omote");
                    r.PrecisionPcbUraPath   = FindImageFile(r.SaveAbsolutePath, $"{folderName}_PCB_ura", "pcb_ura");

                    // 3. 精密検査・回路画像 (Circuit)
                    r.PrecisionCircuitOmotePath = FindImageFile(r.SaveAbsolutePath, $"{folderName}_circuit_omote", "circuit_omote");
                    r.PrecisionCircuitUraPath   = FindImageFile(r.SaveAbsolutePath, $"{folderName}_circuit_ura", "circuit_ura");

                    // 4. サムネイル (thumb優先、なければ PCBオモテ、なければ 簡易オモテ)
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

    // 指定されたキーワード（優先順）で画像を探す
    private string FindImageFile(string dir, params string[] keywords)
    {
        try
        {
            var files = Directory.GetFiles(dir);
            if (files.Length == 0) return "";

            foreach (var keyword in keywords)
            {
                // 大文字小文字を区別せず、キーワードを含む画像を探す
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
        using var command = new SqliteCommand("DELETE FROM inspection WHERE id = $id", connection);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void UpdateInspectionName(int id, string newName, string newPath)
    {
        using var connection = new SqliteConnection(DbPath);
        connection.Open();
        using var command = new SqliteCommand("UPDATE inspection SET save_name = $n, save_absolute_path = $p WHERE id = $id", connection);
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$n", newName);
        command.Parameters.AddWithValue("$p", newPath);
        command.ExecuteNonQuery();
    }
}
