using NAudio.Wave;
using VoiceStudio.Audio;
using VoiceStudio.Audio.Capture;
using VoiceStudio.Audio.Effects;
using VoiceStudio.Audio.Mixing;
using VoiceStudio.Audio.Processing;
using VoiceStudio.Audio.Routing;

// Tests du cœur du moteur audio, sans aucun périphérique.
// Lancement : dotnet run --project tests/VoiceStudio.SelfTest

int failures = 0;
void Check(bool condition, string name)
{
    Console.WriteLine($"{(condition ? "OK   " : "ÉCHEC")} {name}");
    if (!condition) failures++;
}

// --- Tampon circulaire -------------------------------------------------------
{
    var ring = new AudioRingBuffer(8);
    Check(ring.Write(new float[] { 1, 2, 3, 4, 5, 6 }) == 6, "ring : écriture");
    var output = new float[4];
    Check(ring.Read(output) == 4 && output[0] == 1 && output[3] == 4, "ring : lecture dans l'ordre");
    Check(ring.Write(new float[] { 7, 8, 9, 10, 11, 12, 13 }) == 6, "ring : écriture limitée à la place libre");
    var rest = new float[8];
    Check(ring.Read(rest) == 8 && rest[0] == 5 && rest[7] == 12, "ring : passage de la fin au début");
    Check(ring.Available == 0, "ring : vide après lecture");
}

// --- Tampon circulaire entre deux threads -----------------------------------
{
    var ring = new AudioRingBuffer(1024);
    const int total = 2_000_000;
    bool ordered = true;
    var writer = new Thread(() =>
    {
        var block = new float[37];
        int next = 0;
        while (next < total)
        {
            int n = Math.Min(block.Length, total - next);
            for (int i = 0; i < n; i++) block[i] = next + i;
            int written = ring.Write(block.AsSpan(0, n));
            next += written;
            if (written == 0) Thread.Yield();
        }
    });
    writer.Start();
    var readBlock = new float[53];
    int expected = 0;
    while (expected < total)
    {
        int read = ring.Read(readBlock);
        for (int i = 0; i < read; i++)
            if (readBlock[i] != expected++) ordered = false;
        if (read == 0) Thread.Yield();
    }
    writer.Join();
    Check(ordered, "ring : 2 millions d'échantillons entre deux threads, sans perte ni désordre");
}

// --- Conversions -------------------------------------------------------------
{
    var floats = new float[2];
    SampleConverter.ToFloat(new byte[] { 0x00, 0x40, 0x00, 0xC0 }, SampleKind.Pcm16, floats);
    Check(floats[0] == 0.5f && floats[1] == -0.5f, "conversion PCM 16 bits");
    SampleConverter.ToFloat(new byte[] { 0x00, 0x00, 0x40, 0x00, 0x00, 0xC0 }, SampleKind.Pcm24, floats);
    Check(floats[0] == 0.5f && floats[1] == -0.5f, "conversion PCM 24 bits");
    SampleConverter.ToFloat(new byte[] { 0, 0, 0, 0x40, 0, 0, 0, 0xC0 }, SampleKind.Pcm32, floats);
    Check(floats[0] == 0.5f && floats[1] == -0.5f, "conversion PCM 32 bits");
    SampleConverter.ToFloat(BitConverter.GetBytes(0.25f), SampleKind.Float32, floats);
    Check(floats[0] == 0.25f, "conversion float 32 bits");
    Check(SampleConverter.GetSampleKind(new WaveFormat(44100, 16, 1)) == SampleKind.Pcm16, "détection du format PCM");
    Check(SampleConverter.GetSampleKind(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)) == SampleKind.Float32,
        "détection du format float");
}

// --- Source de test ----------------------------------------------------------
static float[] Pull(ISampleProvider provider, int frames)
{
    var buffer = new float[frames * 2];
    var block = new float[960];
    int done = 0;
    while (done < buffer.Length)
    {
        int n = Math.Min(block.Length, buffer.Length - done);
        provider.Read(block, 0, n);
        Array.Copy(block, 0, buffer, done, n);
        done += n;
    }
    return buffer;
}

