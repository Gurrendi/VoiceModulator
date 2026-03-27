using System;
using NAudio.Wave;
using NAudio.CoreAudioApi;

public class VoiceModEngine : IDisposable
{
    private const int SampleRate = 44100;
    private const int Channels = 1;
     private float _hpState = 0f;  // High-pass state
    private float _bpState = 0f;  // Band-pass state
    private float _svfLp = 0f;    // SVF low-pass state
    private float _prevIn = 0f;   // Previous input for DC blocker
    private float _prevHp = 0f;   // Previous high-pass state
    private float _distortionState = 0f; // Distortion smoothing
    private float _militaryLpState = 0f; // Military narrator low-pass state
    private float[]? _militaryReverbBuffer; // Military narrator reverb buffer
    private int _militaryReverbIndex = 0; // Military narrator reverb index
    private float prevLow = 0f;
    private float prevHigh = 0f;
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
    private WaveInEvent? _testWaveIn;
    private WaveOutEvent? _testWaveOut;
    private WasapiOut? _virtualMicOut;
    private BufferedWaveProvider? _playbackBuffer;
    private BufferedWaveProvider? _testBuffer;

    public ModulationMode Mode { get; set; } = ModulationMode.Normal;
    public float Volume { get; set; } = 1.0f; // 0.0..2.0

    // Useful for Echo mode
    public float EchoDelaySeconds { get; set; } = 0.5f;
    public float EchoDecay { get; set; } = 0.4f;

    // Output mode: true = virtual microphone (for calls/games), false = speakers (for testing)
    public bool UseVirtualMicrophone { get; set; } = false;

    public bool IsRunning => _waveIn != null;
    public bool IsTesting => _testWaveOut != null;

    public event Action<string>? StatusChanged;

    public void Start()
    {
        if (IsRunning) return;

        // Always use virtual microphone for production mode
        UseVirtualMicrophone = true;

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

        if (UseVirtualMicrophone)
        {
            // Try to find and use virtual microphone device for calls/games
            var virtualMicDevice = FindVirtualMicrophoneDevice();
            if (virtualMicDevice != null)
            {
                _virtualMicOut = new WasapiOut(virtualMicDevice, AudioClientShareMode.Shared, false, 50);
                _virtualMicOut.Init(_playbackBuffer);
                StatusChanged?.Invoke("Using virtual microphone for calls/games - no speaker output");
            }
            else
            {
                // Fallback to speakers if no virtual mic found
                _waveOut = new WaveOutEvent();
                _waveOut.Init(_playbackBuffer);
                StatusChanged?.Invoke("No virtual microphone found - install VB-Audio Virtual Cable for calls/games");
            }
        }
        else
        {
            // Use speakers for testing
            _waveOut = new WaveOutEvent();
            _waveOut.Init(_playbackBuffer);
            StatusChanged?.Invoke("Using speakers for testing");
        }

        _waveIn.DataAvailable += WaveIn_DataAvailable;
        _waveIn.RecordingStopped += WaveIn_RecordingStopped;

        // Start the appropriate output device
        if (_virtualMicOut != null)
        {
            _virtualMicOut.Play();
        }
        else if (_waveOut != null)
        {
            _waveOut.Play();
        }

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

        if (_virtualMicOut != null)
        {
            _virtualMicOut.Stop();
            _virtualMicOut.Dispose();
            _virtualMicOut = null;
        }

        _playbackBuffer = null;

        StatusChanged?.Invoke("Stopped");
    }

    public void StartTest()
    {
        if (IsTesting) return;

        _testWaveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(SampleRate, 16, Channels),
            BufferMilliseconds = 50
        };

        _testBuffer = new BufferedWaveProvider(_testWaveIn.WaveFormat)
        {
            BufferDuration = TimeSpan.FromSeconds(5),
            DiscardOnBufferOverflow = true
        };

        _testWaveOut = new WaveOutEvent();
        _testWaveOut.Init(_testBuffer);

        _testWaveIn.DataAvailable += TestWaveIn_DataAvailable;
        _testWaveIn.RecordingStopped += TestWaveIn_RecordingStopped;

        _testWaveOut.Play();
        _testWaveIn.StartRecording();

