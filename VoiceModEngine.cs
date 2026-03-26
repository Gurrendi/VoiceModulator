using System;
using NAudio.Wave;

public class VoiceModEngine : IDisposable
{
    private const int SampleRate = 44100;
    private const int Channels = 1;

    private readonly float[] _echoBuffer = new float[SampleRate * Channels];
    private int _echoIndex;

    private float _vaderPhase1;
    private float _vaderPhase2;
    private float _lp1;
    private float _lp2;
    private float[]? _combBuffer;
    private int _combIndex;

    private WaveInEvent? _waveIn;
    private WaveOutEvent? _waveOut;
    private BufferedWaveProvider? _playbackBuffer;

    public ModulationMode Mode { get; set; } = ModulationMode.Normal;
    public float Volume { get; set; } = 1.0f; // 0.0..2.0

    // Useful for Echo mode
    public float EchoDelaySeconds { get; set; } = 0.5f;
    public float EchoDecay { get; set; } = 0.4f;

    public bool IsRunning => _waveIn != null;

    public event Action<string>? StatusChanged;

    public void Start()
    {
        if (IsRunning) return;

        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(SampleRate, 16, Channels),
            BufferMilliseconds = 50
        };

        _playbackBuffer = new BufferedWaveProvider(_waveIn.WaveFormat)
        {
            BufferDuration = TimeSpan.FromSeconds(5),
            DiscardOnBufferOverflow = true
        };

        _waveOut = new WaveOutEvent();
        _waveOut.Init(_playbackBuffer);

        _waveIn.DataAvailable += WaveIn_DataAvailable;
        _waveIn.RecordingStopped += WaveIn_RecordingStopped;

        _waveOut.Play();
        _waveIn.StartRecording();

        StatusChanged?.Invoke("Started");
    }

    public void Stop()
    {
        if (!IsRunning) return;

        if (_waveIn != null)
        {
            _waveIn.DataAvailable -= WaveIn_DataAvailable;
            _waveIn.RecordingStopped -= WaveIn_RecordingStopped;
            _waveIn.StopRecording();
            _waveIn.Dispose();
            _waveIn = null;
        }

        if (_waveOut != null)
        {
            _waveOut.Stop();
            _waveOut.Dispose();
            _waveOut = null;
        }

        _playbackBuffer = null;

        StatusChanged?.Invoke("Stopped");
    }

    private void WaveIn_DataAvailable(object? sender, WaveInEventArgs e)
    {
        try
        {
            if (_playbackBuffer == null) return;
            ProcessAudio(e.Buffer, e.BytesRecorded, _playbackBuffer);
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Error: {ex.Message}");
        }
    }

    private void WaveIn_RecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            StatusChanged?.Invoke($"Recording stopped with error: {e.Exception.Message}");
        }
    }

    private void ProcessAudio(byte[] buffer, int bytesRecorded, BufferedWaveProvider playback)
    {
        int sampleCount = bytesRecorded / 2;
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; ++i)
        {
            short sample = (short)(buffer[i * 2] | (buffer[i * 2 + 1] << 8));
            samples[i] = sample / 32768f;
        }

        switch (Mode)
        {
            case ModulationMode.Robotic:
                ApplyRobot(samples);
                break;
            case ModulationMode.Echo:
                ApplyEcho(samples, EchoDelaySeconds, EchoDecay);
                break;
            case ModulationMode.Tremolo:
                ApplyTremolo(samples, 7.0f, 0.9f);
                break;
            case ModulationMode.Gasmask:
                ApplyGasmask(samples);
                break;
            case ModulationMode.DarthVader:
                ApplyDarthVader(samples);
                break;
            case ModulationMode.Normal:
            default:
                break;
        }

        for (int i = 0; i < sampleCount; ++i)
        {
            samples[i] *= Volume;
            float clipped = Math.Max(-1.0f, Math.Min(1.0f, samples[i]));
            short outSample = (short)(clipped * 32767f);
            buffer[i * 2] = (byte)(outSample & 0xFF);
            buffer[i * 2 + 1] = (byte)((outSample >> 8) & 0xFF);
        }

        playback.AddSamples(buffer, 0, bytesRecorded);
    }

    private void ApplyRobot(float[] samples)
    {
        const float freq = 40.0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float mod = MathF.Sign(MathF.Sin(2 * MathF.PI * freq * i / SampleRate));
            samples[i] *= 0.7f * mod;
        }
    }

    private void ApplyEcho(float[] samples, float delaySec, float decay)
    {
        int delaySamples = (int)(delaySec * SampleRate) * Channels;

        for (int i = 0; i < samples.Length; i++)
        {
            float delayed = _echoBuffer[_echoIndex];
            float output = samples[i] + delayed * decay;
            _echoBuffer[_echoIndex] = output;
            _echoIndex = (_echoIndex + 1) % _echoBuffer.Length;
            samples[i] = output;
        }
    }

    private void ApplyTremolo(float[] samples, float tremoloHz, float depth)
    {
        for (int i = 0; i < samples.Length; i++)
        {
            float mod = (1 - depth) + depth * (0.5f + 0.5f * MathF.Sin(2 * MathF.PI * tremoloHz * i / SampleRate));
            samples[i] *= mod;
        }
    }

    private void ApplyGasmask(float[] samples)
    {
        for (int i = 0; i < samples.Length; i++)
        {
            float mod = MathF.Sin(2 * MathF.PI * 1000 * i / SampleRate);
            samples[i] *= mod;
        }
    }

    private void ApplyDarthVader(float[] samples)
    {
        if (samples.Length == 0) return;

        const float drive = 3.6f;
        const float modHz1 = 62f;
        const float modHz2 = 185f;
        const float cutoffHz = 820f;
        const float wet = 0.92f;
        const float combDelayMs = 4.2f;
        const float combFeedback = 0.42f;
        const float combMix = 0.28f;

        int combDelaySamples = Math.Max(1, (int)(SampleRate * combDelayMs / 1000f));
        if (_combBuffer == null || _combBuffer.Length != combDelaySamples)
        {
            _combBuffer = new float[combDelaySamples];
            _combIndex = 0;
        }

        float phaseInc1 = 2f * MathF.PI * modHz1 / SampleRate;
        float phaseInc2 = 2f * MathF.PI * modHz2 / SampleRate;
        float alpha = 1f - MathF.Exp(-2f * MathF.PI * cutoffHz / SampleRate);

        for (int i = 0; i < samples.Length; i++)
        {
            float dry = samples[i];
            float x = MathF.Tanh(dry * drive);

            float c1 = MathF.Sin(_vaderPhase1);
            float c2 = MathF.Sin(_vaderPhase2);
            float mod = 0.58f + 0.27f * c1 + 0.15f * c2;
            x *= mod;

            _vaderPhase1 += phaseInc1;
            if (_vaderPhase1 >= 2f * MathF.PI) _vaderPhase1 -= 2f * MathF.PI;

            _vaderPhase2 += phaseInc2;
            if (_vaderPhase2 >= 2f * MathF.PI) _vaderPhase2 -= 2f * MathF.PI;

            _lp1 += alpha * (x - _lp1);
            _lp2 += alpha * (_lp1 - _lp2);
            float filtered = _lp2;

            float delayed = _combBuffer[_combIndex];
            float combOut = filtered + delayed * combMix;
            _combBuffer[_combIndex] = filtered + delayed * combFeedback;
            _combIndex = (_combIndex + 1) % _combBuffer.Length;

            float y = dry * (1f - wet) + combOut * wet;
            y *= 1.4f;

            samples[i] = Math.Clamp(y, -1f, 1f);
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
