using System;
using System.Buffers;
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
    public Action<Bitmap?>? OnFrameReceived; // 名前をViewModel側に合わせて変更 (OnFrameReady -> OnFrameReceived)

    public bool IsPaused { get; set; } = false; // ViewModel側で参照しているため追加

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
        
        // 既存のポートバインディングエラーを防ぐためReuseAddressを設定する場合もあるが、今回は標準設定
        _socket.Bind(new IPEndPoint(IPAddress.Any, _port));
        
        // 受信バッファを拡張
        _socket.ReceiveBufferSize = Constants.KERNEL_S_UDP_BUFFER_SIZE;
        
        // ArrayPoolから一時的な受信バッファを借りる
        byte[] receive_buffer = ArrayPool<byte>.Shared.Rent(4096);
        
        try
        {
            EndPoint remote_endpoint = new IPEndPoint(IPAddress.Any, 0);
            
            while (_is_running)
            {
                // 非同期でデータ受信
                var result = await _socket.ReceiveFromAsync(new Memory<byte>(receive_buffer), SocketFlags.None, remote_endpoint);
                
                if (IsPaused) continue; // 一時停止中は処理しない

                int bytes_read = result.ReceivedBytes;
                
                // ヘッダ(1byte) + データがない場合は無視
                if (bytes_read < 2)
                {
                    continue;
                }

                // 受信データをSpanとして扱う
                ReadOnlySpan<byte> packet_span = receive_buffer.AsSpan(0, bytes_read);

                // 先頭1バイトはフラグ (0: 続き, 1: フレーム終了)
                byte flag = packet_span[0];
                int payload_size = bytes_read - 1;

                // バッファオーバーフロー対策
                if (_current_payload_length + payload_size > _reassembly_buffer.Length)
                {
                    _current_payload_length = 0; // 破損フレームとして破棄
                    continue;
                }

                // ペイロード部分を結合バッファにコピー
                packet_span.Slice(1, payload_size).CopyTo(_reassembly_buffer.AsSpan(_current_payload_length));
                _current_payload_length += payload_size;

                // フレーム終了フラグの場合
                if (flag == 1)
                {
                    // レンダリング中でなければ処理を実行 (ドロップフレーム処理)
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
            // バッファをプールに返却
            ArrayPool<byte>.Shared.Return(receive_buffer);
            try { _socket.Close(); } catch {}
        }
    }

    private void Process_frame(int length)
    {
        try
        {
            // MemoryStreamを作成し、Bitmapに変換
            // Note: MemoryStreamはバッファをコピーせず参照するため高速ですが、
            // Bitmap作成完了までは _reassembly_buffer を書き換えてはいけません。
            using (var ms = new MemoryStream(_reassembly_buffer, 0, length, writable: false))
            {
                var bitmap = new Bitmap(ms);
                
                // UIスレッドでイベント発火
                Dispatcher.UIThread.Post(() =>
                {
                    OnFrameReceived?.Invoke(bitmap);
                    
                    // 処理完了、レンダリングロック解除
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
