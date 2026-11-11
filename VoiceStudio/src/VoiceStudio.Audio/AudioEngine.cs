using System.Collections.Concurrent;
using VoiceStudio.Audio.Mixing;
using VoiceStudio.Audio.Processing;
using VoiceStudio.Audio.Services;

namespace VoiceStudio.Audio;

/// <summary>Paramètres de démarrage du moteur.</summary>
public sealed record EngineOptions(string MicrophoneId, string OutputId)
{
    /// <summary>Périphérique du retour audio (null = pas de retour).</summary>
    public string? MonitorId { get; init; }

    /// <summary>Latence demandée pour les sorties, en millisecondes.</summary>
    public int OutputLatencyMs { get; init; } = 40;

    /// <summary>Taille du tampon de capture du micro, en millisecondes.</summary>
    public int CaptureBufferMs { get; init; } = 20;
}

/// <summary>
/// Point d'entrée unique du moteur audio pour l'interface :
///
///   micro → noise gate → effets vocaux ─┬─► tranche Microphone ─┐
///                                       └─► retour audio        │
///   applications du PC (sauf exclues) ────► bus Jeu / Chat / Média / Autres ─┼─► limiteur ─► micro virtuel
///   soundboard ───────────────────────────► tranche Soundboard ─┘
///
/// Ne dépend d'aucun élément graphique.
/// </summary>
public sealed class AudioEngine : IDisposable
{
    private readonly object _gate = new();
    private readonly AudioCaptureService _microphone = new();
    private readonly VirtualMicrophoneService _virtualMicrophone = new();
    private readonly ConcurrentDictionary<string, string> _busByApp = new(StringComparer.OrdinalIgnoreCase);
    private bool _pushToTalkEnabled;
    private bool _pushToTalkPressed;

    public AudioEngine()
    {
        Devices = new AudioDeviceService();
        Mixer = new AudioMixerService();
        SystemAudio = new SystemAudioCaptureService();
        VoiceEffects = new VoiceEffectsService();
        Soundboard = new SoundboardService();
        Monitor = new MonitorService();

        Mixer.Microphone.Source = Monitor.Attach(VoiceEffects.Chain);
        Mixer.Soundboard.Source = Soundboard.Mixer;
        SystemAudio.SourcesChanged += RebuildRouting;

        _microphone.Faulted += ex => OnFault("Le microphone s'est arrêté : " + ex.Message);
        _virtualMicrophone.Faulted += ex => OnFault("La sortie virtuelle s'est arrêtée : " + ex.Message);
    }

    public AudioDeviceService Devices { get; }
    public AudioMixerService Mixer { get; }
    public SystemAudioCaptureService SystemAudio { get; }
    public VoiceEffectsService VoiceEffects { get; }
    public SoundboardService Soundboard { get; }
    public MonitorService Monitor { get; }

    /// <summary>Les bus d'applications (Jeu, Chat, Média, Autres).</summary>
    public IReadOnlyList<AudioBus> Buses => Mixer.Buses;

    /// <summary>
    /// Bus enregistré pour une application, consulté la première fois qu'elle est captée.
    /// Retourner null laisse le classement par défaut décider.
    /// </summary>
    public Func<string, string?>? ResolveSavedBus { get; set; }

    /// <summary>Range une application dans un bus et réapplique le routage.</summary>
    public void AssignApp(string appName, string busId)
    {
        if (Mixer.FindBus(busId) == null) return;
        _busByApp[appName] = busId;
        RebuildRouting();
    }

    /// <summary>Bus dans lequel se trouve une application.</summary>
    public string BusOf(string appName)
    {
        if (_busByApp.TryGetValue(appName, out string? busId)) return busId;
        string resolved = ResolveSavedBus?.Invoke(appName) ?? DefaultBusFor(appName);
        if (Mixer.FindBus(resolved) == null) resolved = "autres";
        _busByApp[appName] = resolved;
        return resolved;
    }

    /// <summary>Auto-ducking des sons du PC par la voix.</summary>
    public Ducker Ducker => Mixer.Engine.Ducker;

    public bool IsRunning { get; private set; }

    /// <summary>
    /// Nombre de coupures audio depuis le démarrage. S'il monte pendant que tu parles,
    /// la machine ne suit pas : augmenter la latence de sortie règle généralement le problème.
    /// </summary>
    public long Dropouts => _microphone.Dropouts;

    /// <summary>Dernière erreur à afficher à l'utilisateur (null si tout va bien).</summary>
    public string? LastError { get; private set; }

    /// <summary>Avertissement non bloquant (ex. retour audio indisponible).</summary>
    public string? LastWarning { get; private set; }

    /// <summary>Levé (éventuellement sur un thread d'arrière-plan) quand l'état change.</summary>
    public event Action? StateChanged;

    /// <summary>Push-to-talk : si activé, le micro n'est transmis que pendant l'appui.</summary>
    public bool PushToTalkEnabled
    {
        get => _pushToTalkEnabled;
        set
        {
            _pushToTalkEnabled = value;
            UpdateMicrophoneGate();
        }
    }

