using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using VoiceStudio.Audio.Devices;

namespace VoiceStudio.Audio.Services;

/// <summary>Liste les périphériques audio et signale les branchements / débranchements.</summary>
public sealed class AudioDeviceService : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly NotificationClient _notificationClient;

    public AudioDeviceService()
    {
        _notificationClient = new NotificationClient(this);
        _enumerator.RegisterEndpointNotificationCallback(_notificationClient);
    }

    /// <summary>Un périphérique a été ajouté, retiré ou a changé d'état. Levé sur un thread système.</summary>
    public event Action? DevicesChanged;

    public IReadOnlyList<AudioDeviceInfo> GetMicrophones() => Enumerate(DataFlow.Capture);

    public IReadOnlyList<AudioDeviceInfo> GetOutputs() => Enumerate(DataFlow.Render);

    /// <summary>Cherche l'entrée de VB-CABLE (« CABLE Input ») parmi les sorties.</summary>
    public AudioDeviceInfo? FindVirtualCable() =>
        GetOutputs().FirstOrDefault(device => device.IsVirtualCable);

    /// <summary>Ouvre un périphérique actif par identifiant. Retourne null s'il est absent ou désactivé.</summary>
    public MMDevice? OpenDevice(string id)
    {
        try
        {
            var device = _enumerator.GetDevice(id);
            if (device.State == DeviceState.Active) return device;
            device.Dispose();
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Niveau d'entrée réglé dans Windows pour ce micro, de 0 à 1. Null si inaccessible.
    /// C'est le même réglage que « Paramètres → Son → Entrée → Volume ».
    /// </summary>
    public float? GetInputLevel(string deviceId)
    {
        try
        {
            using var device = OpenDevice(deviceId);
            return device?.AudioEndpointVolume.MasterVolumeLevelScalar;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Change le niveau d'entrée Windows de ce micro. Retourne false si impossible.</summary>
    public bool SetInputLevel(string deviceId, float level)
    {
        try
        {
            using var device = OpenDevice(deviceId);
            if (device == null) return false;
            device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(level, 0f, 1f);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Vrai si ce micro est un périphérique virtuel créé par un logiciel de routage
    /// (SteelSeries Sonar, VoiceMeeter, VB-CABLE…). Ces entrées ajoutent un étage de
    /// traitement et leur horloge est moins régulière : les craquements y sont fréquents.
    /// </summary>
    public static bool LooksLikeVirtualInput(string name)
    {
        string[] markers = ["sonar", "voicemeeter", "vb-audio", "cable output", "virtual", "nahimic"];
        foreach (string marker in markers)
            if (name.Contains(marker, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public static bool LooksLikeVirtualCable(string name) =>
        name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("VoiceMeeter Input", StringComparison.OrdinalIgnoreCase);

    private List<AudioDeviceInfo> Enumerate(DataFlow flow)
    {
        var result = new List<AudioDeviceInfo>();
        string? defaultId = null;
        try
        {
            if (_enumerator.HasDefaultAudioEndpoint(flow, Role.Console))
            {
                using var defaultDevice = _enumerator.GetDefaultAudioEndpoint(flow, Role.Console);
                defaultId = defaultDevice.ID;
            }
        }
        catch
        {
            // pas de périphérique par défaut
        }

        foreach (var device in _enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            try
            {
                string name = device.FriendlyName;
                result.Add(new AudioDeviceInfo(device.ID, name, device.ID == defaultId,
                    LooksLikeVirtualCable(name)));
            }
            catch
            {
                // périphérique retiré pendant l'énumération
            }
            finally
            {
                device.Dispose();
            }
        }
        return result;
    }

    public void Dispose()
    {
        try { _enumerator.UnregisterEndpointNotificationCallback(_notificationClient); } catch { }
        _enumerator.Dispose();
    }

    private sealed class NotificationClient(AudioDeviceService owner) : IMMNotificationClient
    {
        public void OnDeviceStateChanged(string deviceId, DeviceState newState) => owner.DevicesChanged?.Invoke();
        public void OnDeviceAdded(string pwstrDeviceId) => owner.DevicesChanged?.Invoke();
        public void OnDeviceRemoved(string deviceId) => owner.DevicesChanged?.Invoke();
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) => owner.DevicesChanged?.Invoke();
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }
}
