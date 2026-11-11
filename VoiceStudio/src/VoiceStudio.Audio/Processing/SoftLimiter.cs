namespace VoiceStudio.Audio.Processing;

/// <summary>
/// Limiteur de sortie : empêche la saturation quand la somme des sources dépasse 0 dBFS.
///
/// Il regarde le son un court instant en avance (environ 3 ms) et ramène son gain
/// progressivement avant que la crête n'arrive. Cette anticipation est essentielle :
/// un limiteur qui corrige le gain d'un échantillon à l'autre déforme l'onde elle-même
/// et s'entend comme une saturation, exactement ce qu'il devait éviter.
/// </summary>
public sealed class SoftLimiter
{
    private readonly float[] _delay;
    private readonly int _delayFrames;
    private readonly float _threshold;
    private readonly float _attackCoefficient;
    private readonly float _releaseCoefficient;
    private int _write;
    private float _gain = 1f;
    private float _blockPeak;
    private float _previousBlockPeak;
    private int _blockCounter;

    public SoftLimiter(int sampleRate, float threshold = 0.95f,
        float lookAheadMilliseconds = 3f, float releaseMilliseconds = 200f)
    {
        _threshold = threshold;
        _delayFrames = Math.Max(16, (int)(sampleRate * lookAheadMilliseconds / 1000f));
        _delay = new float[_delayFrames * 2];

        // L'attaque est calée sur l'anticipation : le gain atteint sa valeur
        // au moment précis où la crête se présente.
        float attackSeconds = lookAheadMilliseconds / 1000f / 3f;
        _attackCoefficient = 1f - MathF.Exp(-1f / (sampleRate * attackSeconds));
        _releaseCoefficient = 1f - MathF.Exp(-1f / (sampleRate * releaseMilliseconds / 1000f));
    }

    /// <summary>Réduction de gain actuelle (1 = aucune réduction).</summary>
    public float CurrentGain => Volatile.Read(ref _gain);

    /// <summary>Retard introduit par l'anticipation, en échantillons par canal.</summary>
    public int LatencyFrames => _delayFrames;

    public void Process(float[] buffer, int offset, int count)
    {
        float gain = _gain;
        int end = offset + count - 1;

        for (int i = offset; i < end; i += 2)
        {
            float left = buffer[i];
            float right = buffer[i + 1];

            // Crête la plus forte de tout ce qui attend dans la ligne à retard :
            // c'est elle qui décide du gain, pas l'échantillon du moment. Sans cela,
            // un passage calme ferait remonter le gain juste avant une crête retenue.
            float peak = MathF.Max(MathF.Abs(left), MathF.Abs(right));
            if (peak > _blockPeak) _blockPeak = peak;
            if (++_blockCounter >= _delayFrames)
            {
                _previousBlockPeak = _blockPeak;
                _blockPeak = 0f;
                _blockCounter = 0;
            }

            float windowPeak = MathF.Max(_previousBlockPeak, _blockPeak);
            float target = windowPeak > _threshold ? _threshold / windowPeak : 1f;

            // Descente rapide mais progressive, remontée lente.
            gain += (target - gain) * (target < gain ? _attackCoefficient : _releaseCoefficient);

            // On joue le son retenu pendant ce temps, avec le gain déjà en place.
            int read = _write * 2;
            float delayedLeft = _delay[read];
            float delayedRight = _delay[read + 1];
            _delay[read] = left;
            _delay[read + 1] = right;
            if (++_write == _delayFrames) _write = 0;

            buffer[i] = Math.Clamp(delayedLeft * gain, -1f, 1f);
            buffer[i + 1] = Math.Clamp(delayedRight * gain, -1f, 1f);
        }

        Volatile.Write(ref _gain, gain);
    }

    public void Reset()
    {
        Array.Clear(_delay);
        _write = 0;
        _blockPeak = 0f;
        _previousBlockPeak = 0f;
        _blockCounter = 0;
        Volatile.Write(ref _gain, 1f);
    }
}
