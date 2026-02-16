using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;
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
        set 
        { 
            _currentViewModel = value;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(IsHomeView));
        }
    }

    public bool IsHomeView => CurrentViewModel is HomeViewModel;
    private bool _isFullScreen = true;
    public bool IsFullScreen
    {
        get => _isFullScreen;
        set { _isFullScreen = value; RaisePropertyChanged(); }
    }

    private readonly UdpVideoReceiver _videoReceiver;
    
    // Serverに変更済み
    public TcpJsonServer TcpServer { get; }

    public bool IsConnected => TcpServer.IsConnected;

    private Bitmap? _cameraImage;
    public Bitmap? CameraImage
    {
        get => _cameraImage;
        set { _cameraImage = value; RaisePropertyChanged(); }
    }

    // 画像のフレームID (今回は更新しない)
    private uint _currentImageFrameId;
    public uint CurrentImageFrameId
    {
        get => _currentImageFrameId;
        set { _currentImageFrameId = value; RaisePropertyChanged(); }
    }

    // JSONのフレームID
    private int _latestJsonFrameId;
    public int LatestJsonFrameId
    {
        get => _latestJsonFrameId;
        set { _latestJsonFrameId = value; RaisePropertyChanged(); }
    }

    private bool _isCameraPaused = false;
    public bool IsCameraPaused
    {
        get => _isCameraPaused;
        set 
        {
            _isCameraPaused = value;
            if (_videoReceiver != null) _videoReceiver.IsPaused = value;
            RaisePropertyChanged();
        }
    }

    public List<detection_data> LatestDetections { get; set; } = new();

    public DateTime LastDataReceivedTime { get; private set; } = DateTime.MinValue;

    public ICommand ToggleFullScreenCommand { get; }

    private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public MainViewModel()
    {
        ToggleFullScreenCommand = new RelayCommand(() => IsFullScreen = !IsFullScreen);
        _videoReceiver = new UdpVideoReceiver(50000);
        
        // ★修正: 引数を (bmp) のみに戻しました
        _videoReceiver.OnFrameReceived += (bmp) => 
        {
            Dispatcher.UIThread.Post(() => 
            {
                CameraImage = bmp;
                // CurrentImageFrameId = id; // UdpVideoReceiver側が対応していないため削除
            });
        };
        _videoReceiver.Start();

        // Serverとして初期化
        TcpServer = new TcpJsonServer(55555);
        
        TcpServer.OnJsonReceived += (json) => 
        {
            try { File.AppendAllText("/home/shikoku-pc/json_log.txt", $"[{DateTime.Now:HH:mm:ss}] {json}\n"); } catch { }

            try
            {
                if (json.Contains("\"type\": \"data\"") || json.Contains("\"type\":\"data\""))
                {
                    var response = JsonSerializer.Deserialize<type_data_json>(json, _jsonOptions);
                    if (response != null)
                    {
                        LatestDetections = response.detections ?? new List<detection_data>();
                        LatestJsonFrameId = response.frame_id;
                        LastDataReceivedTime = DateTime.Now;
                        Console.WriteLine($"[JSON] Updated: ID={response.frame_id}, Count={LatestDetections.Count}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[JSON Error] {ex.Message}");
            }
        };

        TcpServer.Start();
        
        _currentViewModel = new TimeSettingViewModel(() => Navigate(new HomeViewModel(this)));
    }

    public void Navigate(ViewModelBase viewModel) => CurrentViewModel = viewModel;
    public void ShutdownApplication()
    {
        _videoReceiver.Stop();
        TcpServer.Stop();
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
