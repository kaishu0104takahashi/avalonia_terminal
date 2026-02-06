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
            // ★追加: 画面が変わるたびにホーム画面かどうか判定して通知
            RaisePropertyChanged(nameof(IsHomeView));
        }
    }

    // ★追加: 現在のViewModelがHomeViewModelならTrue
    public bool IsHomeView => CurrentViewModel is HomeViewModel;

    private bool _isFullScreen = true;
    public bool IsFullScreen
    {
        get => _isFullScreen;
        set { _isFullScreen = value; RaisePropertyChanged(); }
    }

    private readonly UdpVideoReceiver _videoReceiver;
    public TcpJsonClient TcpServer { get; }

    public bool IsConnected => TcpServer.IsConnected;

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
        _videoReceiver.OnFrameReceived += (bmp) => Dispatcher.UIThread.Post(() => CameraImage = bmp);
        _videoReceiver.Start();

        TcpServer = new TcpJsonClient(55555);
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
                        LastDataReceivedTime = DateTime.Now;
                        Console.WriteLine($"[JSON] Updated: {LatestDetections.Count} items at {LastDataReceivedTime:HH:mm:ss.fff}");
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
