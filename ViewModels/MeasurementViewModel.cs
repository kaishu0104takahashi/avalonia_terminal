using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media.Imaging;

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

            // 【修正】測定開始時に画質変更コマンド送信
            await _mainViewModel.TcpServer.SendJsonAsync(new 
            { 
                type = "cmd", 
                command = "change_format", 
                args = new { format = "YUV422" } 
            });

            // 切り替え待ち含めて少し待機 (300ms -> 500msへ延長)
            await Task.Delay(500);

            if (_mainViewModel.CameraImage != null)
            {
                CapturedImage = _mainViewModel.CameraImage;
            }

            ResultText = "1kΩ";
            IsMeasuring = false;
            HasResult = true;
        }
    }
}
