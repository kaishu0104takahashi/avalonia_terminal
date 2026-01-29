using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace avalonia_terminal.Services;

public class TcpJsonClient
{
    private TcpListener? _listener;
    private TcpClient? _currentClient; // 送信用に接続中のクライアントを保持
    private bool _isRunning;
    private readonly int _port;

    // イベント
    public event Action<string>? OnJsonReceived;
    public event Action<string>? OnStatusChanged;

    public TcpJsonClient(int port)
    {
        _port = port;
    }

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;
        Task.Run(ReceiveLoop);
    }

    public void Stop()
    {
        _isRunning = false;
        try { _listener?.Stop(); } catch { }
        try { _currentClient?.Close(); } catch { }
        _currentClient = null;
    }

    /// <summary>
    /// 【追加】接続中のクライアントへJSONを送信するメソッド
    /// </summary>
    public async Task SendJsonAsync(object data)
    {
        // クライアントが接続されていない場合は送れない
        if (_currentClient == null || !_currentClient.Connected)
        {
            return;
        }

        try
        {
            // 1. データをJSON文字列化 -> UTF8バイト配列へ
            string jsonString = JsonSerializer.Serialize(data);
            byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonString);
            
            // 2. ヘッダー作成 (4バイト, BigEndian)
            // C++側の htonl に対応させるため、ホストオーダーからネットワークオーダー(BigEndian)へ変換
            int bodyLength = bodyBytes.Length;
            int networkOrderLength = IPAddress.HostToNetworkOrder(bodyLength);
            byte[] headerBytes = BitConverter.GetBytes(networkOrderLength);

            var stream = _currentClient.GetStream();
            
            // 3. 送信 (ヘッダー -> ボディ)
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length);
            await stream.WriteAsync(bodyBytes, 0, bodyBytes.Length);
        }
        catch (Exception ex)
        {
            OnStatusChanged?.Invoke($"送信エラー: {ex.Message}");
        }
    }

    private async Task ReceiveLoop()
    {
        try
        {
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
            OnStatusChanged?.Invoke($"ポート {_port} で待機中...");

            while (_isRunning)
            {
                try
                {
                    // 接続待ち
                    var client = await _listener.AcceptTcpClientAsync();
                    
                    // 【重要】送信メソッドから使えるように保持する
                    _currentClient?.Close();
                    _currentClient = client;

                    OnStatusChanged?.Invoke($"接続完了: {client.Client.RemoteEndPoint}");
                    
                    using var stream = client.GetStream();

                    while (_isRunning && client.Connected)
                    {
                        // 1. ヘッダー(4バイト)を受信
                        byte[] headerBuffer = new byte[4];
                        int bytesRead = await ReadExactAsync(stream, headerBuffer, 4);
                        
                        if (bytesRead == 0) break; // 切断された

                        // ネットワークバイトオーダー(Big Endian)からホストバイトオーダーへ変換
                        if (BitConverter.IsLittleEndian)
                        {
                            Array.Reverse(headerBuffer);
                        }
                        int bodyLength = BitConverter.ToInt32(headerBuffer, 0);

                        // 2. ボディ(JSON文字列)を受信
                        if (bodyLength > 0)
                        {
                            if (bodyLength > 10 * 1024 * 1024) throw new Exception("データサイズが大きすぎます");

                            byte[] bodyBuffer = new byte[bodyLength];
                            await ReadExactAsync(stream, bodyBuffer, bodyLength);

                            string jsonStr = Encoding.UTF8.GetString(bodyBuffer);
                            OnJsonReceived?.Invoke(jsonStr);
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (_isRunning) OnStatusChanged?.Invoke($"通信エラー: {ex.Message}");
                }
                finally
                {
                    _currentClient = null; // 切断されたらnullに戻す
                    if (_isRunning) OnStatusChanged?.Invoke($"待機中...");
                }
            }
        }
        catch (Exception ex)
        {
            OnStatusChanged?.Invoke($"起動エラー: {ex.Message}");
        }
        finally
        {
            _listener?.Stop();
        }
    }

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
