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
using OpenCvSharp;
using System.Runtime.InteropServices;

namespace avalonia_terminal.Services;

static class Constants
{
    public const int ALLOC_BYTE_SIZE = 8 * 1024 * 1024;
    public const int KERNEL_S_UDP_BUFFER_SIZE = 8 * 1024 * 1024;
    public const int HEADER_SIZE = 15;
}

/// <summary>
/// UDPによる映像ストリーム受信クラス。
/// 分割されたUDPパケットの再構築、および画像データのデコード（MJPEG/YUV422）を担当します。
/// </summary>
public class UdpVideoReceiver
{
    private Socket? _socket;
    private bool _is_running;
    private readonly int _port;
    private readonly byte[] _reassembly_buffer = new byte[Constants.ALLOC_BYTE_SIZE];
    private int _current_payload_length = 0;
    
    /// <summary>
    /// 排他制御用フラグ (0:待機可能, 1:処理中)。
    /// UIスレッドの描画遅延時に、バッファ溢れやメモリ競合を防ぐために使用します。
    /// </summary>
    private int _is_processing = 0;
    
    private uint _current_frame_id = 0;
    private int _current_width = 0;
    private int _current_height = 0;

    /// <summary>
    /// フレーム受信完了時に発火するイベント。生成されたBitmapを通知します。
    /// </summary>
    public Action<Avalonia.Media.Imaging.Bitmap?>? OnFrameReceived;
    
    /// <summary>
    /// 受信処理の一時停止フラグ。
    /// </summary>
    public bool IsPaused { get; set; } = false;

    /// <summary>
    /// コンストラクタ。
    /// </summary>
    /// <param name="port">受信待機ポート番号</param>
    public UdpVideoReceiver(int port)
    {
        _port = port;
    }

    /// <summary>
    /// 受信スレッドを開始します。
    /// </summary>
    public void Start()
    {
        if (_is_running) return;
        _is_running = true;
        Task.Run(Receiver_loop);
    }

    /// <summary>
    /// UDPパケット受信ループ。
    /// ヘッダー解析とペイロードの結合を行います。
    /// </summary>
    private async Task Receiver_loop()
    {
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _socket.Bind(new IPEndPoint(IPAddress.Any, _port));
        _socket.ReceiveBufferSize = Constants.KERNEL_S_UDP_BUFFER_SIZE;
        
        byte[] receive_buffer = ArrayPool<byte>.Shared.Rent(65536);
        
        try
        {
            EndPoint remote_endpoint = new IPEndPoint(IPAddress.Any, 0);
            
            while (_is_running)
            {
                var result = await _socket.ReceiveFromAsync(new Memory<byte>(receive_buffer), SocketFlags.None, remote_endpoint);
                
                if (IsPaused) continue;

                int bytes_read = result.ReceivedBytes;
                if (bytes_read < Constants.HEADER_SIZE) continue;

                ReadOnlySpan<byte> packet_span = receive_buffer.AsSpan(0, bytes_read);

                // ヘッダー解析 (BigEndian)
                uint identifier = BinaryPrimitives.ReadUInt32BigEndian(packet_span.Slice(0));
                ushort packet_index = BinaryPrimitives.ReadUInt16BigEndian(packet_span.Slice(4));
                ushort total_packet = BinaryPrimitives.ReadUInt16BigEndian(packet_span.Slice(6));
                ushort data_size = BinaryPrimitives.ReadUInt16BigEndian(packet_span.Slice(8));
                
                ushort width = BinaryPrimitives.ReadUInt16BigEndian(packet_span.Slice(10));
                ushort height = BinaryPrimitives.ReadUInt16BigEndian(packet_span.Slice(12));

                if (bytes_read < Constants.HEADER_SIZE + data_size) continue;

                // 新しいフレームの開始パケット検出
                if (packet_index == 0)
                {
                    _current_frame_id = identifier;
                    _current_payload_length = 0;
                    _current_width = width;
                    _current_height = height;
                }
                else if (identifier != _current_frame_id)
                {
                    // 異なるフレームのパケットが混入した場合は破棄
                    continue;
                }

                if (_current_payload_length + data_size > _reassembly_buffer.Length)
                {
                    _current_payload_length = 0;
                    continue;
                }

                // ペイロードを再構築バッファへコピー
                packet_span.Slice(Constants.HEADER_SIZE, data_size).CopyTo(_reassembly_buffer.AsSpan(_current_payload_length));
                _current_payload_length += data_size;

                // 最終パケット受信時、画像生成処理へ
                if (packet_index == total_packet - 1)
                {
                    // 排他制御: 画像生成処理が空いている場合のみ実行（フレームドロップ処理）
                    if (Interlocked.CompareExchange(ref _is_processing, 1, 0) == 0)
                    {
                        Process_frame(_current_payload_length);
                    }
                    _current_payload_length = 0;
                }
            }
        }
        catch (Exception ex)
        {
             Console.WriteLine($"UDP Loop Error: {ex.Message}");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(receive_buffer);
            try { _socket?.Close(); } catch {}
        }
    }

    /// <summary>
    /// 再構築されたバッファからBitmap画像を生成します。
    /// </summary>
    /// <param name="length">データのバイト長</param>
    private void Process_frame(int length)
    {
        try
        {
            Avalonia.Media.Imaging.Bitmap? bitmap = null;
            int w = _current_width;
            int h = _current_height;

            // 1. まず標準的なMJPEG形式としてデコードを試行
            try
            {
                using (var ms = new MemoryStream(_reassembly_buffer, 0, length, writable: false))
                {
                    bitmap = new Avalonia.Media.Imaging.Bitmap(ms);
                }
            }
            catch
            {
                // 2. デコード失敗時はRAWデータ(YUV422)とみなしてOpenCVで変換
                try
                {
                    if (w > 0 && h > 0)
                    {
                        var handle = GCHandle.Alloc(_reassembly_buffer, GCHandleType.Pinned);
                        try
                        {
                            // YUV422(YUYV) -> BGR変換 -> PNGエンコード -> Bitmap生成
                            using var matYuv = Mat.FromPixelData(h, w, MatType.CV_8UC2, handle.AddrOfPinnedObject());
                            using var matBgr = new Mat();
                            Cv2.CvtColor(matYuv, matBgr, ColorConversionCodes.YUV2BGR_YUYV);
                            
                            using var msOut = matBgr.ToMemoryStream(".png");
                            msOut.Position = 0;
                            bitmap = new Avalonia.Media.Imaging.Bitmap(msOut);
                        }
                        finally
                        {
                            handle.Free();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Image Convert Error: {ex.Message}");
                }
            }

            // 画像生成成功時、UIスレッドへ通知
            if (bitmap != null)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    OnFrameReceived?.Invoke(bitmap);
                }, DispatcherPriority.Render);
            }
        }
        finally
        {
            // UI更新の完了を待たずにロックを解除し、次のパケット受信を即座に許可する
            Interlocked.Exchange(ref _is_processing, 0);
        }
    }

    /// <summary>
    /// 受信スレッドを停止し、リソースを解放します。
    /// </summary>
    public void Stop()
    {
        _is_running = false;
        try
        {
            _socket?.Shutdown(SocketShutdown.Both);
            _socket?.Close();
        }
        catch { }
        finally
        {
            _socket?.Dispose();
        }
    }
}
