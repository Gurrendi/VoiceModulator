using NAudio.Wave;

internal enum ModulationMode
{
    Normal = 1,
    Robotic,
    Echo,
    Tremolo,
    Gasmask,
    DarthVader
}

internal class Program
{
    private const int SampleRate = 44100;
    private const int Channels = 1;

    private static float[] _echoBuffer = new float[SampleRate * Channels];
    private static int _echoIndex;

    private static ModulationMode _mode = ModulationMode.Normal;

    static void Main()
    {
        Console.WriteLine("Voice Modulator (NAudio)");
        Console.WriteLine("1: Normal, 2: Robotic, 3: Echo, 4: Tremolo, 5: Gasmask, 6: DarthVader");

        var waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(SampleRate, 16, Channels),
            BufferMilliseconds = 50
        };

        var playbackBuffer = new BufferedWaveProvider(waveIn.WaveFormat)
        {
            BufferDuration = TimeSpan.FromSeconds(5),
            DiscardOnBufferOverflow = true
        };

        var waveOut = new WaveOutEvent();
        waveOut.Init(playbackBuffer);
        waveOut.Play();

        waveIn.DataAvailable += (s, e) =>
        {
            ProcessAudio(e.Buffer, e.BytesRecorded, playbackBuffer);
        };

        waveIn.StartRecording();

        while (true)
        {
            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.Q) break;
            switch (key.Key)
            {
                case ConsoleKey.D1: _mode = ModulationMode.Normal; break;
                case ConsoleKey.D2: _mode = ModulationMode.Robotic; break;
                case ConsoleKey.D3: _mode = ModulationMode.Echo; break;
                case ConsoleKey.D4: _mode = ModulationMode.Tremolo; break;
                case ConsoleKey.D5: _mode = ModulationMode.Gasmask; break;
                case ConsoleKey.D6: _mode = ModulationMode.DarthVader; break;
            }
            Console.WriteLine($"Mode set to {_mode}");
        }

        waveIn.StopRecording();
        waveOut.Stop();
        waveOut.Dispose();
        waveIn.Dispose();
    }

    private static void ProcessAudio(byte[] buffer, int bytesRecorded, BufferedWaveProvider playback)
    {
        var sampleCount = bytesRecorded / 2; // 16-bit
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; ++i)
        {
            short sample = (short)(buffer[i * 2] | (buffer[i * 2 + 1] << 8));
            samples[i] = sample / 32768f;
        }

        switch (_mode)
        {
            case ModulationMode.Normal:
                break;
            case ModulationMode.Robotic:
                ApplyRobot(samples);
                break;
            case ModulationMode.Echo:
                ApplyEcho(samples, 0.5f, 0.4f);
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
        }

        for (int i = 0; i < sampleCount; ++i)
        {
            float clipped = Math.Max(-1.0f, Math.Min(1.0f, samples[i]));
            short outSample = (short)(clipped * 32767f);
            buffer[i * 2] = (byte)(outSample & 0xFF);
            buffer[i * 2 + 1] = (byte)((outSample >> 8) & 0xFF);
        }

        playback.AddSamples(buffer, 0, bytesRecorded);
    }

    private static void ApplyRobot(float[] samples)
    {
        const float freq = 40.0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float mod = (float)Math.Sign(Math.Sin(2 * Math.PI * freq * i / SampleRate));
            samples[i] *= 0.7f * mod;
        }
    }

    private static void ApplyEcho(float[] samples, float delaySec, float decay)
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

    private static void ApplyTremolo(float[] samples, float tremoloHz, float depth)
    {
        for (int i = 0; i < samples.Length; i++)
        {
            float mod = (1 - depth) + depth * (0.5f + 0.5f * (float)Math.Sin(2 * Math.PI * tremoloHz * i / SampleRate));
            samples[i] *= mod;
        }
    }
    private static void ApplyGasmask(float[] samples)
    {
        for (int i = 0; i < samples.Length; i++)
        {
            float mod = (float)Math.Sin(2 * Math.PI * 1000 * i / SampleRate);
            samples[i] *= mod;
        }
    }

     
    
private static float _vaderPhase1 = 0f;// Class-level state:
private static float _vaderPhase2 = 0f;// Class-level state:
private static float _lp1 = 0f;// Class-level state:
private static float _lp2 = 0f;// Class-level state:

// Comb filter state (for metallic resonance)
private static float[] _combBuffer;
private static int _combIndex = 0;

private static void ApplyDarthVader(float[] samples, float sampleRate = 44100f)
{
    if (samples == null || samples.Length == 0) return;

    // ---- Tunable parameters ----
    const float drive = 3.6f;          // more grit
    const float modHz1 = 62f;          // lower = heavier/doomier
    const float modHz2 = 185f;         // extra metallic edge
    const float cutoffHz = 820f;       // lower cutoff = deeper/darker
    const float wet = 0.92f;           // stronger processed blend

    // Comb filter (metal resonance)
    const float combDelayMs = 4.2f;    // 3..6 ms often works well
    const float combFeedback = 0.42f;  // 0.25..0.55
    const float combMix = 0.28f;       // how metallic it gets

    // Prepare comb buffer
    int combDelaySamples = Math.Max(1, (int)(sampleRate * combDelayMs / 1000f));
    if (_combBuffer == null || _combBuffer.Length != combDelaySamples)
    {
        _combBuffer = new float[combDelaySamples];
        _combIndex = 0;
    }

    float phaseInc1 = 2f * MathF.PI * modHz1 / sampleRate;
    float phaseInc2 = 2f * MathF.PI * modHz2 / sampleRate;

    // Two cascaded one-pole LP filters for stronger darkening
    float alpha = 1f - MathF.Exp(-2f * MathF.PI * cutoffHz / sampleRate);

    for (int i = 0; i < samples.Length; i++)
    {
        float dry = samples[i];
        float x = dry;

        // 1) Saturation
        x = MathF.Tanh(x * drive);

        // 2) Ring-mod blend: low mod (weight) + high mod (metal)
        float c1 = MathF.Sin(_vaderPhase1);
        float c2 = MathF.Sin(_vaderPhase2);
        float mod = 0.58f + 0.27f * c1 + 0.15f * c2;
        x *= mod;

        _vaderPhase1 += phaseInc1;
        if (_vaderPhase1 >= 2f * MathF.PI) _vaderPhase1 -= 2f * MathF.PI;

        _vaderPhase2 += phaseInc2;
        if (_vaderPhase2 >= 2f * MathF.PI) _vaderPhase2 -= 2f * MathF.PI;

        // 3) Darken/deepen (2-stage LP)
        _lp1 += alpha * (x - _lp1);
        _lp2 += alpha * (_lp1 - _lp2);
        float filtered = _lp2;

        // 4) Comb filter for metallic resonance
        float delayed = _combBuffer[_combIndex];
        float combOut = filtered + delayed * combMix;
        _combBuffer[_combIndex] = filtered + delayed * combFeedback;
        _combIndex++;
        if (_combIndex >= _combBuffer.Length) _combIndex = 0;

        // 5) Wet/dry mix + gain boost
        float y = dry * (1f - wet) + combOut * wet;
        y *= 1.4f;  // Boost volume to match other modes

        samples[i] = Math.Clamp(y, -1f, 1f);
    }
}
}
