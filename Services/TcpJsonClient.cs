using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Buffers.Binary;

namespace avalonia_terminal.Services;

public class TcpJsonClient
{
    private TcpListener? _listener;
    private TcpClient? _currentClient;
    private bool _isRunning;
    private readonly int _port;

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

    public async Task SendJsonAsync(object data)
    {
        if (_currentClient == null || !_currentClient.Connected) return;

        try
        {
            string jsonString = JsonSerializer.Serialize(data);
            byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonString);
            
            // ヘッダ作成 (BigEndian 4byte)
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
                    var client = await _listener.AcceptTcpClientAsync();
                    _currentClient?.Close();
                    _currentClient = client;
                    OnStatusChanged?.Invoke($"接続完了: {client.Client.RemoteEndPoint}");
                    Console.WriteLine($"Client connected: {client.Client.RemoteEndPoint}");
                    
                    using var stream = client.GetStream();
                    while (_isRunning && client.Connected)
                    {
                        // 1. ヘッダー受信 (4byte)
                        byte[] headerBuffer = new byte[4];
                        int bytesRead = await ReadExactAsync(stream, headerBuffer, 4);
                        if (bytesRead == 0) break; // 切断

                        // Big Endianとしてサイズを解釈
                        int bodyLength = BinaryPrimitives.ReadInt32BigEndian(headerBuffer);
                        
                        if (bodyLength <= 0) continue;
                        if (bodyLength > 10 * 1024 * 1024) throw new Exception("Data too large");

                        // 2. ボディ受信
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
