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
                DetectionItems.Clear();
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
            _mainViewModel.LatestDetections.Clear();

            // ★重要: コマンド送信前の時刻を記録
            var startTime = DateTime.Now;

            // 推論開始トリガー (YUV422)
            await _mainViewModel.TcpServer.SendJsonAsync(new 
            { 
                type = "cmd", 
                command = "change_format", 
                args = new { format = "YUV422" } 
            });

            Console.WriteLine("Waiting for detection result...");

            // ★修正: 新しいデータが来るまで待機 (最大20秒)
            // 検出数が0の場合でも、受信時刻が更新されれば「受信した」とみなして進む
            bool received = false;
            for (int i = 0; i < 200; i++) // 200 * 100ms = 20秒
            {
                if (_mainViewModel.LastDataReceivedTime > startTime)
                {
                    Console.WriteLine($"Data received! Count: {_mainViewModel.LatestDetections.Count}");
                    received = true;
                    break;
                }
                await Task.Delay(100);
            }

            if (!received)
            {
                Console.WriteLine("Timeout waiting for data.");
                ResultText = "タイムアウト: データ受信なし";
                IsMeasuring = false;
                return;
            }

            // 受信完了後、少しだけ待って画像と同期させる
            await Task.Delay(500);

            if (_mainViewModel.CameraImage != null)
            {
                CapturedImage = _mainViewModel.CameraImage;
                await ProcessDetectionsAsync();
            }
            else
            {
                ResultText = "画像取得エラー";
            }

            IsMeasuring = false;
            HasResult = true;
        }

        private async Task ProcessDetectionsAsync()
        {
            if (CapturedImage == null) return;
            await Task.Run(() => 
            {
                try
                {
                    using var ms = new MemoryStream();
                    CapturedImage.Save(ms);
                    ms.Position = 0;
                    
                    using var src = Cv2.ImDecode(ms.ToArray(), ImreadModes.Color);
                    if (src.Empty()) return;

                    var detections = _mainViewModel.LatestDetections;
                    var newItems = new List<GalleryDetectionItem>();

                    foreach (var det in detections)
                    {
                        if (det.yolo_obb == null) continue;

                        var obb = det.yolo_obb;
                        float angleDeg = obb.rotation_rad * (180f / (float)Math.PI);
                        
                        var center = new Point2f(obb.center_x, obb.center_y);
                        var size = new Size2f(obb.width, obb.height);
                        var rotatedRect = new RotatedRect(center, size, angleDeg);

                        Point2f[] pts = rotatedRect.Points();
                        Point2f tl = new Point2f(), tr = new Point2f(), br = new Point2f(), bl = new Point2f();

                        float min_s = float.MaxValue;
                        float max_s = float.MinValue;
                        float min_diff = float.MaxValue;
                        float max_diff = float.MinValue;

                        for (int i = 0; i < 4; i++)
                        {
                            float s = pts[i].X + pts[i].Y;
                            float diff = pts[i].Y - pts[i].X;

                            if (s < min_s) { min_s = s; tl = pts[i]; }
                            if (s > max_s) { max_s = s; br = pts[i]; }
                            if (diff < min_diff) { min_diff = diff; tr = pts[i]; }
                            if (diff > max_diff) { max_diff = diff; bl = pts[i]; }
                        }

                        float widthA = (float)Math.Sqrt(Math.Pow(br.X - bl.X, 2) + Math.Pow(br.Y - bl.Y, 2));
                        float widthB = (float)Math.Sqrt(Math.Pow(tr.X - tl.X, 2) + Math.Pow(tr.Y - tl.Y, 2));
                        int maxWidth = (int)Math.Max(widthA, widthB);

                        float heightA = (float)Math.Sqrt(Math.Pow(tr.X - br.X, 2) + Math.Pow(tr.Y - br.Y, 2));
                        float heightB = (float)Math.Sqrt(Math.Pow(tl.X - bl.X, 2) + Math.Pow(tl.Y - bl.Y, 2));
                        int maxHeight = (int)Math.Max(heightA, heightB);

                        if (maxWidth <= 0 || maxHeight <= 0) continue;

                        Point2f[] srcPts = { tl, tr, br, bl };
                        Point2f[] dstPts = {
                            new Point2f(0, 0),
                            new Point2f(maxWidth - 1, 0),
                            new Point2f(maxWidth - 1, maxHeight - 1),
                            new Point2f(0, maxHeight - 1)
                        };

                        using var perspectiveM = Cv2.GetPerspectiveTransform(srcPts, dstPts);
                        using var cropped = new Mat();
                        Cv2.WarpPerspective(src, cropped, perspectiveM, new OpenCvSharp.Size(maxWidth, maxHeight));

                        if (cropped.Rows > cropped.Cols)
                        {
                            Cv2.Rotate(cropped, cropped, RotateFlags.Rotate90Clockwise);
                        }

                        var bmp = MatToBitmap(cropped);
                        newItems.Add(new GalleryDetectionItem(det.detection_id, det.value ?? "", bmp));
                    }

                    Dispatcher.UIThread.Post(() =>
                    {
                        foreach (var item in newItems) DetectionItems.Add(item);
                        
                        if (DetectionItems.Count == 0) ResultText = "検出なし";
                        else ResultText = "測定完了";
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
