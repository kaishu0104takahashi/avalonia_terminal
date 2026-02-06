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

        private bool _isCancelled;
        public ObservableCollection<GalleryDetectionItem> DetectionItems { get; } = new();

        public ICommand ExecuteMeasurementCommand { get; }
        public ICommand RetryCommand { get; }
        public ICommand BackCommand { get; }
        public ICommand CancelMeasurementCommand { get; }

        public MeasurementViewModel(MainViewModel mainViewModel)
        {
            _mainViewModel = mainViewModel;
            _mainViewModel.PropertyChanged += MainViewModel_PropertyChanged;

            ExecuteMeasurementCommand = new RelayCommand<object>(async _ => await ExecuteMeasurement());
            CancelMeasurementCommand = new RelayCommand(() => _isCancelled = true);
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
            if (!_mainViewModel.IsConnected)
            {
                ResultText = "通信未接続";
                return;
            }

            IsMeasuring = true;
            HasResult = false;
            _isCancelled = false;
            DetectionItems.Clear();
            _mainViewModel.LatestDetections.Clear();
            ResultText = "測定開始...";

            // 1. MJPEGリセット
            await _mainViewModel.TcpServer.SendJsonAsync(new 
            { 
                type = "cmd", 
                command = "change_format", 
                args = new { format = "MJPEG" } 
            });
            
            await Task.Delay(200);

            // 2. YUV422 (高画質) に切り替え
            await _mainViewModel.TcpServer.SendJsonAsync(new 
            { 
                type = "cmd", 
                command = "change_format", 
                args = new { format = "YUV422" } 
            });

            Console.WriteLine("Sent YUV422 command. Waiting for data...");
            ResultText = "データ受信待機中...";

            // 古いデータを流すための待機
            await Task.Delay(2000);

            var validStartTime = DateTime.Now;
            bool received = false;
            
            // キャンセルされるまで待つ
            while (!_isCancelled)
            {
                var lastRecv = _mainViewModel.LastDataReceivedTime;

                // まだ新しいデータが来ていないなら待つ
                if (lastRecv <= validStartTime)
                {
                    await Task.Delay(100);
                    continue;
                }

                // データが空でなければ採用
                if (_mainViewModel.LatestDetections.Count >= 0)
                {
                    received = true;
                    break;
                }
                
                await Task.Delay(100);
            }

            if (!received)
            {
                await CancelProcess();
                return;
            }

            // 画像確定待ち
            await Task.Delay(200);

            if (_mainViewModel.CameraImage != null)
            {
                try {
                    using (var ms = new MemoryStream())
                    {
                        _mainViewModel.CameraImage.Save(ms);
                        ms.Position = 0;
                        CapturedImage = new Bitmap(ms);
                    }
                }
                catch {
                    CapturedImage = _mainViewModel.CameraImage;
                }
                
                await ProcessDetectionsAsync();
            }
            else
            {
                ResultText = "画像取得エラー";
                IsMeasuring = false;
            }
        }

        private async Task CancelProcess()
        {
            Console.WriteLine("Measurement Cancelled.");
            ResultText = "中断";
            IsMeasuring = false;
            await _mainViewModel.TcpServer.SendJsonAsync(new 
            { 
                type = "cmd", 
                command = "change_format", 
                args = new { format = "MJPEG" } 
            });
        }

        private async Task ProcessDetectionsAsync()
        {
            if (CapturedImage == null) return;

            string tempPath = Path.GetTempFileName() + ".png";

            await Task.Run(() => 
            {
                try
                {
                    CapturedImage.Save(tempPath);
                    
                    using var src = Cv2.ImRead(tempPath);
                    if (src.Empty()) return;
                    
                    // ★追加: 画像サイズ取得
                    float imgW = src.Width;
                    float imgH = src.Height;

                    var detections = new List<detection_data>(_mainViewModel.LatestDetections);
                    var newItems = new List<GalleryDetectionItem>();

                    Console.WriteLine($"Processing {detections.Count} detections...");

                    foreach (var det in detections)
                    {
                        // ★修正: 個別のtry-catchで囲む (1つ失敗しても他は続行する)
                        try 
                        {
                            if (det.yolo_obb == null) continue;
                            var obb = det.yolo_obb;
                            
                            float angleDeg = obb.rotation_rad * (180f / (float)Math.PI);
                            
                            // ★修正: 座標が画像範囲外にならないように補正 (Clamp)
                            float cx = Math.Clamp(obb.center_x, 0, imgW);
                            float cy = Math.Clamp(obb.center_y, 0, imgH);
                            float w = Math.Min(obb.width, imgW);
                            float h = Math.Min(obb.height, imgH);

                            var center = new Point2f(cx, cy);
                            var size = new Size2f(w, h);
                            var rotatedRect = new RotatedRect(center, size, angleDeg);

                            Point2f[] pts = rotatedRect.Points();
                            Point2f tl = new Point2f(), tr = new Point2f(), br = new Point2f(), bl = new Point2f();
                            float min_s = float.MaxValue, max_s = float.MinValue;
                            float min_diff = float.MaxValue, max_diff = float.MinValue;

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
                                Cv2.Rotate(cropped, cropped, RotateFlags.Rotate90Clockwise);

                            var bmp = MatToBitmap(cropped);
                            if (bmp != null)
                            {
                                newItems.Add(new GalleryDetectionItem(det.detection_id, det.value ?? "", bmp));
                            }
                        }
                        catch (Exception innerEx)
                        {
                            // 個別のエラーはログに出すが、ループは止めない
                            Console.WriteLine($"Item Error: {innerEx.Message}");
                        }
                    }

                    // UI更新 (成功した分だけ表示)
                    Dispatcher.UIThread.Post(() =>
                    {
                        foreach (var item in newItems) DetectionItems.Add(item);
                        
                        if (DetectionItems.Count == 0) ResultText = "検出なし (0件)";
                        else ResultText = $"測定完了 ({DetectionItems.Count}件)";
                        
                        IsMeasuring = false;
                        HasResult = true;
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.UIThread.Post(() => {
                        ResultText = $"エラー: {ex.Message}";
                        IsMeasuring = false;
                    });
                }
                finally
                {
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
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
