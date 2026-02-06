using System.Collections.Generic;

namespace avalonia_terminal.Models;

public class type_data_json
{
    public string type { get; set; } = "";
    public int frame_id { get; set; }
    public List<detection_data> detections { get; set; } = new();
}

public class detection_data
{
    public int detection_id { get; set; }
    public string? value { get; set; }
    
    // ★重要: C++側のキー名 "yolo_obb" に合わせる
    public yolo_obb? yolo_obb { get; set; }
}

public class yolo_obb
{
    public float center_x { get; set; }
    public float center_y { get; set; }
    public float width { get; set; }
    public float height { get; set; }
    public float rotation_rad { get; set; }
}