        StatusChanged?.Invoke("Test mode started - adjust settings while listening through speakers");
    }

    public void StopTest()
    {
        if (!IsTesting) return;

        if (_testWaveIn != null)
        {
            _testWaveIn.DataAvailable -= TestWaveIn_DataAvailable;
            _testWaveIn.RecordingStopped -= TestWaveIn_RecordingStopped;
            _testWaveIn.StopRecording();
            _testWaveIn.Dispose();
            _testWaveIn = null;
        }

        if (_testWaveOut != null)
        {
            _testWaveOut.Stop();
            _testWaveOut.Dispose();
            _testWaveOut = null;
        }

        _testBuffer = null;

        StatusChanged?.Invoke("Test mode stopped");
    }

    private void TestWaveIn_DataAvailable(object? sender, WaveInEventArgs e)
    {
        try
        {
            if (_testBuffer == null) return;
            ProcessAudio(e.Buffer, e.BytesRecorded, _testBuffer);
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Test error: {ex.Message}");
        }
    }

    private void TestWaveIn_RecordingStopped(object? sender, StoppedEventArgs e)
    {
        // Handle test recording stopped if needed
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
            case ModulationMode.WallE:
                WallE(samples);
                break;
            case ModulationMode.DarthVader:
                ApplyDarthVader(samples);
                break;
            case ModulationMode.Astartes:
                ApplytacticalMissile(samples);
                break;
            case ModulationMode.BattlefieldRadio:
                ApplyBattlefieldRadio(samples);
                break;
            case ModulationMode.MilitaryNarrator:
                ApplyMilitaryNarrator(samples);
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

    private void WallE(float[] samples)
    {
        for (int i = 0; i < samples.Length; i++)
        {
            float mod = MathF.Sin(2 * MathF.PI * 1000 * i / SampleRate);
            samples[i] *= mod;
        }
    }

    private void ApplytacticalMissile(float[] samples)
    {
        for (int i = 0; i < samples.Length; i++)
        {
            float mod = MathF.Sin(2 * MathF.PI * 200 * i / SampleRate);
            samples[i] *= mod;
        }
    }
    private void ApplyDarthVader(float[] samples)
    {
        if (samples.Length == 0) return;

        const float drive = 4.2f;
        const float modHz1 = 45f;
        const float modHz2 = 130f;
        const float cutoffHz = 550f;
        const float wet = 0.92f;
        const float combDelayMs = 4.2f;
        const float combFeedback = 0.45f;
        const float combMix = 0.35f;

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

    private void ApplyBattlefieldRadio(float[] samples)
{
    if (samples == null || samples.Length == 0) return;
    
    float fs = SampleRate; // Your sample rate

    // Tuning parameters
    const float highPassFreq = 120f;    // Remove rumble (lowered for more bass)
    const float bandPassFreq = 1800f;   // Focus on intelligibility range (lowered)
    const float bandPassQ = 1.2f;       // Wider bandwidth
    const float distortion = 2.2f;      // More grit
    const float wet = 0.9f;             // More effect intensity

    // Filter coefficients
    float hpAlpha = 1f - MathF.Exp(-2f * MathF.PI * highPassFreq / fs);
    float f = 2f * MathF.Sin(MathF.PI * bandPassFreq / fs);
    float damp = 1f / bandPassQ;

    for (int i = 0; i < samples.Length; i++)
    {
        float dry = samples[i];
        float x = dry;

        // 1. DC blocking (prevents rumble)
        float hp = x - _prevIn + 0.995f * _prevHp;
        _prevIn = x;
        _prevHp = hp;
        x = hp;

        // 2. Aggressive high-pass
        _hpState += hpAlpha * (x - _hpState);
        x -= _hpState;

        // 3. Military-style band-pass (honky mid-range)
        float svfHp = x - _svfLp - damp * _bpState;
        _bpState += f * svfHp;
        _svfLp += f * _bpState;
        float bandPass = _bpState;
        x = bandPass;

        // 4. Clean distortion (avoid harsh clipping)
        float distorted = MathF.Tanh(x * distortion);
        _distortionState = 0.85f * _distortionState + 0.15f * distorted;
        x = _distortionState;

        // 5. Wet/dry mix (preserve clarity)
        samples[i] = Math.Clamp(dry * (1f - wet) + x * wet, -1f, 1f);
    }
}

    private void ApplyMilitaryNarrator(float[] samples)
    {
        float lowAlpha = 0.1f;   // minimal filtering for sharp sound
        float highAlpha = 0.7f;   // aggressive high boost

    for (int i = 0; i < samples.Length; i++)
    {
        float input = samples[i];

        // 1. Light low-pass (keep clarity)
        float low = lowAlpha * input + (1 - lowAlpha) * prevLow;

        // 2. High-pass (remove rumble / boost crispness)
        float high = highAlpha * input + (1 - highAlpha) * prevHigh;

        // 3. Blend mids (radio focus) - more highs for aggression
        float band = (low * 0.5f) + (high * 0.5f);

        // 4. Tight compression (punchy, not squashed)
        float compressed = band * 4.5f; 

        // 5. Crisp distortion (edge, not mud)
        float distorted = MathF.Tanh(compressed * 1.8f);

        // 6. VERY subtle noise (optional)
        float noise = ((float)new Random().NextDouble() * 2f - 1f) * 0.001f;

        samples[i] = distorted + noise;

        prevLow = low;
        prevHigh = high;
    }
    }

    public void ToggleOutputMode()
    {
        if (IsRunning)
        {
            Stop();
            UseVirtualMicrophone = !UseVirtualMicrophone;
            Start();
        }
        else
        {
            UseVirtualMicrophone = !UseVirtualMicrophone;
        }
    }

    public string GetCurrentOutputMode()
    {
        return UseVirtualMicrophone ? "Virtual Mic (Calls/Games)" : "Speakers (Testing)";
    }

    public void Dispose()
    {
        Stop();
        StopTest();
    }

    private MMDevice? FindVirtualMicrophoneDevice()
    {
        try
        {
            var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

            // Common virtual microphone device names
            string[] virtualMicNames = new[]
            {
                "CABLE Input",           // VB-Audio Virtual Cable
                "VB-Audio Virtual Cable",
                "VoiceMeeter",           // VoiceMeeter
                "VoiceMeeter VAIO",      // VoiceMeeter VAIO
                "VoiceMeeter AUX",       // VoiceMeeter AUX
                "Microphone (Virtual Audio Device)", // Generic virtual mic
                "Virtual Microphone",    // Generic
                "Stereo Mix",            // Some virtual devices
                "What U Hear",           // Some virtual devices
                "Line 1 (Virtual Audio Cable)" // VAC
            };

            foreach (var device in devices)
            {
                string deviceName = device.FriendlyName.ToLowerInvariant();
                foreach (string virtualName in virtualMicNames)
                {
                    if (deviceName.Contains(virtualName.ToLowerInvariant()))
                    {
                        return device;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Error finding virtual mic: {ex.Message}");
        }

        return null;
    }
}
