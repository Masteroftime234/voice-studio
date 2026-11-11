namespace VoiceStudio.Audio.Effects;

/// <summary>
/// Suppression du bruit de fond (souffle, ventilateur, bourdonnement), comme Krisp dans Discord.
///
/// Principe : le signal est découpé en fenêtres qui se recouvrent et analysé par bandes de fréquences.
/// Dans chaque bande, le niveau du bruit permanent est estimé en continu ; les bandes où le signal
/// dépasse à peine ce bruit sont atténuées, celles où la voix domine sont laissées intactes.
/// Un filtre coupe aussi les très basses fréquences (vibrations du bureau, souffle du micro).
///
/// Le signal ressort retardé d'environ 21 ms, que l'effet soit actif ou non, pour qu'activer
/// ou désactiver la suppression ne provoque aucun saut.
/// </summary>
public sealed class NoiseSuppressor
{
    private const int FrameSize = 1024;
    private const int HopSize = 256;
    private const int Bins = FrameSize / 2 + 1;
    private const float Normalisation = 1f / 1.5f; // somme des fenêtres au carré, saut = taille / 4
    private const int Windows = 3;                 // sous-fenêtres de recherche du minimum
    private const int WindowFrames = 90;           // ~0,5 s chacune, soit ~1,4 s de mémoire
    private const float MinimumBias = 2.3f;        // un minimum est toujours sous le niveau moyen

    private readonly float[] _window = new float[FrameSize];
    private readonly float[] _slidingInput = new float[FrameSize];
    private readonly float[] _overlap = new float[FrameSize];
    private readonly float[] _pending = new float[HopSize];
    private readonly float[] _ready = new float[HopSize];
    private readonly float[] _real = new float[FrameSize];
    private readonly float[] _imaginary = new float[FrameSize];
    private readonly float[] _power = new float[Bins];
    private readonly float[] _runningMinimum = new float[Bins];
    private readonly float[,] _windowMinimum = new float[Windows, Bins];
    private readonly float[] _gain = new float[Bins];
    private readonly int _firstVoiceBin;

    private BiQuad _highPass;
    private int _position;
    private int _windowFrame;
    private int _windowSlot;
    private bool _noiseKnown;
    private bool _enabled;
    private float _strength = 0.7f;
    private float _reduction;

