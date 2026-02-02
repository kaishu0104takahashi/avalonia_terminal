using System;
using System.Diagnostics;
using System.Windows.Input;

namespace avalonia_terminal.ViewModels
{
    public class TimeSettingViewModel : ViewModelBase
    {
        private readonly Action _closeAction;

        // --- プロパティ ---
        private int _year;
        public int Year 
        { 
            get => _year; 
            set 
            { 
                _year = value; 
                ValidateDay(); // 年が変わったらうるう年チェックなど
                RaisePropertyChanged(); 
            } 
        }

        private int _month;
        public int Month 
        { 
            get => _month; 
            set 
            { 
                _month = value; 
                ValidateDay(); // 月が変わったら日数の最大値チェック
                RaisePropertyChanged(); 
            } 
        }

        private int _day;
        public int Day 
        { 
            get => _day; 
            set { _day = value; RaisePropertyChanged(); } 
        }

        private int _hour;
        public int Hour 
        { 
            get => _hour; 
            set { _hour = value; RaisePropertyChanged(); } 
        }

        private int _minute;
        public int Minute 
        { 
            get => _minute; 
            set { _minute = value; RaisePropertyChanged(); } 
        }

        private int _second;
        public int Second 
        { 
            get => _second; 
            set { _second = value; RaisePropertyChanged(); } 
        }

        // --- コマンド ---
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

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        public TimeSettingViewModel(Action closeAction)
        {
            _closeAction = closeAction;

            // 現在時刻で初期化
            var now = DateTime.Now;
            Year = now.Year;
            Month = now.Month;
            Day = now.Day;
            Hour = now.Hour;
            Minute = now.Minute;
            Second = now.Second;

            // コマンド定義
            UpYearCommand = new RelayCommand(() => Year++);
            DownYearCommand = new RelayCommand(() => Year--);

            UpMonthCommand = new RelayCommand(() => { if (Month < 12) Month++; else Month = 1; });
            DownMonthCommand = new RelayCommand(() => { if (Month > 1) Month--; else Month = 12; });

            // 日付変更ロジック：その月の最大日数を考慮
            UpDayCommand = new RelayCommand(() => 
            { 
                int maxDays = DateTime.DaysInMonth(Year, Month);
                if (Day < maxDays) Day++; else Day = 1; 
            });
            DownDayCommand = new RelayCommand(() => 
            { 
                int maxDays = DateTime.DaysInMonth(Year, Month);
                if (Day > 1) Day--; else Day = maxDays; 
            });

            UpHourCommand = new RelayCommand(() => { if (Hour < 23) Hour++; else Hour = 0; });
            DownHourCommand = new RelayCommand(() => { if (Hour > 0) Hour--; else Hour = 23; });

            UpMinuteCommand = new RelayCommand(() => { if (Minute < 59) Minute++; else Minute = 0; });
            DownMinuteCommand = new RelayCommand(() => { if (Minute > 0) Minute--; else Minute = 59; });

            UpSecondCommand = new RelayCommand(() => { if (Second < 59) Second++; else Second = 0; });
            DownSecondCommand = new RelayCommand(() => { if (Second > 0) Second--; else Second = 59; });

            SaveCommand = new RelayCommand(ExecuteSave);
            CancelCommand = new RelayCommand(() => _closeAction?.Invoke());
        }

        /// <summary>
        /// 現在の年月に対して、日が範囲外なら修正する
        /// </summary>
        private void ValidateDay()
        {
            try
            {
                // 年や月が極端な値のときはDateTime.DaysInMonthがエラーになるのを防ぐガード
                if (Year < 1 || Month < 1 || Month > 12) return;

                int maxDays = DateTime.DaysInMonth(Year, Month);
                if (Day > maxDays)
                {
                    Day = maxDays;
                }
            }
            catch
            {
                // 無視
            }
        }

        private void ExecuteSave()
        {
            // Linuxのdateコマンドを実行して時刻を設定
            // フォーマット: "yyyy-MM-dd HH:mm:ss"
            string dateStr = $"{Year:D4}-{Month:D2}-{Day:D2} {Hour:D2}:{Minute:D2}:{Second:D2}";

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "sudo",
                    Arguments = $"date -s \"{dateStr}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi)?.WaitForExit();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Date Set Error: {ex.Message}");
            }

            _closeAction?.Invoke();
        }
    }
}
