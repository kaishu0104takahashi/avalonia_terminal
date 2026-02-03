using Avalonia.Media.Imaging;

namespace avalonia_terminal.Models;

public class GalleryDetectionItem
{
    public int Id { get; set; }
    public string Label => $"R{Id}";      // 例: R1
    public string ValueText { get; set; } // 例: 1000Ω
    public Bitmap? CroppedImage { get; set; }
    
    public GalleryDetectionItem(int id, string value, Bitmap? image)
    {
        Id = id;
        ValueText = string.IsNullOrEmpty(value) ? "不明" : $"{value}Ω";
        CroppedImage = image;
    }
}