static float PeakOf(float[] buffer, int from = 0)
{
    float peak = 0;
    for (int i = from; i < buffer.Length; i++) peak = Math.Max(peak, Math.Abs(buffer[i]));
    return peak;
}

// --- Mixeur : volumes, mute, addition ---------------------------------------
{
    var a = new MixerChannel("A", 48000) { Source = new ConstantSource(0.25f) };
    var b = new MixerChannel("B", 48000) { Source = new ConstantSource(0.5f) };
    var mixer = new MixerEngine(a, b);

    var mixed = Pull(mixer, 4800);
    Check(Math.Abs(mixed[^1] - 0.75f) < 1e-4, "mixeur : addition de deux sources (0,25 + 0,5 = 0,75)");

    b.Volume = 0.5f;
    mixed = Pull(mixer, 4800);
    Check(Math.Abs(mixed[^1] - 0.5f) < 1e-4, "mixeur : volume d'une source à 50 %");

    b.Muted = true;
    mixed = Pull(mixer, 4800);
    Check(Math.Abs(mixed[^1] - 0.25f) < 1e-4, "mixeur : source coupée");
    Pull(mixer, 48000); // l'indicateur d'une source coupée retombe en ~1 s
    Check(b.Peak < 0.05f && a.Peak > 0.2f, "mixeur : indicateurs de niveau par source");

    a.Volume = 0f;
    var ramp = Pull(mixer, 480);
    bool smooth = true;
    for (int i = 2; i < ramp.Length; i += 2)
        if (Math.Abs(ramp[i] - ramp[i - 2]) > 0.01f) smooth = false;
    Check(smooth && Math.Abs(ramp[^1]) < 1e-4, "mixeur : changement de volume progressif (pas de clic)");

    mixer.MasterVolume = 0f;
    a.Volume = 1f;
    Pull(mixer, 4800);
    Check(PeakOf(Pull(mixer, 480)) < 1e-4, "mixeur : volume général à 0");
    Check(mixer.FramesRendered > 0, "mixeur : compteur de trames");
}

// --- Limiteur : jamais de saturation ----------------------------------------
{
    var loud1 = new MixerChannel("A", 48000) { Source = new SineSource(440, 0.9f), Volume = 2f };
    var loud2 = new MixerChannel("B", 48000) { Source = new SineSource(660, 0.9f), Volume = 2f };
    var mixer = new MixerEngine(loud1, loud2) { MasterVolume = 2f };
    var output = Pull(mixer, 48000);
    Check(PeakOf(output) <= 1f, "limiteur : la sortie ne dépasse jamais 0 dBFS");
    Check(PeakOf(output, 9600) <= 0.9f, "limiteur : niveau ramené sous le seuil");
    Check(mixer.LimiterGain < 0.5f, "limiteur : réduction de gain signalée");

    var quiet = new MixerEngine(new MixerChannel("A", 48000) { Source = new SineSource(440, 0.5f) });
    Check(Math.Abs(PeakOf(Pull(quiet, 48000), 9600) - 0.5f) < 0.01f, "limiteur : n'agit pas sur un signal normal");
}

