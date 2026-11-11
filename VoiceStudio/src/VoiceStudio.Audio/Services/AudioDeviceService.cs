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
