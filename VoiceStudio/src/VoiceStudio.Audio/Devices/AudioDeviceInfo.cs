namespace VoiceStudio.Audio.Devices;

/// <summary>Description d'un périphérique audio, indépendante de NAudio (utilisable par l'interface).</summary>
public sealed record AudioDeviceInfo(string Id, string Name, bool IsDefault, bool IsVirtualCable)
{
    public override string ToString() => Name;
}