// --- Chaîne de capture : mono 44,1 kHz 16 bits → stéréo 48 kHz --------------
{
    var pipeline = new CaptureSourcePipeline(44100, 1);
    var output = new List<float>();
    var block = new float[960];
    var bytes = new byte[441 * 2];
    double phase = 0;
    for (int tick = 0; tick < 200; tick++) // 2 secondes par blocs de 10 ms
    {
        for (int i = 0; i < 441; i++)
        {
            short sample = (short)(Math.Sin(phase) * 16000);
            phase += 2 * Math.PI * 1000 / 44100;
            bytes[2 * i] = (byte)sample;
            bytes[2 * i + 1] = (byte)(sample >> 8);
        }
        pipeline.Write(bytes, SampleKind.Pcm16);
        pipeline.Output.Read(block, 0, block.Length);
        output.AddRange(block);
    }

    Check(pipeline.Output.WaveFormat.SampleRate == 48000 && pipeline.Output.WaveFormat.Channels == 2,
        "capture : sortie en 48 kHz stéréo");

    // Fréquence mesurée par passages à zéro sur la dernière seconde (canal gauche).
    int crossings = 0;
    int start = output.Count - 96000;
    bool stereoEqual = true;
    for (int i = start + 2; i < output.Count; i += 2)
    {
        if (output[i - 2] < 0 && output[i] >= 0) crossings++;
        if (output[i] != output[i + 1]) stereoEqual = false;
    }
    Check(Math.Abs(crossings - 1000) <= 12, $"capture : un son de 1000 Hz reste à 1000 Hz après conversion ({crossings} Hz)");
    Check(stereoEqual, "capture : micro mono recopié sur les deux canaux");
    float peak = PeakOf(output.ToArray(), start);
    Check(peak > 0.4f && peak < 0.55f, $"capture : niveau conservé ({peak:0.00})");
}

// --- Source en retard : silence, puis reprise propre ------------------------
{
    var ring = new AudioRingBuffer(48000 * 2);
    var provider = new RingBufferSampleProvider(ring, AudioConstants.MixFormat, 20, 150);
    var block = new float[960];
    provider.Read(block, 0, 960);
    Check(PeakOf(block) == 0 && provider.Underruns == 0, "retard : silence tant que rien n'est arrivé");

    var ones = new float[960];
    Array.Fill(ones, 1f);
    ring.Write(ones);
    provider.Read(block, 0, 960);
    Check(PeakOf(block) == 0, "retard : attend le pré-remplissage avant de jouer");
    ring.Write(ones);
    provider.Read(block, 0, 960);
    Check(block[0] == 1f && block[959] == 1f, "retard : lecture normale une fois pré-rempli");

    for (int i = 0; i < 30; i++) ring.Write(ones); // 300 ms d'avance : dérive simulée
    provider.Read(block, 0, 960);
    Check(provider.Overruns == 1 && ring.Available <= 48000 * 2 * 150 / 1000,
        "dérive : la latence accumulée est rattrapée");
}

// --- Sources multiples (applications qui apparaissent / disparaissent) ------
{
    var multi = new MultiSourceSampleProvider();
    var block = new float[960];
    multi.Read(block, 0, 960);
    Check(PeakOf(block) == 0, "multi : silence sans application");
    multi.SetSources([new ConstantSource(0.2f), new ConstantSource(0.3f)]);
    multi.Read(block, 0, 960);
    Check(Math.Abs(block[500] - 0.5f) < 1e-5, "multi : addition de deux applications");
    multi.SetSources([new ConstantSource(0.3f)]);
    multi.Read(block, 0, 960);
    Check(Math.Abs(block[500] - 0.3f) < 1e-5, "multi : retrait d'une application");
}


// --- Effets vocaux -----------------------------------------------------------
static float[] MonoSine(double frequency, float amplitude, int samples)
{
    var data = new float[samples];
    for (int i = 0; i < samples; i++) data[i] = (float)Math.Sin(2 * Math.PI * frequency * i / 48000) * amplitude;
    return data;
}

static int Crossings(float[] data, int from)
{
    int crossings = 0;
    for (int i = from + 1; i < data.Length; i++)
        if (data[i - 1] < 0 && data[i] >= 0) crossings++;
    return crossings;
}

static void ProcessBlocks(VoiceEffect effect, float[] data)
{
    for (int i = 0; i < data.Length; i += 480)
        effect.Process(data.AsSpan(i, Math.Min(480, data.Length - i)));
}