    /// <summary>État de la touche push-to-talk (fourni par l'interface).</summary>
    public bool PushToTalkPressed
    {
        get => _pushToTalkPressed;
        set
        {
            _pushToTalkPressed = value;
            UpdateMicrophoneGate();
        }
    }

    /// <summary>Classement de départ, d'après le nom du processus.</summary>
    private static string DefaultBusFor(string appName)
    {
        string[] media =
        [
            "spotify", "chrome", "msedge", "firefox", "opera", "brave", "vivaldi", "vlc",
            "wmplayer", "itunes", "deezer", "music", "youtube", "netflix", "mpc-hc", "potplayer"
        ];
        string[] chat = ["teamspeak", "ts3client", "zoom", "teams", "skype", "mumble", "slack", "whatsapp"];

        foreach (string name in media)
            if (appName.Contains(name, StringComparison.OrdinalIgnoreCase)) return "media";
        foreach (string name in chat)
            if (appName.Contains(name, StringComparison.OrdinalIgnoreCase)) return "chat";
        return "autres";
    }

    /// <summary>Redistribue les applications captées dans leurs bus respectifs.</summary>
    private void RebuildRouting()
    {
        var sources = SystemAudio.Sources;
        foreach (var bus in Mixer.Buses)
            bus.SetSources(sources.Where(source => BusOf(source.Name) == bus.Id).ToArray());
    }

    private void UpdateMicrophoneGate() =>
        Mixer.Microphone.ExternalGain = !_pushToTalkEnabled || _pushToTalkPressed ? 1f : 0f;

    public void Start(string microphoneId, string outputId, int latencyMilliseconds) =>
        Start(new EngineOptions(microphoneId, outputId) { OutputLatencyMs = latencyMilliseconds });

    /// <summary>Démarre tout le flux. Lève une exception avec un message lisible en cas d'échec.</summary>
    public void Start(EngineOptions options)
    {
        lock (_gate)
        {
            StopCore();
            LastError = null;
            LastWarning = null;

            var output = Devices.OpenDevice(options.OutputId)
                         ?? throw new InvalidOperationException(
                             "Le périphérique de sortie virtuelle est introuvable ou désactivé.");
            var microphone = Devices.OpenDevice(options.MicrophoneId);
            if (microphone == null)
            {
                output.Dispose();
                throw new InvalidOperationException("Le microphone est introuvable ou désactivé.");
            }

            try
            {
                VoiceEffects.Chain.Source = _microphone.Start(microphone, options.CaptureBufferMs);
            }
            catch (Exception ex)
            {
                microphone.Dispose();
                output.Dispose();
                StopCore();
                throw new InvalidOperationException("Impossible d'ouvrir le microphone : " + ex.Message, ex);
            }

            try
            {
                _virtualMicrophone.Start(output, Mixer.Engine, options.OutputLatencyMs);
            }
            catch (Exception ex)
            {
                output.Dispose();
                StopCore();
                throw new InvalidOperationException("Impossible d'ouvrir la sortie virtuelle : " + ex.Message, ex);
            }

            if (options.MonitorId != null) StartMonitorCore(options.MonitorId, options.OutputLatencyMs);

            SystemAudio.Start();
            IsRunning = true;
        }
        StateChanged?.Invoke();
    }

    /// <summary>Active ou coupe le retour audio pendant que le moteur tourne (null = coupé).</summary>
    public void SetMonitor(string? deviceId, int latencyMilliseconds)
    {
        lock (_gate)
        {
            Monitor.Stop();
            LastWarning = null;
            if (IsRunning && deviceId != null) StartMonitorCore(deviceId, latencyMilliseconds);
        }
        StateChanged?.Invoke();
    }

    private void StartMonitorCore(string deviceId, int latencyMilliseconds)
    {
        var device = Devices.OpenDevice(deviceId);
        if (device == null)
        {
            LastWarning = "Retour audio : périphérique introuvable.";
            return;
        }
        try
        {
            Monitor.Start(device, latencyMilliseconds);
        }
        catch (Exception ex)
        {
            device.Dispose();
            LastWarning = "Retour audio indisponible : " + ex.Message;
        }
    }

    public void Stop()
    {
        lock (_gate) StopCore();
        StateChanged?.Invoke();
    }

    private void StopCore()
    {
        IsRunning = false;
        _virtualMicrophone.Stop();
        Monitor.Stop();
        VoiceEffects.Chain.Source = null;
        _microphone.Stop();
        SystemAudio.Stop();
        Soundboard.StopAll();
    }

    private void OnFault(string message)
    {
        // Jamais d'arrêt depuis le thread audio lui-même : on passe par le pool de threads.
        ThreadPool.QueueUserWorkItem(_ =>
        {
            lock (_gate)
            {
                if (!IsRunning) return;
                StopCore();
                LastError = message;
            }
            StateChanged?.Invoke();
        });
    }

    public void Dispose()
    {
        lock (_gate) StopCore();
        SystemAudio.Dispose();
        Monitor.Dispose();
        _microphone.Dispose();
        _virtualMicrophone.Dispose();
        Devices.Dispose();
    }
}
