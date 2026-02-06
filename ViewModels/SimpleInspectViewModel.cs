using System;
using System.Collections.Generic;
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
        set 
        { 
            _isCaptured = value; 
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(IsCaptureButtonVisible));
        }
    }

    // ★追加: 撮影処理中かどうか
    private bool _isCapturing;
    public bool IsCapturing
    {
        get => _isCapturing;
        set 
        { 
            _isCapturing = value; 
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(IsCaptureButtonVisible));
        }
    }

    // ★追加: ボタンの表示判定 (撮影済みでなく、かつ処理中でもないとき表示)
    public bool IsCaptureButtonVisible => !IsCaptured && !IsCapturing;

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
                // ★追加: 処理開始フラグOn (ボタン非表示)
                IsCapturing = true;

                Main.LatestDetections.Clear();
                var startTime = DateTime.Now;

                StatusMessage = "高画質撮影中...";

                // 推論開始
                await Main.TcpServer.SendJsonAsync(new 
                { 
                    type = "cmd", 
                    command = "change_format", 
                    args = new { format = "YUV422" } 
                });

                // データ受信待ち (最大20秒)
                bool received = false;
                for (int i = 0; i < 200; i++)
                {
                    if (Main.LastDataReceivedTime > startTime)
                    {
                        received = true;
                        break;
                    }
                    await Task.Delay(100);
                }

                if (!received)
                {
                    StatusMessage = "エラー: データ受信タイムアウト";
                    IsCapturing = false; // エラー時はボタン再表示
                    return;
                }

                // 撮影確定
                Main.IsCameraPaused = true;
                CapturedImage = Main.CameraImage;
                IsCaptured = true; 
                IsCapturing = false; // 処理完了 (IsCapturedがTrueなのでボタンは非表示のまま)
                
                int count = Main.LatestDetections?.Count ?? 0;
                StatusMessage = $"検出: {count}個\nこの画像で保存しますか？";
            }
        });

        RetakeCommand = new RelayCommand(() =>
        {
            CapturedImage = null;
            IsCaptured = false;
            IsCapturing = false;
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

                var detectionsToSave = Main.LatestDetections ?? new List<detection_data>();
                
                _dbService.CreateDetectionTableAndInsert(timestamp, detectionsToSave);
                Console.WriteLine($"Saved {detectionsToSave.Count} detections to table '{timestamp}'");

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
