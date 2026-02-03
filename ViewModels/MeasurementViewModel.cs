using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using avalonia_terminal.Models;
using OpenCvSharp;

namespace avalonia_terminal.ViewModels
{
    public class MeasurementViewModel : ViewModelBase
    {
        private readonly MainViewModel _mainViewModel;
        public Bitmap? LiveImage => _mainViewModel.CameraImage;

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

        private bool _isMeasuring;
        public bool IsMeasuring
        {
            get => _isMeasuring;
            set { _isMeasuring = value; RaisePropertyChanged(); }
        }

        private bool _hasResult;
        public bool HasResult
        {
            get => _hasResult;
            set { _hasResult = value; RaisePropertyChanged(); }
        }

        // 検出結果リスト (ギャラリーと同じ型を使用)
        public ObservableCollection<GalleryDetectionItem> DetectionItems { get; } = new();

        public ICommand ExecuteMeasurementCommand { get; }
        public ICommand RetryCommand { get; }
        public ICommand BackCommand { get; }

        public MeasurementViewModel(MainViewModel mainViewModel)
        {
            _mainViewModel = mainViewModel;
            _mainViewModel.PropertyChanged += MainViewModel_PropertyChanged;

            ExecuteMeasurementCommand = new RelayCommand<object>(async _ => await ExecuteMeasurement());
            RetryCommand = new RelayCommand(() =>
            {
                CapturedImage = null;
                HasResult = false;
                ResultText = "";
                DetectionItems.Clear(); // リストもクリア
            });
            BackCommand = new RelayCommand(async () =>
            {
                await _mainViewModel.TcpServer.SendJsonAsync(new 
                { 
                    type = "cmd", 
                    command = "change_format", 
                    args = new { format = "MJPEG" } 
                });

                _mainViewModel.PropertyChanged -= MainViewModel_PropertyChanged;
                _mainViewModel.Navigate(new HomeViewModel(_mainViewModel));
            });
        }

        private void MainViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.CameraImage))
            {
                RaisePropertyChanged(nameof(LiveImage));
            }
        }

        private async Task ExecuteMeasurement()
        {
            if (IsMeasuring) return;
            IsMeasuring = true;
            HasResult = false;
            DetectionItems.Clear();
            _mainViewModel.LatestDetections.Clear(); // 古いデータを消しておく

            // 1. 高画質化コマンド
            await _mainViewModel.TcpServer.SendJsonAsync(new 
            { 
                type = "cmd", 
                command = "change_format", 
                args = new { format = "YUV422" } 
            });

            // 2. 切り替えとデータ受信待ち (500ms)
            // この間にカメラから画像とJSON(type:data)が送られてくるはず
            await Task.Delay(500);

            if (_mainViewModel.CameraImage != null)
            {
                CapturedImage = _mainViewModel.CameraImage;
                
                // 3. 切り抜き処理を実行
                await ProcessDetectionsAsync();
            }

            ResultText = "測定完了"; // 特定の値ではなく完了メッセージに変更
            IsMeasuring = false;
            HasResult = true;
        }

        private async Task ProcessDetectionsAsync()
        {
            if (CapturedImage == null) return;
            
            // UIスレッドをブロックしないように別スレッドで処理
            await Task.Run(() => 
            {
                try
                {
                    // Avalonia Bitmap -> MemoryStream -> OpenCV Mat
                    using var ms = new MemoryStream();
                    CapturedImage.Save(ms);
                    ms.Position = 0;
                    
                    // バイト配列として読み込んでデコード
                    using var src = Cv2.ImDecode(ms.ToArray(), ImreadModes.Color);
                    if (src.Empty()) return;

                    var detections = _mainViewModel.LatestDetections;
                    var newItems = new List<GalleryDetectionItem>();

                    foreach (var det in detections)
                    {
                        if (det.YoloObb == null) continue;

                        // 回転矩形の計算
                        var center = new Point2f(det.YoloObb.CenterX, det.YoloObb.CenterY);
                        var size = new Size2f(det.YoloObb.Width, det.YoloObb.Height);
                        float angleDeg = det.YoloObb.RotationRad * 180f / (float)Math.PI;

                        var rotatedRect = new RotatedRect(center, size, angleDeg);
                        var rectSize = rotatedRect.Size;

                        if (rectSize.Width <= 0 || rectSize.Height <= 0) continue;

                        // 切り抜き
                        using var cropped = new Mat();
                        Cv2.GetRectSubPix(src, new OpenCvSharp.Size((int)rectSize.Width, (int)rectSize.Height), rotatedRect.Center, cropped);

                        // Mat -> Avalonia Bitmap
                        var bmp = MatToBitmap(cropped);
                        newItems.Add(new GalleryDetectionItem(det.DetectionId, det.Value ?? "", bmp));
                    }

                    // UI更新
                    Dispatcher.UIThread.Post(() =>
                    {
                        foreach (var item in newItems)
                        {
                            DetectionItems.Add(item);
                        }
                        
                        if (DetectionItems.Count == 0)
                        {
                            ResultText = "検出なし";
                        }
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Crop Error: {ex.Message}");
                }
            });
        }

        private Bitmap? MatToBitmap(Mat mat)
        {
            try
            {
                using var ms = mat.ToMemoryStream(".png");
                ms.Position = 0;
                return new Bitmap(ms);
            }
            catch { return null; }
        }
    }
}
