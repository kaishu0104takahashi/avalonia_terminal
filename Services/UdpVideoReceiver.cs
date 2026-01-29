using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace avalonia_terminal.Services;

static class Constants
{
    // 再構築用バッファサイズ (4MB)
    public const int ALLOC_BYTE_SIZE = 4 * 1024 * 1024;
    // ソケットの受信バッファサイズ (8MB - カーネルレベルでのドロップを防ぐため大きく取る)
    public const int KERNEL_S_UDP_BUFFER_SIZE = 8 * 1024 * 1024;
    // ヘッダーサイズ (Identifier:4 + Index:2 + Total:2 + Size:2 + W:2 + H:2 + C:1)
    public const int HEADER_SIZE = 15;
}

public class UdpVideoReceiver
{
    private Socket? _socket;
    private bool _is_running;
    private readonly int _port;
    // 分割されたパケットを結合するためのバッファ
    private readonly byte[] _reassembly_buffer = new byte[Constants.ALLOC_BYTE_SIZE];
    // 現在のフレームの受信済みデータ長
    private int _current_payload_length = 0;
    // レンダリング中フラグ (Interlockedで使用)
    private int _is_rendering = 0;
    
    // 画像を受信したときにViewModelへ通知するアクション
    public Action<Bitmap?>? OnFrameReceived;

    public bool IsPaused { get; set; } = false;

    public UdpVideoReceiver(int port)
    {
        _port = port;
    }

    public void Start()
    {
        if (_is_running)
        {
            return;
        }
        _is_running = true;
        Task.Run(Receiver_loop);
    }

    private async Task Receiver_loop()
    {
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _socket.Bind(new IPEndPoint(IPAddress.Any, _port));
        _socket.ReceiveBufferSize = Constants.KERNEL_S_UDP_BUFFER_SIZE;
        
        byte[] receive_buffer = ArrayPool<byte>.Shared.Rent(4096);
        
        try
        {
            EndPoint remote_endpoint = new IPEndPoint(IPAddress.Any, 0);
            while (_is_running)
            {
                // 非同期でデータ受信
                var result = await _socket.ReceiveFromAsync(new Memory<byte>(receive_buffer), SocketFlags.None, remote_endpoint);
                
                if (IsPaused) continue;

                int bytes_read = result.ReceivedBytes;

                // ヘッダーサイズ未満のデータは無視
                if (bytes_read < Constants.HEADER_SIZE)
                {
                    continue;
                }

                // ヘッダーの解析 (Little Endianと仮定)
                ReadOnlySpan<byte> packet_span = receive_buffer.AsSpan(0, bytes_read);
                
                // Offset 4: packet_index (uint16)
                ushort packet_index = BinaryPrimitives.ReadUInt16LittleEndian(packet_span.Slice(4));
                // Offset 6: total_packet (uint16)
                ushort total_packet = BinaryPrimitives.ReadUInt16LittleEndian(packet_span.Slice(6));
                // Offset 8: data_size (uint16)
                ushort data_size = BinaryPrimitives.ReadUInt16LittleEndian(packet_span.Slice(8));

                // データ整合性チェック: 受信サイズがヘッダー+データサイズ以上あるか
                if (bytes_read < Constants.HEADER_SIZE + data_size)
                {
                    continue;
                }

                // フレームの最初のパケットならバッファをリセット
                if (packet_index == 0)
                {
                    _current_payload_length = 0;
                }

                // バッファオーバーフロー対策
                if (_current_payload_length + data_size > _reassembly_buffer.Length)
                {
                    _current_payload_length = 0; // 破損フレームとして破棄
                    continue;
                }

                // ペイロード部分(ヘッダー以降)を結合バッファにコピー
                packet_span.Slice(Constants.HEADER_SIZE, data_size).CopyTo(_reassembly_buffer.AsSpan(_current_payload_length));
                _current_payload_length += data_size;

                // 最後のパケットを受信したらフレーム処理を実行
                if (packet_index == total_packet - 1)
                {
                    // レンダリング中でなければ処理を実行
                    if (Interlocked.CompareExchange(ref _is_rendering, 1, 0) == 0)
                    {
                        Process_frame(_current_payload_length);
                    }
                    
                    // 次のフレームのためにリセット
                    _current_payload_length = 0;
                }
            }
        }
        catch (Exception ex)
        {
             Console.WriteLine($"Socket Error : {ex.Message}");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(receive_buffer);
            try { _socket.Close(); } catch {}
        }
    }

    private void Process_frame(int length)
    {
        try
        {
            // MemoryStreamを作成し、Bitmapに変換
            using (var ms = new MemoryStream(_reassembly_buffer, 0, length, writable: false))
            {
                var bitmap = new Bitmap(ms);
                // UIスレッドでイベント発火
                Dispatcher.UIThread.Post(() =>
                {
                    OnFrameReceived?.Invoke(bitmap);
                    Interlocked.Exchange(ref _is_rendering, 0);
                }, DispatcherPriority.Render);
            }
        }
        catch
        {
            // エラー時はロックを解除して次へ
            Interlocked.Exchange(ref _is_rendering, 0);
        }
    }

    public void Stop()
    {
        _is_running = false;
        try
        {
            _socket?.Shutdown(SocketShutdown.Both);
            _socket?.Close();
        }
        catch { /* 無視 */ }
        finally
        {
            _socket?.Dispose();
        }
    }
}
