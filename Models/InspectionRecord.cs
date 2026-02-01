namespace avalonia_terminal.Models;

public class InspectionRecord
{
    public int Id { get; set; }
    public string Date { get; set; } = "";
    public string SaveName { get; set; } = "";
    public string SaveAbsolutePath { get; set; } = "";
    public string ThumbnailPath { get; set; } = "";
    
    // Typeカラムは削除済み

    // 簡易検査用パス
    public string SimpleOmotePath { get; set; } = "";
    public string SimpleUraPath { get; set; } = "";
    
    // 精密検査用パス
    public string PrecisionPcbOmotePath { get; set; } = "";
    public string PrecisionPcbUraPath { get; set; } = "";
    public string PrecisionCircuitOmotePath { get; set; } = "";
    public string PrecisionCircuitUraPath { get; set; } = "";
}