{
    // Hauteur : +12 demi-tons double la fréquence, -12 la divise par deux, sans changer la durée.
    var pitch = new PitchShiftEffect(48000);
    pitch.Parameters[0].Value = 12f;
    var up = MonoSine(300, 0.5f, 96000);
    ProcessBlocks(pitch, up);
    int upHz = Crossings(up, 48000);
    Check(Math.Abs(upHz - 600) <= 30, $"hauteur : 300 Hz + 12 demi-tons ≈ 600 Hz ({upHz} Hz)");

    pitch.Reset();
    pitch.Parameters[0].Value = -12f;
    var down = MonoSine(600, 0.5f, 96000);
    ProcessBlocks(pitch, down);
    int downHz = Crossings(down, 48000);
    Check(Math.Abs(downHz - 300) <= 20, $"hauteur : 600 Hz - 12 demi-tons ≈ 300 Hz ({downHz} Hz)");
    Check(PeakOf(down, 48000) > 0.3f && PeakOf(down, 48000) < 0.75f, "hauteur : niveau conservé");

    // Filtre passe-bas à 500 Hz : laisse passer 100 Hz, atténue fortement 5000 Hz.
    var filter = new FilterEffect(48000);
    filter.Parameters[1].Value = 500f;
    var low = MonoSine(100, 0.5f, 48000);
    ProcessBlocks(filter, low);
    filter.Reset();
    var high = MonoSine(5000, 0.5f, 48000);
    ProcessBlocks(filter, high);
    Check(PeakOf(low, 24000) > 0.45f && PeakOf(high, 24000) < 0.02f, "filtre : passe-bas");

    // Téléphone : coupe les graves et les aigus, garde les médiums.
    var phone = new TelephoneEffect(48000);
    var bass = MonoSine(60, 0.5f, 48000);
    ProcessBlocks(phone, bass);
    phone.Reset();
    var mid = MonoSine(1000, 0.5f, 48000);
    ProcessBlocks(phone, mid);
    phone.Reset();
    var treble = MonoSine(10000, 0.5f, 48000);
    ProcessBlocks(phone, treble);
    Check(PeakOf(bass, 24000) < 0.03f && PeakOf(mid, 24000) > 0.4f && PeakOf(treble, 24000) < 0.03f,
        "téléphone : bande 300–3400 Hz");

    // Écho : une impulsion revient après le délai réglé.
    var echo = new EchoEffect(48000);
    echo.Parameters[0].Value = 100f;
    echo.Parameters[1].Value = 0f;
    echo.Parameters[2].Value = 0.5f;
    var impulse = new float[9600];
    impulse[0] = 1f;
    ProcessBlocks(echo, impulse);
    Check(impulse[0] == 1f && Math.Abs(impulse[4800] - 0.5f) < 1e-5 && impulse[2400] == 0f,
        "écho : répétition après 100 ms");

    // Réverbération : le son continue après la fin du signal, puis s'éteint.
    var reverb = new ReverbEffect(48000);
    var burst = new float[96000];
    Array.Copy(MonoSine(440, 0.5f, 4800), burst, 4800);
    ProcessBlocks(reverb, burst);
    float tail = PeakOf(burst[9600..19200]);
    Check(tail > 0.001f && PeakOf(burst[90000..]) < tail, "réverbération : queue qui s'éteint");

    // Distorsion et robot : sortie bornée, signal modifié.
    var distortion = new DistortionEffect();
    var loud = MonoSine(440, 1f, 4800);
    ProcessBlocks(distortion, loud);
    Check(PeakOf(loud) <= 0.61f, "distorsion : niveau de sortie maîtrisé");
    var robot = new RobotEffect(48000);
    var voice = MonoSine(200, 0.5f, 48000);
    ProcessBlocks(robot, voice);
    Check(PeakOf(voice) <= 0.81f && Crossings(voice, 0) != 200, "robot : voix modulée");
}

