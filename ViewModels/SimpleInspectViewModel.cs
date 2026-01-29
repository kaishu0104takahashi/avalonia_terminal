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

        private Bitmap? _capturedImage;
        public Bitmap? CapturedImage
        {
            get => _capturedImage;
            set { _capturedImage = value; RaisePropertyChanged(); }
        }

        private string _resultText = "";
        public string ResultText
        {
            get => _resultText;
            set { _resultText = value; RaisePropertyChanged(); }
        }

        private bool _isCapturing;
        public bool IsCapturing
        {
            get => _isCapturing;
            set { _isCapturing = value; RaisePropertyChanged(); }
        }

        public ICommand CaptureCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand RetryCommand { get; }
        public ICommand BackCommand { get; }

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
                
                // ★修正: タイムアウト(30回制限)を削除し、データが来るまで無限に待機する
                // これにより「画像取得エラー」は出なくなる
                while (true)
                {
                    var lastRecv = _mainViewModel.LastDataReceivedTime;
                    
                    // コマンド送信後に新しいデータが来ていればOK
                    if (lastRecv > validStartTime)
                    {
                        break; 
                    }

                    // 0.1秒待って再チェック（回数制限なし）
                    await Task.Delay(100);
                }

                // 画像安定化のための少しの待機
                await Task.Delay(200);

                if (_mainViewModel.CameraImage != null)
                {
                    // UI表示用に画像をコピーして確保
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
                    // データは来たが画像がnullの場合（稀なケース）
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
                 
                 // DB登録情報作成
                 var record = new InspectionRecord
                 {
                     SaveName = dateStr,
                     SaveAbsolutePath = saveDir,
                     Date = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss")
                 };
                 
                 // 親テーブルへ保存
                 _dbService.InsertInspection(record);
                 
                 // 検出データ(抵抗器の位置や値)を専用テーブルへ保存
                 _dbService.CreateDetectionTableAndInsert(dateStr, _mainViewModel.LatestDetections);

                 ResultText = "保存しました";
                 await Task.Delay(1000);
                 
                 // 保存完了後はホームへ戻る（画質も戻す）
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
