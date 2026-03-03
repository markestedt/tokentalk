using NAudio.Wave;

namespace TokenTalk.Audio;

public record AudioSegment(byte[] WavData, int SampleRate, TimeSpan Duration);

public class AudioRecorder : IDisposable
{
    private readonly int _deviceIndex;
    private readonly int _maxSeconds;
    private const int SampleRate = 16000;
    private const int BitsPerSample = 16;
    private const int Channels = 1;

    public event Action<float>? AmplitudeAvailable;

    private WaveInEvent? _waveIn;
    private MemoryStream? _buffer;
    private WaveFileWriter? _writer;
    private DateTime _startTime;
    private bool _recording;
    private readonly object _lock = new();

    public AudioRecorder(int deviceIndex, int maxSeconds)
    {
        _deviceIndex = deviceIndex;
        _maxSeconds = maxSeconds;
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_recording)
                return;

            _buffer = new MemoryStream();
            var format = new WaveFormat(SampleRate, BitsPerSample, Channels);
            _writer = new WaveFileWriter(_buffer, format);

            _waveIn = new WaveInEvent
            {
                DeviceNumber = _deviceIndex,
                WaveFormat = format,
                BufferMilliseconds = 50
            };

            _waveIn.DataAvailable += OnDataAvailable;

            _startTime = DateTime.UtcNow;
            _recording = true;
            _waveIn.StartRecording();
        }
    }

    private static float ComputeNormalizedRms(byte[] buffer, int bytesRecorded)
    {
        if (bytesRecorded < 2)
            return 0f;

        long sumSquares = 0;
        int sampleCount = bytesRecorded / 2;
        for (int i = 0; i < bytesRecorded - 1; i += 2)
        {
            short sample = (short)(buffer[i] | (buffer[i + 1] << 8));
            sumSquares += (long)sample * sample;
        }
        double rms = Math.Sqrt((double)sumSquares / sampleCount);
        return (float)(rms / short.MaxValue);
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        float amplitude;
        Action<float>? handler;
        lock (_lock)
        {
            if (!_recording || _writer == null)
                return;

            _writer.Write(e.Buffer, 0, e.BytesRecorded);

            amplitude = ComputeNormalizedRms(e.Buffer, e.BytesRecorded);
            handler = AmplitudeAvailable;

            // Enforce max recording time
            if ((DateTime.UtcNow - _startTime).TotalSeconds >= _maxSeconds)
            {
                // Signal that max time was reached — recording will be stopped externally
            }
        }
        handler?.Invoke(amplitude);
    }

    public AudioSegment Stop()
    {
        lock (_lock)
        {
            if (!_recording || _waveIn == null || _writer == null || _buffer == null)
                return new AudioSegment([], SampleRate, TimeSpan.Zero);

            _recording = false;
            _waveIn.StopRecording();
            _waveIn.DataAvailable -= OnDataAvailable;
            _waveIn.Dispose();
            _waveIn = null;

            _writer.Flush();
            _writer.Dispose();
            _writer = null;

            var duration = DateTime.UtcNow - _startTime;
            var wavData = _buffer.ToArray();
            _buffer.Dispose();
            _buffer = null;

            return new AudioSegment(wavData, SampleRate, duration);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_recording)
            {
                _waveIn?.StopRecording();
                _recording = false;
            }
            _waveIn?.Dispose();
            _writer?.Dispose();
            _buffer?.Dispose();
        }
    }
}
