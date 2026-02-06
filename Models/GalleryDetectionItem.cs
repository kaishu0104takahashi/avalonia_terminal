using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;

namespace avalonia_terminal.Models;

public class GalleryDetectionItem : INotifyPropertyChanged
{
    public int Id { get; set; }
    
    // ★修正: 表示用ラベルを detection_id + 1 にする ("R1" 始まりにする)
    public string Label => $"R{Id + 1}";
    
    public string RawValue { get; set; }
    
    public string FormattedValue => FormatSiPrefix(RawValue);

    public Bitmap? CroppedImage { get; set; }
    
    // 変更通知用フィールド
    private bool _isCorrectionMode;
    public bool IsCorrectionMode 
    { 
        get => _isCorrectionMode;
        set 
        {
            if (_isCorrectionMode != value)
            {
                _isCorrectionMode = value;
                OnPropertyChanged(); // 値が変わったら通知
            }
        }
    }

    public GalleryDetectionItem(int id, string value, Bitmap? image)
    {
        Id = id;
        RawValue = value ?? "";
        CroppedImage = image;
    }

    private string FormatSiPrefix(string input)
    {
        if (string.IsNullOrEmpty(input)) return "不明";
        if (ParseWithSuffix(input, out double val))
        {
            if (val >= 1_000_000) return $"{val / 1_000_000:0.##}MΩ";
            if (val >= 1_000) return $"{val / 1_000:0.##}kΩ";
            return $"{val}Ω";
        }
        
        return input;
    }

    private bool ParseWithSuffix(string input, out double result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(input)) return false;

        string normalized = input.Trim().ToUpperInvariant().Replace("Ω", "");
        double multiplier = 1.0;
        if (normalized.EndsWith("K"))
        {
            multiplier = 1_000.0;
            normalized = normalized.TrimEnd('K');
        }
        else if (normalized.EndsWith("M"))
        {
            multiplier = 1_000_000.0;
            normalized = normalized.TrimEnd('M');
        }

        if (double.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out double baseVal))
        {
            result = baseVal * multiplier;
            return true;
        }
        return false;
    }

    // INotifyPropertyChanged 実装
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
