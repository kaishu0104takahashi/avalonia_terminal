using System;
using System.Diagnostics;
using System.Windows.Input;
using avalonia_terminal.Models;

namespace avalonia_terminal.ViewModels
{
    public class TimeSettingViewModel : ViewModelBase
    {
        private int _year;
        public int Year 
        { 
            get => _year; 
            set 
            { 
                _year = value; 
                RaisePropertyChanged(); 
                AdjustDayLimit(); // 年が変わったらうるう年チェック
            } 
        }

        private int _month;
        public int Month 
        { 
            get => _month; 
            set 
            { 
                _month = value; 
                RaisePropertyChanged(); 
                AdjustDayLimit(); // 月が変わったら日数チェック
            } 
        }

        private int _day;
        public int Day 
        { 
            get => _day; 
            set { _day = value; RaisePropertyChanged(); } 
        }

        private int _hour;
        public int Hour { get => _hour; set { _hour = value; RaisePropertyChanged(); } }

        private int _minute;
        public int Minute { get => _minute; set { _minute = value; RaisePropertyChanged(); } }

        private int _second;
        public int Second { get => _second; set { _second = value; RaisePropertyChanged(); } }

        public ICommand UpYearCommand { get; }
        public ICommand DownYearCommand { get; }
        public ICommand UpMonthCommand { get; }
        public ICommand DownMonthCommand { get; }
        public ICommand UpDayCommand { get; }
        public ICommand DownDayCommand { get; }
        public ICommand UpHourCommand { get; }
        public ICommand DownHourCommand { get; }
        public ICommand UpMinuteCommand { get; }
        public ICommand DownMinuteCommand { get; }
        public ICommand UpSecondCommand { get; }
        public ICommand DownSecondCommand { get; }

        public ICommand SetTimeCommand { get; }
        public ICommand SaveCommand => SetTimeCommand;
        public ICommand CancelCommand { get; }

        private readonly Action _onComplete;

        public TimeSettingViewModel(Action onComplete)
        {
            _onComplete = onComplete;
            
            var now = DateTime.Now;
            _year = now.Year;   // 初期化時はプロパティ経由せず直接セット(Adjust回避)
            _month = now.Month;
            _day = now.Day;
            Hour = now.Hour;
            Minute = now.Minute;
            Second = now.Second;

            // コマンド実装
            UpYearCommand = new RelayCommand(() => Year++);
            DownYearCommand = new RelayCommand(() => Year--);
            
            UpMonthCommand = new RelayCommand(() => { if(Month < 12) Month++; else Month = 1; });
            DownMonthCommand = new RelayCommand(() => { if(Month > 1) Month--; else Month = 12; });

            // ★修正: その月の日数に応じて上限を変える
            UpDayCommand = new RelayCommand(() => 
            { 
                int maxDays = DateTime.DaysInMonth(Year, Month);
                if(Day < maxDays) Day++; else Day = 1; 
            });
            DownDayCommand = new RelayCommand(() => 
            { 
                int maxDays = DateTime.DaysInMonth(Year, Month);
                if(Day > 1) Day--; else Day = maxDays; 
            });

            UpHourCommand = new RelayCommand(() => { if(Hour < 23) Hour++; else Hour = 0; });
            DownHourCommand = new RelayCommand(() => { if(Hour > 0) Hour--; else Hour = 23; });

            UpMinuteCommand = new RelayCommand(() => { if(Minute < 59) Minute++; else Minute = 0; });
            DownMinuteCommand = new RelayCommand(() => { if(Minute > 0) Minute--; else Minute = 59; });

            UpSecondCommand = new RelayCommand(() => { if(Second < 59) Second++; else Second = 0; });
            DownSecondCommand = new RelayCommand(() => { if(Second > 0) Second--; else Second = 59; });

            SetTimeCommand = new RelayCommand(OnSetTime);
            CancelCommand = new RelayCommand(() => _onComplete?.Invoke());
        }

        // 年や月が変わった時に、日数がはみ出していたら修正するメソッド
        // 例: 1月31日から「2月」に変えた時、31日は存在しないので28日(または29日)に戻す
        private void AdjustDayLimit()
        {
            try 
            {
                int maxDays = DateTime.DaysInMonth(Year, Month);
                if (Day > maxDays)
                {
                    Day = maxDays;
                }
            }
            catch 
            {
                // 万が一異常値が入っていた場合の安全策
                Day = 1;
            }
        }

        private void OnSetTime()
        {
            try
            {
                var dt = new DateTime(Year, Month, Day, Hour, Minute, Second);
                var timeStr = dt.ToString("yyyy-MM-dd HH:mm:ss");

                // 1. システム時刻設定
                Process.Start(new ProcessStartInfo
                {
                    FileName = "sudo",
                    Arguments = $"date -s \"{timeStr}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                })?.WaitForExit();

                // 2. 時刻情報強制保存
                Process.Start(new ProcessStartInfo
                {
                    FileName = "sudo",
                    Arguments = "fake-hwclock save",
                    CreateNoWindow = true,
                    UseShellExecute = false
                })?.WaitForExit();

                Console.WriteLine($"Time set: {timeStr}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Time Set Error: {ex.Message}");
            }
            finally
            {
                _onComplete?.Invoke();
            }
        }
    }
}
