using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using avalonia_terminal.Models;
using avalonia_terminal.Services;

namespace avalonia_terminal.ViewModels;

public class SimpleInspectViewModel : ViewModelBase
{
    public MainViewModel Main { get; }
    private readonly DatabaseService _dbService;

    private Bitmap? _capturedImage;
    public Bitmap? CapturedImage
    {
        get => _capturedImage;
        set { _capturedImage = value; RaisePropertyChanged(); }
    }

    private bool _isCaptured;
    public bool IsCaptured
    {
        get => _isCaptured;
        set { _isCaptured = value; RaisePropertyChanged(); }
    }

    private string _statusMessage = "検査対象をセットして\n撮影ボタンを押してください";
    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; RaisePropertyChanged(); }
    }

    public ICommand BackCommand { get; }
    public ICommand CaptureCommand { get; }
    public ICommand RetakeCommand { get; }
    public ICommand SaveCommand { get; }

    public SimpleInspectViewModel(MainViewModel main)
    {
        Main = main;
        _dbService = new DatabaseService();

        BackCommand = new RelayCommand(async () => 
        {
            await Main.TcpServer.SendJsonAsync(new 
            { 
                type = "cmd", 
                command = "change_format", 
                args = new { format = "MJPEG" } 
            });

            Main.IsCameraPaused = false;
            Main.Navigate(new HomeViewModel(Main));
        });

        CaptureCommand = new RelayCommand(async () =>
        {
            if (Main.CameraImage != null)
            {
                // 以前の検出データをクリア
                Main.LatestDetections.Clear();

                // 1. 画質変更コマンド送信
                await Main.TcpServer.SendJsonAsync(new 
                { 
                    type = "cmd", 
                    command = "change_format", 
                    args = new { format = "YUV422" } 
                });

                StatusMessage = "高画質撮影中...";
                await Task.Delay(500);

                // 3. 撮影
                Main.IsCameraPaused = true;
                CapturedImage = Main.CameraImage;
                IsCaptured = true;
                StatusMessage = "この画像で保存しますか？";
            }
        });

        RetakeCommand = new RelayCommand(() =>
        {
            CapturedImage = null;
            IsCaptured = false;
            Main.IsCameraPaused = false;
            StatusMessage = "検査対象をセットして\n撮影ボタンを押してください";
        });

        SaveCommand = new RelayCommand(async () =>
        {
            if (CapturedImage == null) return;

            StatusMessage = "保存中...";
            await Task.Delay(50); 

            try
            {
                string baseDir = "/home/shikoku-pc/pic";
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss");
                string saveDir = Path.Combine(baseDir, timestamp);
                Directory.CreateDirectory(saveDir);

                string filePath = Path.Combine(saveDir, "simple_omote.bmp");
                CapturedImage.Save(filePath);
                
                string thumbPath = Path.Combine(saveDir, "thumb.bmp");
                CapturedImage.Save(thumbPath);

                // 1. メインレコード保存
                _dbService.Initialize();
                var record = new InspectionRecord
                {
                    Date = timestamp,
                    SaveName = timestamp,
                    SaveAbsolutePath = saveDir,
                    ThumbnailPath = thumbPath,
                    SimpleOmotePath = filePath
                };
                _dbService.InsertInspection(record);

                // 2. 【追加】検出データがあれば、専用テーブルを作成して保存
                if (Main.LatestDetections != null && Main.LatestDetections.Count > 0)
                {
                    _dbService.CreateDetectionTableAndInsert(timestamp, Main.LatestDetections);
                    Console.WriteLine($"Saved {Main.LatestDetections.Count} detections to table '{timestamp}'");
                }

                // MJPEGに戻してホームへ
                await Main.TcpServer.SendJsonAsync(new 
                { 
                    type = "cmd", 
                    command = "change_format", 
                    args = new { format = "MJPEG" } 
                });

                Main.IsCameraPaused = false;
                Main.Navigate(new HomeViewModel(Main));
            }
            catch (Exception ex)
            {
                StatusMessage = $"エラー: {ex.Message}";
                Console.WriteLine(ex);
            }
        });
    }
}
