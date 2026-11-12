namespace VoiceStudio.Audio.Mixing;

/// <summary>Réglages de lecture d'un son du soundboard : découpe, fondus, boucle.</summary>
public sealed class PlaybackOptions
{
    /// <summary>Volume linéaire, 1 = niveau d'origine.</summary>
    public float Volume { get; set; } = 1f;

    /// <summary>Premier échantillon joué (pair : début d'une trame stéréo).</summary>
    public int StartSample { get; set; }

    /// <summary>Échantillon où la lecture s'arrête.</summary>
    public int EndSample { get; set; } = int.MaxValue;

    /// <summary>Durée du fondu d'entrée et de sortie, en échantillons.</summary>
    public int FadeSamples { get; set; }

    /// <summary>Rejouer en boucle jusqu'à l'arrêt.</summary>
    public bool Loop { get; set; }

    /// <summary>Ajuste les bornes à la longueur réelle du son.</summary>
    public PlaybackOptions Clamp(int length)
    {
        int start = Math.Clamp(StartSample, 0, Math.Max(0, length - 2));
        int end = Math.Clamp(EndSample, start + 2, length);
        return new PlaybackOptions
        {
            Volume = Math.Clamp(Volume, 0f, 2f),
            StartSample = start - (start % 2),
            EndSample = end - (end % 2),
            FadeSamples = Math.Clamp(FadeSamples, 0, (end - start) / 2),
            Loop = Loop
        };
    }

    /// <summary>Gain du fondu à cette position : monte au début, descend à la fin.</summary>
    public float EnvelopeAt(int position)
    {
        if (FadeSamples <= 0) return 1f;
        int sinceStart = position - StartSample;
        int untilEnd = EndSample - position;
        if (sinceStart < FadeSamples) return sinceStart / (float)FadeSamples;
        if (untilEnd < FadeSamples) return untilEnd / (float)FadeSamples;
        return 1f;
    }
}