    public NoiseSuppressor(int sampleRate)
    {
        for (int i = 0; i < FrameSize; i++)
            _window[i] = 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / FrameSize));
        _firstVoiceBin = Math.Max(1, 70 * FrameSize / sampleRate);
        _highPass.SetHighPass(sampleRate, 85f, 0.7f);
        Array.Fill(_gain, 1f);
    }

    public bool Enabled
    {
        get => Volatile.Read(ref _enabled);
        set => Volatile.Write(ref _enabled, value);
    }

    /// <summary>Intensité : 0 = très léger, 1 = maximum (voix un peu plus sourde).</summary>
    public float Strength
    {
        get => Volatile.Read(ref _strength);
        set => Volatile.Write(ref _strength, Math.Clamp(value, 0f, 1f));
    }

    /// <summary>Atténuation moyenne appliquée en ce moment : 0 = rien, 1 = tout coupé.</summary>
    public float Reduction => Volatile.Read(ref _reduction);

    /// <summary>Traite un bloc en place (mono).</summary>
    public void Process(Span<float> samples)
    {
        bool enabled = Enabled;
        float strength = Strength;
        float floor = 0.18f - 0.16f * strength;         // atténuation maximale d'une bande         // atténuation maximale d'une bande
        float overSubtraction = 1.6f + 3.8f * strength; // marge exigée au-dessus du bruit estimé // marge exigée au-dessus du bruit estimé

        for (int i = 0; i < samples.Length; i++)
        {
            _pending[_position] = enabled ? _highPass.Process(samples[i]) : samples[i];
            samples[i] = _ready[_position];

            if (++_position < HopSize) continue;
            _position = 0;
            ProcessFrame(enabled, floor, overSubtraction);
        }
    }

    private void ProcessFrame(bool enabled, float floor, float overSubtraction)
    {
        Array.Copy(_slidingInput, HopSize, _slidingInput, 0, FrameSize - HopSize);
        Array.Copy(_pending, 0, _slidingInput, FrameSize - HopSize, HopSize);

        for (int i = 0; i < FrameSize; i++)
        {
            _real[i] = _slidingInput[i] * _window[i];
            _imaginary[i] = 0f;
        }

        Fft.Transform(_real, _imaginary, false);

        float totalGain = 0f;
        for (int k = 0; k < Bins; k++)
        {
            float power = _real[k] * _real[k] + _imaginary[k] * _imaginary[k];

            // Puissance lissée : sans ce lissage, les fluctuations du bruit feraient
            // sous-estimer son niveau et l'atténuation resterait faible.
            float smoothed = _noiseKnown ? _power[k] + (power - _power[k]) * 0.4f : power;
            _power[k] = smoothed;

            // Niveau du bruit = plus faible puissance observée sur environ 1,4 seconde.
            // Une pause entre deux mots suffit à le mesurer, et une phrase, même longue,
            // ne le fait pas monter.
            if (!_noiseKnown)
            {
                _runningMinimum[k] = smoothed;
                for (int w = 0; w < Windows; w++) _windowMinimum[w, k] = smoothed;
            }
            else if (smoothed < _runningMinimum[k])
            {
                _runningMinimum[k] = smoothed;
            }

            float minimum = _runningMinimum[k];
            for (int w = 0; w < Windows; w++)
                if (_windowMinimum[w, k] < minimum) minimum = _windowMinimum[w, k];
            float noise = minimum * MinimumBias;

            float target = 1f;
            if (enabled)
            {
                if (k < _firstVoiceBin)
                {
                    target = floor;
                }
                else
                {
                    float excess = smoothed - overSubtraction * noise;
                    target = smoothed > 1e-18f
                        ? Math.Clamp(MathF.Sqrt(MathF.Max(excess, 0f) / smoothed), floor, 1f)
                        : floor;
                }
            }

            // Lissage dans le temps : évite le bruit « métallique » des suppressions trop brusques.
            float gain = _gain[k];
            gain += (target - gain) * (target < gain ? 0.5f : 0.4f);
            _gain[k] = gain;
            totalGain += gain;

            _real[k] *= gain;
            _imaginary[k] *= gain;
        }
        _noiseKnown = true;
        if (++_windowFrame >= WindowFrames)
        {
            _windowFrame = 0;
            for (int k = 0; k < Bins; k++)
            {
                _windowMinimum[_windowSlot, k] = _runningMinimum[k];
                _runningMinimum[k] = _power[k];
            }
            _windowSlot = (_windowSlot + 1) % Windows;
        }
        Volatile.Write(ref _reduction, enabled ? 1f - totalGain / Bins : 0f);

        // Symétrie conjuguée : le signal reconstruit doit rester réel.
        for (int k = 1; k < FrameSize / 2; k++)
        {
            _real[FrameSize - k] = _real[k];
            _imaginary[FrameSize - k] = -_imaginary[k];
        }

        Fft.Transform(_real, _imaginary, true);

        for (int i = 0; i < FrameSize; i++)
            _overlap[i] += _real[i] * _window[i] * Normalisation;

        Array.Copy(_overlap, 0, _ready, 0, HopSize);
        Array.Copy(_overlap, HopSize, _overlap, 0, FrameSize - HopSize);
        Array.Clear(_overlap, FrameSize - HopSize, HopSize);
    }

    public void Reset()
    {
        Array.Clear(_slidingInput);
        Array.Clear(_overlap);
        Array.Clear(_pending);
        Array.Clear(_ready);
        Array.Clear(_power);
        Array.Clear(_runningMinimum);
        Array.Clear(_windowMinimum);
        _windowFrame = 0;
        _windowSlot = 0;
        Array.Fill(_gain, 1f);
        _highPass.Reset();
        _position = 0;
        _noiseKnown = false;
        Volatile.Write(ref _reduction, 0f);
    }
}
