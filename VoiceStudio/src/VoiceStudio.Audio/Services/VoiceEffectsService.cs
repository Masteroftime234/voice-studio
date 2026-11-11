using VoiceStudio.Audio.Effects;

namespace VoiceStudio.Audio.Services;

/// <summary>Pilotage des effets vocaux : presets, réinitialisation, capture de la configuration courante.</summary>
public sealed class VoiceEffectsService
{
    public VoiceEffectsChain Chain { get; } = new();

    /// <summary>Applique un preset : les effets absents du preset sont désactivés.</summary>
    public void ApplyPreset(VoicePreset preset)
    {
        foreach (var effect in Chain.Effects)
        {
            if (preset.Effects.TryGetValue(effect.Id, out var state))
            {
                foreach (var parameter in effect.Parameters)
                    parameter.Value = state.Parameters.TryGetValue(parameter.Id, out float value)
                        ? value
                        : parameter.Default;
                effect.Enabled = state.Enabled;
            }
            else
            {
                effect.Enabled = false;
            }
        }
    }

    /// <summary>Enregistre la configuration actuelle sous forme de preset.</summary>
    public VoicePreset CapturePreset(string name)
    {
        var preset = new VoicePreset { Name = name };
        foreach (var effect in Chain.Effects)
        {
            if (!effect.Enabled) continue;
            preset.Effects[effect.Id] = new EffectState
            {
                Enabled = true,
                Parameters = effect.Parameters.ToDictionary(p => p.Id, p => p.Value)
            };
        }
        return preset;
    }

    /// <summary>Désactive tous les effets et remet leurs réglages par défaut.</summary>
    public void Reset()
    {
        foreach (var effect in Chain.Effects)
        {
            effect.Enabled = false;
            foreach (var parameter in effect.Parameters) parameter.Value = parameter.Default;
        }
    }
}
