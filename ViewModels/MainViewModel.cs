using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using avalonia_terminal.Models;
using avalonia_terminal.Services;
using avalonia_terminal.Views;

namespace avalonia_terminal.ViewModels;

public class MainViewModel : ViewModelBase
{
    private ViewModelBase _currentViewModel;
    public ViewModelBase CurrentViewModel
    {
        get => _currentViewModel;
        set { _currentViewModel = value; RaisePropertyChanged(); }
    }

    private bool _isFullScreen = true;
    public bool IsFullScreen
    {
        get => _isFullScreen;
        set { _isFullScreen = value; RaisePropertyChanged(); }
    }

    private readonly UdpVideoReceiver _videoReceiver;
    
    // アプリ全体で共有するTCPサーバー (Port 55555)
    public TcpJsonClient TcpServer { get; }

    private Bitmap? _cameraImage;
    public Bitmap? CameraImage
    {
        get => _cameraImage;
        set { _cameraImage = value; RaisePropertyChanged(); }
    }

    private bool _isCameraPaused = false;
    public bool IsCameraPaused
    {
        get => _isCameraPaused;
        set 
        {
            _isCameraPaused = value;
            if (_videoReceiver != null)
            {
                _videoReceiver.IsPaused = value;
            }
            RaisePropertyChanged();
        }
    }

    // 【追加】最新の検出データ（保存ボタンを押すまでここに保持する）
    public List<DetectionItem> LatestDetections { get; set; } = new();

    public MainViewModel()
    {
        // 1. 映像受信開始 (Port 50000)
        _videoReceiver = new UdpVideoReceiver(50000);
        _videoReceiver.OnFrameReceived += (bmp) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                CameraImage = bmp;
            });
        };
        _videoReceiver.Start();

        // 2. コマンド送受信用サーバー開始 (Port 55555)
        TcpServer = new TcpJsonClient(55555);
        
        // 受信したJSONを処理
        TcpServer.OnJsonReceived += (json) => 
        {
            try
            {
                // 1. ログ保存
                string logPath = "/home/shikoku-pc/json_log.txt";
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string logLine = $"[{timestamp}] {json}{Environment.NewLine}";
                File.AppendAllText(logPath, logLine);

                // 2. 検出データのパースと保持
                // type: "data" の場合のみ処理する
                if (json.Contains("\"type\": \"data\"") || json.Contains("\"type\":\"data\""))
                {
                    var response = JsonSerializer.Deserialize<DetectionResponse>(json);
                    if (response != null && response.Detections != null)
                    {
                        LatestDetections = response.Detections;
                        Console.WriteLine($"Detection Data Received: {LatestDetections.Count} items");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"JSON Process Error: {ex.Message}");
            }
        };

        TcpServer.Start();

        // 初期画面は時刻設定から
        _currentViewModel = new TimeSettingViewModel(() =>
        {
            Navigate(new HomeViewModel(this));
        });
    }

    public void Navigate(ViewModelBase viewModel)
    {
        CurrentViewModel = viewModel;
    }

    public void ShutdownApplication()
    {
        _videoReceiver.Stop();
        TcpServer.Stop(); // サーバーも停止
        
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