// --- Noise gate --------------------------------------------------------------
{
    var gate = new NoiseGate(48000) { Enabled = true, ThresholdDb = -40f };
    var noise = MonoSine(200, 0.002f, 48000); // -54 dB : bruit de fond
    for (int i = 0; i < noise.Length; i += 480) gate.Process(noise.AsSpan(i, 480));
    Check(PeakOf(noise, 24000) < 0.0001f && !gate.IsOpen, "noise gate : bruit de fond coupé");
    var speech = MonoSine(200, 0.3f, 48000);
    for (int i = 0; i < speech.Length; i += 480) gate.Process(speech.AsSpan(i, 480));
    Check(PeakOf(speech, 2400) > 0.29f && gate.IsOpen, "noise gate : la voix passe");
}

// --- Chaîne d'effets ---------------------------------------------------------
{
    var chain = new VoiceEffectsChain { Source = new SineSource(440, 0.5f) };
    var reference = Pull(new SineSource(440, 0.5f), 4800);
    var untouched = Pull(chain, 4800);
    Check(reference.AsSpan().SequenceEqual(untouched), "effets : aucun effet actif = signal intact");

    chain.FindEffect("distortion")!.Enabled = true;
    var changed = Pull(chain, 4800);
    chain.EffectsEnabled = false;
    chain.Source = new SineSource(440, 0.5f);
    var bypassed = Pull(chain, 4800);
    Check(!reference.AsSpan().SequenceEqual(changed) && reference.AsSpan().SequenceEqual(bypassed),
        "effets : interrupteur général");

    var service = new VoiceStudio.Audio.Services.VoiceEffectsService();
    var presets = VoicePreset.CreateBuiltIns();
    bool presetsValid = presets.All(preset => preset.Effects.All(e =>
        service.Chain.FindEffect(e.Key) is { } effect &&
        e.Value.Parameters.Keys.All(id => effect.FindParameter(id) != null)));
    Check(presetsValid && presets.Count >= 6, "presets : tous les presets fournis sont valides");

    service.ApplyPreset(presets.First(p => p.Name == "Démon"));
    var captured = service.CapturePreset("Test");
    Check(captured.Effects.Count == 3 && captured.Effects["pitch"].Parameters["semitones"] == -8f,
        "presets : application puis capture");
    string json = System.Text.Json.JsonSerializer.Serialize(captured);
    var restored = System.Text.Json.JsonSerializer.Deserialize<VoicePreset>(json)!;
    Check(restored.Effects["pitch"].Parameters["semitones"] == -8f, "presets : sauvegarde JSON");
    service.Reset();
    Check(service.Chain.Effects.All(e => !e.Enabled), "presets : réinitialisation");

    // Tous les effets ensemble pendant 2 s : sortie finie et bornée.
    var all = new VoiceEffectsChain { Source = new SineSource(220, 0.8f) };
    all.Gate.Enabled = true;
    foreach (var effect in all.Effects) effect.Enabled = true;
    var stress = Pull(new MixerEngine(new MixerChannel("V", 48000) { Source = all }), 96000);
    Check(stress.All(float.IsFinite) && PeakOf(stress) <= 1f, "effets : tous combinés, sortie stable");
}

