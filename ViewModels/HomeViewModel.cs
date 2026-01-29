using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace avalonia_terminal.ViewModels;

public class HomeViewModel : ViewModelBase
{
    private readonly MainViewModel _main;
    
    public Bitmap? CameraImage => _main.CameraImage;

    private string _currentDateTime = "";
    public string CurrentDateTime
    {
        get => _currentDateTime;
        set { _currentDateTime = value; RaisePropertyChanged(); }
    }

    public ICommand CaptureCommand { get; }
    public ICommand GalleryCommand { get; }
    public ICommand MeasurementCommand { get; }
    public ICommand TimeSettingCommand { get; }
    public ICommand JsonModeCommand { get; }
    public ICommand StopCommand { get; }            
    public ICommand ShutdownJetsonCommand { get; }  
    public ICommand ShutdownAllCommand { get; }     

    public HomeViewModel(MainViewModel main)
    {
        _main = main;
        _main.PropertyChanged += MainViewModel_PropertyChanged;
        
        UpdateDateTime();
        DispatcherTimer.Run(() => 
        {
            UpdateDateTime();
            return true; 
        }, TimeSpan.FromSeconds(1));

        // 【撮影モード遷移】YUV422を送信 (ここが動いていない可能性があるので再定義)
        CaptureCommand = new RelayCommand(async () => 
        {
            await _main.TcpServer.SendJsonAsync(new 
            { 
                type = "cmd", 
                command = "change_format", 
                args = new { format = "YUV422" } 
            });

            Cleanup();
            _main.Navigate(new SimpleInspectViewModel(_main));
        });

        // 【測定モード遷移】YUV422を送信 (これは動いている)
        MeasurementCommand = new RelayCommand(async () => 
        {
            await _main.TcpServer.SendJsonAsync(new 
            { 
                type = "cmd", 
                command = "change_format", 
                args = new { format = "YUV422" } 
            });
            
            Cleanup();
            _main.Navigate(new MeasurementViewModel(_main));
        });

        GalleryCommand = new RelayCommand(() => 
        {
            Cleanup();
            _main.Navigate(new GalleryViewModel(_main));
        });

        TimeSettingCommand = new RelayCommand(() =>
        {
             Cleanup();
             _main.Navigate(new TimeSettingViewModel(() =>
             {
                 _main.Navigate(new HomeViewModel(_main));
             }));
        });
        
        JsonModeCommand = new RelayCommand(() =>
        {
            Cleanup();
            _main.IsCameraPaused = true; 
            _main.Navigate(new JsonDisplayViewModel(_main));
        });

        StopCommand = new RelayCommand(_main.ShutdownApplication);

        // 【Jetsonシャットダウン】(これも動いている)
        ShutdownJetsonCommand = new RelayCommand(async () =>
        {
            await _main.TcpServer.SendJsonAsync(new 
            { 
                type = "cmd", 
                command = "shutdown", 
                args = new { } 
            });
        });

        // 【全電源オフ】(これも動いている)
        ShutdownAllCommand = new RelayCommand(async () =>
        {
            await _main.TcpServer.SendJsonAsync(new 
            { 
                type = "cmd", 
                command = "shutdown", 
                args = new { } 
            });
            
            await Task.Delay(1000);
            
            try
            {
                var psiLocal = new ProcessStartInfo
                {
                    FileName = "sudo",
                    Arguments = "shutdown -h now",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psiLocal);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Local Shutdown Error: {ex.Message}");
            }
        });
    }

    private void Cleanup()
    {
        _main.PropertyChanged -= MainViewModel_PropertyChanged;
    }

    private void MainViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CameraImage))
        {
            RaisePropertyChanged(nameof(CameraImage));
        }
    }

    private void UpdateDateTime()
    {
        CurrentDateTime = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
    }
}
