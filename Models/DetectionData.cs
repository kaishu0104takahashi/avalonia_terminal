using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace avalonia_terminal.Models;

// 受信するJSON全体
public class DetectionResponse
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("frame_id")]
    public int FrameId { get; set; }

    [JsonPropertyName("detections")]
    public List<DetectionItem> Detections { get; set; } = new();
}

// 個別の検出データ
public class DetectionItem
{
    [JsonPropertyName("detection_id")]
    public int DetectionId { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }

    [JsonPropertyName("yolo_obb")]
    public YoloObb? YoloObb { get; set; }
}

// 座標データ
public class YoloObb
{
    [JsonPropertyName("center_x")]
    public float CenterX { get; set; }

    [JsonPropertyName("center_y")]
    public float CenterY { get; set; }

    [JsonPropertyName("width")]
    public float Width { get; set; }

    [JsonPropertyName("height")]
    public float Height { get; set; }

    [JsonPropertyName("rotation_rad")]
    public float RotationRad { get; set; }
}
