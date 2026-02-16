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

public class UdpVideoReceiver
{
    private Socket? _socket;
    private bool _is_running;
    private readonly int _port;
    private readonly byte[] _reassembly_buffer = new byte[Constants.ALLOC_BYTE_SIZE];
    private int _current_payload_length = 0;
    
    // 排他制御用フラグ (0:空き, 1:処理中)
    private int _is_processing = 0;
    
    private uint _current_frame_id = 0;
    private int _current_width = 0;
    private int _current_height = 0;

    public Action<Avalonia.Media.Imaging.Bitmap?>? OnFrameReceived;
    public bool IsPaused { get; set; } = false;

    public UdpVideoReceiver(int port)
    {
        _port = port;
    }

    public void Start()
    {
        if (_is_running) return;
        _is_running = true;
        Task.Run(Receiver_loop);
    }

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

                uint identifier = BinaryPrimitives.ReadUInt32BigEndian(packet_span.Slice(0));
                ushort packet_index = BinaryPrimitives.ReadUInt16BigEndian(packet_span.Slice(4));
                ushort total_packet = BinaryPrimitives.ReadUInt16BigEndian(packet_span.Slice(6));
                ushort data_size = BinaryPrimitives.ReadUInt16BigEndian(packet_span.Slice(8));
                
                ushort width = BinaryPrimitives.ReadUInt16BigEndian(packet_span.Slice(10));
                ushort height = BinaryPrimitives.ReadUInt16BigEndian(packet_span.Slice(12));

                if (bytes_read < Constants.HEADER_SIZE + data_size) continue;

                if (packet_index == 0)
                {
                    _current_frame_id = identifier;
                    _current_payload_length = 0;
                    _current_width = width;
                    _current_height = height;
                }
                else if (identifier != _current_frame_id)
                {
                    continue;
                }

                if (_current_payload_length + data_size > _reassembly_buffer.Length)
                {
                    _current_payload_length = 0;
                    continue;
                }

                packet_span.Slice(Constants.HEADER_SIZE, data_size).CopyTo(_reassembly_buffer.AsSpan(_current_payload_length));
                _current_payload_length += data_size;

                if (packet_index == total_packet - 1)
                {
                    // ★重要: ここでフラグを立てて、Bitmap生成が終わったら即座に下ろす
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

    private void Process_frame(int length)
    {
        try
        {
            Avalonia.Media.Imaging.Bitmap? bitmap = null;
            int w = _current_width;
            int h = _current_height;

            // 1. MJPEGとして試す
            try
            {
                using (var ms = new MemoryStream(_reassembly_buffer, 0, length, writable: false))
                {
                    bitmap = new Avalonia.Media.Imaging.Bitmap(ms);
                }
            }
            catch
            {
                // MJPEG失敗 -> YUV422としてOpenCV変換
                try
                {
                    if (w > 0 && h > 0)
                    {
                        var handle = GCHandle.Alloc(_reassembly_buffer, GCHandleType.Pinned);
                        try
                        {
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

            // 画像があればUIへ通知 (ここは非同期)
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
            // ★最重要: UIスレッドへの通知完了を待たずに、即座にロックを解除する
            // これによりUIが固まっても、次の画像受信は止まらなくなる
            Interlocked.Exchange(ref _is_processing, 0);
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
        catch { }
        finally
        {
            _socket?.Dispose();
        }
    }
}
