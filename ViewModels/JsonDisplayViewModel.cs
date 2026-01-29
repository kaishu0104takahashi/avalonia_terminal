using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Threading;
using avalonia_terminal.Services;

namespace avalonia_terminal.ViewModels;

public class JsonDisplayViewModel : ViewModelBase
{
    private readonly MainViewModel _main;

    // 受信ログを表示するためのコレクション
    public ObservableCollection<string> JsonLogs { get; } = new();

    private string _statusText = "モニタリング中...";
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; RaisePropertyChanged(); }
    }

    private string _latestJson = "";
    public string LatestJson
    {
        get => _latestJson;
        set { _latestJson = value; RaisePropertyChanged(); }
    }

    public ICommand BackCommand { get; }
    public ICommand ClearCommand { get; }

    public JsonDisplayViewModel(MainViewModel main)
    {
        _main = main;

        // 戻るボタン
        BackCommand = new RelayCommand(() =>
        {
            Cleanup(); // イベント購読解除
            _main.IsCameraPaused = false; // カメラ再開
            _main.Navigate(new HomeViewModel(_main));
        });

        // クリアボタン
        ClearCommand = new RelayCommand(() =>
        {
            JsonLogs.Clear();
            LatestJson = "";
        });

        // 【変更】自分でサーバーを立てず、MainViewModelのサーバーのイベントを購読する
        _main.TcpServer.OnStatusChanged += OnStatusChanged;
        _main.TcpServer.OnJsonReceived += OnJsonReceived;
    }

    private void Cleanup()
    {
        // 画面を抜けるときは購読を解除する（サーバー自体は止めない）
        _main.TcpServer.OnStatusChanged -= OnStatusChanged;
        _main.TcpServer.OnJsonReceived -= OnJsonReceived;
    }

    private void OnStatusChanged(string msg)
    {
        Dispatcher.UIThread.Post(() => StatusText = msg);
    }

    private void OnJsonReceived(string json)
    {
        Dispatcher.UIThread.Post(() =>
        {
            LatestJson = json;

            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            JsonLogs.Insert(0, $"[{timestamp}] {json}");
            
            if (JsonLogs.Count > 100) JsonLogs.RemoveAt(JsonLogs.Count - 1);
        });
    }
}
