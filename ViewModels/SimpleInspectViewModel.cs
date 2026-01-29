using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using avalonia_terminal.Models;
using avalonia_terminal.Services;

namespace avalonia_terminal.ViewModels
{
    public class SimpleInspectViewModel : ViewModelBase
    {
        private readonly MainViewModel _mainViewModel;
        private readonly DatabaseService _dbService;

        // XAMLからの参照用
        public MainViewModel Main => _mainViewModel;

        private Bitmap? _capturedImage;
        public Bitmap? CapturedImage
        {
            get => _capturedImage;
            set 
            { 
                _capturedImage = value; 
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(IsCaptured));
                RaisePropertyChanged(nameof(IsCaptureButtonVisible));
            }
        }

        // XAML互換プロパティ
        public bool IsCaptured => CapturedImage != null;
        public bool IsCaptureButtonVisible => !IsCaptured && !IsCapturing;

        private string _resultText = "";
        public string ResultText
        {
            get => _resultText;
            set 
            { 
                _resultText = value; 
                RaisePropertyChanged(); 
                RaisePropertyChanged(nameof(StatusMessage)); // Alias
            }
        }

        // XAML互換エイリアス
        public string StatusMessage
        {
            get => ResultText;
            set => ResultText = value;
        }

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

        public ICommand CaptureCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand RetryCommand { get; }
        public ICommand BackCommand { get; }
        
        // XAML互換コマンド
        public ICommand RetakeCommand => RetryCommand;

        public SimpleInspectViewModel(MainViewModel mainViewModel)
        {
            _mainViewModel = mainViewModel;
            _dbService = new DatabaseService();

            CaptureCommand = new RelayCommand(async () => await Capture());
            SaveCommand = new RelayCommand(async () => await Save());

            RetryCommand = new RelayCommand(() =>
            {
                CapturedImage = null;
                ResultText = "";
            });

            BackCommand = new RelayCommand(async () =>
            {
                // 戻る時はMJPEG(低負荷)に戻す
                await _mainViewModel.TcpServer.SendJsonAsync(new 
                { 
                    type = "cmd", 
                    command = "change_format", 
                    args = new { format = "MJPEG" } 
                });
                _mainViewModel.Navigate(new HomeViewModel(_mainViewModel));
            });
        }

        private async Task Capture()
        {
            if (IsCapturing) return;
            IsCapturing = true;
            ResultText = "撮影中...";

            try
            {
                // 1. 高画質化 (YUV422) コマンド送信
                await _mainViewModel.TcpServer.SendJsonAsync(new 
                { 
                    type = "cmd", 
                    command = "change_format", 
                    args = new { format = "YUV422" } 
                });

                var validStartTime = DateTime.Now;
                
                // データが来るまで無限に待機する
                while (true)
                {
                    var lastRecv = _mainViewModel.LastDataReceivedTime;
                    if (lastRecv > validStartTime) break; 
                    await Task.Delay(100);
                }

                // 画像安定化
                await Task.Delay(200);

                if (_mainViewModel.CameraImage != null)
                {
                    using (var ms = new MemoryStream())
                    {
                        _mainViewModel.CameraImage.Save(ms);
                        ms.Position = 0;
                        CapturedImage = new Bitmap(ms);
                    }
                    var count = _mainViewModel.LatestDetections.Count;
                    ResultText = $"撮影完了 (検出: {count}個)";
                }
                else
                {
                    ResultText = "画像なし";
                }
            }
            catch (Exception ex)
            {
                ResultText = $"エラー: {ex.Message}";
            }
            finally
            {
                IsCapturing = false;
            }
        }

        private async Task Save()
        {
             if (CapturedImage == null) return;
             
             try 
             {
                 var dateStr = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                 var saveDir = Path.Combine("/home/shikoku-pc/pic", dateStr);
                 
                 if (!Directory.Exists(saveDir)) Directory.CreateDirectory(saveDir);
                 
                 var fileName = "simple_omote.png";
                 var absPath = Path.Combine(saveDir, fileName);
                 
                 CapturedImage.Save(absPath);
                 
                 var record = new InspectionRecord
                 {
                     SaveName = dateStr,
                     SaveAbsolutePath = saveDir,
                     Date = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss")
                 };
                 
                 _dbService.InsertInspection(record);
                 _dbService.CreateDetectionTableAndInsert(dateStr, _mainViewModel.LatestDetections);

                 ResultText = "保存しました";
                 await Task.Delay(1000);
                 
                 // 完了後ホームへ戻る
                 await _mainViewModel.TcpServer.SendJsonAsync(new 
                 { 
                     type = "cmd", 
                     command = "change_format", 
                     args = new { format = "MJPEG" } 
                 });
                 _mainViewModel.Navigate(new HomeViewModel(_mainViewModel));
             }
             catch (Exception ex)
             {
                 ResultText = $"保存エラー: {ex.Message}";
             }
        }
    }
}
