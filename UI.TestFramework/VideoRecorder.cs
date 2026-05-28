using SharpAvi;
using SharpAvi.Codecs;
using SharpAvi.Output;
using FlaUI.Core.Capturing;
using System.Drawing;
using System.Runtime.InteropServices;

namespace UI.TestFramework;

public class VideoRecorder : IDisposable
{
    private AviWriter? _writer;
    private IAviVideoStream? _stream;
    private Thread? _recordingThread;
    private bool _isRecording;
    private readonly string _filePath;

    public VideoRecorder(string filePath, int width = 1920, int height = 1080)
    {
        _filePath = filePath;
        try
        {
            _writer = new AviWriter(filePath)
            {
                FramesPerSecond = 10,
                EmitIndex1 = true
            };

            _stream = _writer.AddVideoStream();
            _stream.Width = width;
            _stream.Height = height;
            // Uncompressed for maximum compatibility without needing extra codecs
            _stream.Codec = CodecIds.Uncompressed;
            _stream.BitsPerPixel = BitsPerPixel.Bpp32;
        }
        catch(Exception)
        {
            // We ignore init errors to not fail tests when display is missing
        }
    }

    public void Start()
    {
        if (_writer == null) return;

        _isRecording = true;
        _recordingThread = new Thread(RecordLoop)
        {
            IsBackground = true
        };
        _recordingThread.Start();
    }

    public void Stop()
    {
        _isRecording = false;
        _recordingThread?.Join(1000);
        _writer?.Close();
    }

    private void RecordLoop()
    {
        if (_stream == null) return;

        try
        {
            byte[] buffer = new byte[_stream.Width * _stream.Height * 4];

            while (_isRecording)
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    try
                    {
                        var image = Capture.MainScreen();
                        using var ms = new MemoryStream();
                        image.Bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
                        byte[] imgBytes = ms.ToArray();

                        // We copy only the pixels portion of the BMP (usually starts around offset 54)
                        int offset = 54;
                        int lengthToCopy = Math.Min(buffer.Length, imgBytes.Length - offset);
                        if (lengthToCopy > 0)
                        {
                            Array.Copy(imgBytes, offset, buffer, 0, lengthToCopy);
                        }
                    }
                    catch { } // Ignore occasional capture errors
                }

                _stream.WriteFrame(true, buffer, 0, buffer.Length);
                Thread.Sleep(100);
            }
        }
        catch(Exception)
        {
            // Ignore capture errors on headless servers
        }
    }

    public void Dispose()
    {
        if (_isRecording) Stop();
    }
}
