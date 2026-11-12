namespace VoiceStudio.Audio.Services;

/// <summary>Résultat d'un test micro : les niveaux mesurés et le conseil qui en découle.</summary>
public sealed record MicrophoneTestResult(float PeakDb, float RmsDb, float SuggestedGainDb, string Verdict)
{
    /// <summary>Niveau visé pour une voix confortable, en dBFS.</summary>
    public const float TargetRmsDb = -20f;

    /// <summary>Marge de sécurité : les crêtes ne doivent pas dépasser ce niveau.</summary>
    public const float MaximumPeakDb = -6f;

    /// <summary>Analyse un enregistrement de voix et propose un gain.</summary>
    public static MicrophoneTestResult Analyse(float[] samples, float currentGainDb)
    {
        if (samples.Length == 0)
            return new MicrophoneTestResult(-120f, -120f, currentGainDb, "Rien n'a été enregistré.");

        // Seuls les passages où tu parles comptent : les silences fausseraient la moyenne.
        float peak = 0f;
        for (int i = 0; i < samples.Length; i++) peak = MathF.Max(peak, MathF.Abs(samples[i]));

        float gate = peak * 0.12f;
        double sum = 0;
        int counted = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            float value = samples[i];
            if (MathF.Abs(value) < gate) continue;
            sum += value * (double)value;
            counted++;
        }

        float rms = counted > 0 ? (float)Math.Sqrt(sum / counted) : 0f;
        float peakDb = ToDecibels(peak);
        float rmsDb = ToDecibels(rms);

        if (peak < 0.003f)
            return new MicrophoneTestResult(peakDb, rmsDb, currentGainDb,
                "Je n'ai presque rien entendu. Vérifie que le bon micro est sélectionné et qu'il n'est pas coupé.");

        // On vise le niveau confortable, sans laisser les crêtes approcher la saturation.
        float wanted = TargetRmsDb - rmsDb;
        float headroom = MaximumPeakDb - peakDb;
        float change = MathF.Min(wanted, headroom);
        float suggested = Math.Clamp(currentGainDb + change, -12f, 24f);

        string verdict = peakDb > -1f
            ? "Ton micro sature déjà avant Voice Studio. Baisse le niveau d'entrée dans Windows, ou éloigne-toi un peu."
            : change switch
            {
                > 3f => "Tu es trop discret : le gain proposé te remontera au bon niveau.",
                < -3f => "Tu es un peu fort : le gain proposé laisse plus de marge avant saturation.",
                _ => "Ton niveau est bon, il n'y a rien à changer."
            };

        return new MicrophoneTestResult(peakDb, rmsDb, suggested, verdict);
    }

    private static float ToDecibels(float value) =>
        value <= 1e-6f ? -120f : 20f * MathF.Log10(value);
}
