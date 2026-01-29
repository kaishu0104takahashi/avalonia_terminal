using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Buffers.Binary;

namespace avalonia_terminal.Services;

/// <summary>
/// AI推論結果(JSON)の受信および制御コマンド送信を行うTCPサーバークラス。
/// カメラデバイス（クライアント）からの接続を待ち受けます。
/// </summary>
public class TcpJsonServer
{
    private TcpListener? _listener;
    private TcpClient? _currentClient;
    private bool _isRunning;
    private readonly int _port;

    /// <summary>
    /// JSONデータ受信時に発生するイベント。
    /// </summary>
    public event Action<string>? OnJsonReceived;
    
    /// <summary>
    /// 接続状態やエラーメッセージの通知イベント。
    /// </summary>
    public event Action<string>? OnStatusChanged;
    
    /// <summary>
    /// クライアントとの接続状態を取得します。
    /// </summary>
    public bool IsConnected => _currentClient != null && _currentClient.Connected;

    public TcpJsonServer(int port)
    {
        _port = port;
    }

    /// <summary>
    /// サーバーを開始し、接続待機ループを実行します。
    /// </summary>
    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;
        Task.Run(ReceiveLoop);
    }

    /// <summary>
    /// サーバーを停止し、接続を切断します。
    /// </summary>
    public void Stop()
    {
        _isRunning = false;
        try { _listener?.Stop(); } catch { }
        try { _currentClient?.Close(); } catch { }
        _currentClient = null;
    }

    /// <summary>
    /// 接続中のクライアントへJSONデータを送信します。
    /// データ構造: [サイズ(BigEndian 4byte)] + [JSON文字列(UTF8)]
    /// </summary>
    /// <param name="data">送信するオブジェクト（自動的にシリアライズされます）</param>
    public async Task SendJsonAsync(object data)
    {
        if (_currentClient == null || !_currentClient.Connected) return;
        try
        {
            string jsonString = JsonSerializer.Serialize(data);
            byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonString);
            
            // ヘッダー作成 (BigEndian 4byte, ボディサイズ)
            byte[] headerBytes = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(headerBytes, bodyBytes.Length);

            var stream = _currentClient.GetStream();
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length);
            await stream.WriteAsync(bodyBytes, 0, bodyBytes.Length);
        }
        catch (Exception ex)
        {
            OnStatusChanged?.Invoke($"送信エラー: {ex.Message}");
            Console.WriteLine($"Send Error: {ex.Message}");
        }
    }

    /// <summary>
    /// クライアント接続待ちおよびデータ受信ループ。
    /// </summary>
    private async Task ReceiveLoop()
    {
        try
        {
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
            OnStatusChanged?.Invoke($"ポート {_port} で待機中...");
            Console.WriteLine($"TCP Server started on port {_port}");

            while (_isRunning)
            {
                try
                {
                    // クライアント接続待機
                    var client = await _listener.AcceptTcpClientAsync();
                    _currentClient?.Close();
                    _currentClient = client;
                    OnStatusChanged?.Invoke($"接続完了: {client.Client.RemoteEndPoint}");
                    Console.WriteLine($"Client connected: {client.Client.RemoteEndPoint}");
                    
                    using var stream = client.GetStream();
                    while (_isRunning && client.Connected)
                    {
                        // 1. ヘッダー受信 (データ長 4byte)
                        byte[] headerBuffer = new byte[4];
                        int bytesRead = await ReadExactAsync(stream, headerBuffer, 4);
                        if (bytesRead == 0) break; 

                        // Big Endianとしてサイズを解釈
                        int bodyLength = BinaryPrimitives.ReadInt32BigEndian(headerBuffer);
                        if (bodyLength <= 0) continue;
                        if (bodyLength > 10 * 1024 * 1024) throw new Exception("Data too large");

                        // 2. ボディ受信 (JSON文字列)
                        byte[] bodyBuffer = new byte[bodyLength];
                        await ReadExactAsync(stream, bodyBuffer, bodyLength);

                        string jsonStr = Encoding.UTF8.GetString(bodyBuffer);
                        OnJsonReceived?.Invoke(jsonStr);
                    }
                }
                catch (Exception ex)
                {
                    if(_isRunning) OnStatusChanged?.Invoke($"通信エラー: {ex.Message}");
                    Console.WriteLine($"Receive Error: {ex.Message}");
                }
                finally
                {
                    _currentClient = null;
                    if(_isRunning) OnStatusChanged?.Invoke("待機中...");
                }
            }
        }
        catch (Exception ex)
        {
            OnStatusChanged?.Invoke($"起動エラー: {ex.Message}");
            Console.WriteLine($"Listener Error: {ex.Message}");
        }
        finally
        {
            _listener?.Stop();
        }
    }

    /// <summary>
    /// 指定されたバイト数だけストリームから確実に読み込みます。
    /// </summary>
    private async Task<int> ReadExactAsync(NetworkStream stream, byte[] buffer, int length)
    {
        int totalRead = 0;
        while (totalRead < length)
        {
            int read = await stream.ReadAsync(buffer, totalRead, length - totalRead);
            if (read == 0) return 0;
            totalRead += read;
        }
        return totalRead;
    }
}