// --- Auto-ducking ------------------------------------------------------------
{
    var voiceSource = new SwitchSource();
    var voice = new MixerChannel("Voix", 48000) { Source = voiceSource };
    var music = new MixerChannel("Musique", 48000) { Source = new ConstantSource(0.5f) };
    var mixer = new MixerEngine(voice, music);
    mixer.ConfigureDucking(voice, music);
    mixer.Ducker.MinimumVolume = 0.2f;

    voiceSource.Level = 0.3f;
    Pull(mixer, 48000);
    Check(mixer.Ducker.CurrentGain == 1f, "ducking : désactivé par défaut");

    mixer.Ducker.Enabled = true;
    Pull(mixer, 48000);
    Check(Math.Abs(mixer.Ducker.CurrentGain - 0.2f) < 0.01f, "ducking : la musique baisse quand on parle");

    voiceSource.Level = 0f;
    Pull(mixer, 9600);
    Check(mixer.Ducker.CurrentGain < 0.5f, "ducking : maintien entre deux mots");
    var after = Pull(mixer, 48000 * 4);
    Check(mixer.Ducker.CurrentGain > 0.99f && Math.Abs(after[^1] - 0.5f) < 5e-3,
        "ducking : la musique remonte quand on se tait");

    // Push-to-talk : ExternalGain à 0 coupe le micro.
    voiceSource.Level = 0.3f;
    voice.ExternalGain = 0f;
    var silent = Pull(mixer, 48000 * 4);
    Check(Math.Abs(silent[^1] - 0.5f) < 5e-3, "push-to-talk : micro coupé sans appui (et pas de ducking)");
}

// --- Soundboard --------------------------------------------------------------
{
    var board = new SoundboardMixer { MaxSimultaneous = 2 };
    var sound = new float[9600];
    Array.Fill(sound, 0.25f);
    var block = new float[960];

    Check(board.Play("a", sound, 1f) && board.Play("b", sound, 2f), "soundboard : deux sons lancés");
    Check(!board.Play("c", sound, 1f), "soundboard : limite de sons simultanés respectée");
    board.Read(block, 0, 960);
    Check(Math.Abs(block[100] - 0.75f) < 1e-5, "soundboard : sons superposés avec leur volume");

    board.Stop("b");
    for (int i = 0; i < 3; i++) board.Read(block, 0, 960);
    Check(Math.Abs(block[900] - 0.25f) < 1e-5 && !board.IsPlaying("b") && board.IsPlaying("a"),
        "soundboard : arrêt d'un son");

    for (int i = 0; i < 12; i++) board.Read(block, 0, 960);
    Check(board.ActiveCount == 0 && PeakOf(block) == 0, "soundboard : fin de lecture, emplacement libéré");

    var resampled = VoiceStudio.Audio.Services.SoundboardService.ReadAll(new MonoClip(22050, 22050));
    Check(Math.Abs(resampled.Length - 96000) < 400, "soundboard : fichier mono 22 kHz converti en 48 kHz stéréo");
}

Console.WriteLine(failures == 0 ? "\nTous les tests passent." : $"\n{failures} test(s) en échec.");
return failures == 0 ? 0 : 1;

sealed class ConstantSource(float value) : ISampleProvider
{
    public WaveFormat WaveFormat => AudioConstants.MixFormat;
    public int Read(float[] buffer, int offset, int count)
    {
        Array.Fill(buffer, value, offset, count);
        return count;
    }
}

sealed class SineSource(double frequency, float amplitude) : ISampleProvider
{
    private double _phase;
    public WaveFormat WaveFormat => AudioConstants.MixFormat;
    public int Read(float[] buffer, int offset, int count)
    {
        for (int i = 0; i < count; i += 2)
        {
            float sample = (float)Math.Sin(_phase) * amplitude;
            _phase += 2 * Math.PI * frequency / AudioConstants.SampleRate;
            buffer[offset + i] = sample;
            buffer[offset + i + 1] = sample;
        }
        return count;
    }
}

sealed class SwitchSource : ISampleProvider
{
    public volatile float Level;
    public WaveFormat WaveFormat => AudioConstants.MixFormat;
    public int Read(float[] buffer, int offset, int count)
    {
        Array.Fill(buffer, Level, offset, count);
        return count;
    }
}

sealed class MonoClip(int sampleRate, int samples) : ISampleProvider
{
    private int _position;
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
    public int Read(float[] buffer, int offset, int count)
    {
        int n = Math.Min(count, samples - _position);
        Array.Fill(buffer, 0.5f, offset, n);
        _position += n;
        return n;
    }
}
