using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;
using VoiceStudio.Audio.Capture;
using VoiceStudio.Audio.Mixing;
using VoiceStudio.Audio.Processing;

namespace VoiceStudio.Audio.Services;

public enum AppCaptureState
{
    Stopped,
    Waiting,
    Capturing,
    Error
}

/// <summary>
/// Capture « tout ce que j'écoute » : le son de chaque application qui joue de l'audio sur le PC
/// (Spotify, navigateur, jeux, lecteur vidéo...), SAUF les applications exclues.
///
/// Pourquoi application par application plutôt que « toute la sortie du PC » :
/// - Discord doit être exclu, sinon les voix des amis leur seraient renvoyées en écho ;
/// - Voice Studio doit être exclu, sinon il capturerait son propre mix (larsen).
/// Windows ne permet d'exclure qu'un seul processus par capture globale ; on capture donc
/// chaque application séparément (Application Loopback) et on additionne.
/// </summary>
public sealed class SystemAudioCaptureService : IDisposable
{
    public const int MaxCapturedApps = 8;
    private const int ScanIntervalMs = 1500;
    private const float SilenceThreshold = 0.0004f;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    /// <summary>Une application reste dans le mixeur ce temps-là après s'être tue.</summary>
    private static readonly TimeSpan KeepAfterSilence = TimeSpan.FromSeconds(30);

    public static readonly string[] DefaultExcludedApps =
        ["Discord", "DiscordPTB", "DiscordCanary", "DiscordDevelopment"];

    private readonly object _gate = new();
    private readonly ManualResetEventSlim _wake = new(false);
    private readonly int _ownProcessId = Environment.ProcessId;
    private Thread? _monitorThread;
    private volatile bool _stopRequested;
    private volatile string[] _excludedApps = DefaultExcludedApps;
    private AppAudioSource[] _sources = [];
    private string[] _excludedPlaying = [];
    private readonly Dictionary<int, string?> _names = new();
    private readonly Dictionary<int, DateTime> _lastSound = new();
    private HashSet<int> _lastRaw = [];
    private HashSet<int> _lastResult = [];
    private string[]? _lastExcluded;

    public AppCaptureState State { get; private set; } = AppCaptureState.Stopped;

    /// <summary>Message lisible décrivant l'état (affiché dans l'interface).</summary>
    public string StatusMessage { get; private set; } = "Arrêté";

    /// <summary>Levé sur un thread d'arrière-plan à chaque changement d'état.</summary>
    public event Action? StateChanged;

    /// <summary>Levé quand une application entre dans le mix ou en sort.</summary>
    public event Action? SourcesChanged;

    /// <summary>Les applications capturées en ce moment, chacune avec son volume.</summary>
    public IReadOnlyList<AppAudioSource> Sources => Volatile.Read(ref _sources);

    /// <summary>
    /// Volume et état « coupé » à appliquer à une application dès qu'elle est captée,
    /// pour retrouver les réglages d'une session à l'autre. Clé = nom du processus.
    /// </summary>
    public Func<string, (float Volume, bool Muted)>? ResolveSavedLevels { get; set; }

    /// <summary>Noms de processus (sans .exe) à ne jamais capturer.</summary>
    public IReadOnlyList<string> ExcludedApps
    {
        get => _excludedApps;
        set
        {
            _excludedApps = value.Select(name => name.Trim())
                .Select(name => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name)
                .Where(name => name.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            _wake.Set();
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_monitorThread != null) return;
            _stopRequested = false;
            _wake.Reset();
            _monitorThread = new Thread(MonitorLoop) { IsBackground = true, Name = "VoiceStudio.SystemAudioMonitor" };
            _monitorThread.SetApartmentState(ApartmentState.MTA);
            _monitorThread.Start();
        }
    }

    public void Stop()
    {
        Thread? thread;
        lock (_gate)
        {
            thread = _monitorThread;
            _monitorThread = null;
            _stopRequested = true;
            _wake.Set();
        }
        if (thread != null && thread != Thread.CurrentThread) thread.Join(5000);
        SetState(AppCaptureState.Stopped, "Arrêté");
    }

    private sealed class Slot
    {
        public Slot(int processId, string name)
        {
            var pipeline = new CaptureSourcePipeline(AudioConstants.SampleRate, AudioConstants.Channels);
            Source = new AppAudioSource(name, processId, pipeline);
        }

        public AppAudioSource Source { get; }
        public int ProcessId => Source.ProcessId;
        public string Name => Source.Name;
        public ProcessLoopbackCapture? Capture { get; set; }
        public SampleKind Kind { get; set; }
        public volatile bool Faulted;

        // Thread de capture : pas d'allocation, pas de verrou.
        public void OnData(ReadOnlySpan<byte> data) => Source.Write(data, Kind);
        public void OnStopped(Exception? error) => Faulted = true;
    }

    private void MonitorLoop()
    {
        var slots = new List<Slot>();
        var failedUntil = new Dictionary<int, DateTime>();
        string? lastFailure = null;
        SetState(AppCaptureState.Waiting, "Recherche des applications qui jouent du son…");

        using var enumerator = new MMDeviceEnumerator();
        try
        {
            while (!_stopRequested)
            {
                bool changed = false;
                HashSet<int> wanted;
                try
                {
                    wanted = FindAudioProcesses(enumerator);
                }
                catch (Exception ex)
                {
                    SetState(AppCaptureState.Error, "Impossible de lister les applications audio : " + ex.Message);
                    _wake.Wait(3000);
                    _wake.Reset();
                    continue;
                }

                // 1. Retirer les captures dont l'application s'est fermée, est exclue ou a planté.
                for (int i = slots.Count - 1; i >= 0; i--)
                {
                    var slot = slots[i];
                    if (!slot.Faulted && wanted.Contains(slot.ProcessId)) continue;

                    slots.RemoveAt(i);
                    PublishSources(slots); // le thread audio cesse de lire cette source avant sa fermeture
                    slot.Capture?.Dispose();
                    changed = true;
                }

                // 2. Démarrer une capture pour chaque nouvelle application.
                foreach (int processId in wanted)
                {
                    if (slots.Count >= MaxCapturedApps) break;
                    if (slots.Exists(slot => slot.ProcessId == processId)) continue;
                    if (failedUntil.TryGetValue(processId, out DateTime until) && DateTime.UtcNow < until) continue;

                    string name = NameOf(processId) ?? $"PID {processId}";
                    var created = TryStartCapture(processId, name, out string? failure);
                    if (created != null)
                    {
                        if (ResolveSavedLevels != null)
                        {
                            var saved = ResolveSavedLevels(name);
                            created.Source.Volume = saved.Volume;
                            created.Source.Muted = saved.Muted;
                        }
                        slots.Add(created);
                        failedUntil.Remove(processId);
                        changed = true;
                    }
                    else
                    {
                        failedUntil[processId] = DateTime.UtcNow + RetryDelay;
                        lastFailure = $"{name} : {failure}";
                    }
                }

                if (changed) PublishSources(slots);

                if (slots.Count > 0)
                {
                    string names = string.Join(", ", slots.Select(slot => slot.Name).Distinct());
                    SetState(AppCaptureState.Capturing,
                        slots.Count == 1 ? $"1 application capturée : {names}"
                                         : $"{slots.Count} applications capturées : {names}");
                }
                else if (lastFailure != null && wanted.Count > 0)
                {
                    SetState(AppCaptureState.Error, "Capture impossible — " + lastFailure);
                }
                else
                {
                    SetState(AppCaptureState.Waiting, "Aucune application ne joue de son pour l'instant");
                }

                _wake.Wait(ScanIntervalMs);
                _wake.Reset();
            }
        }
        finally
        {
            Volatile.Write(ref _sources, []);
            SourcesChanged?.Invoke();
            foreach (var slot in slots) slot.Capture?.Dispose();
        }
    }

    private void PublishSources(List<Slot> slots)
    {
        Volatile.Write(ref _sources, slots.Select(slot => slot.Source).ToArray());
        SourcesChanged?.Invoke();
    }

    /// <summary>
    /// Processus possédant une session audio sur une sortie active, hors exclusions.
    /// Si un processus et l'un de ses ancêtres sont tous deux présents, seul l'ancêtre est gardé
    /// (sa capture inclut déjà ses enfants : évite de doubler le son).
    ///
    /// Ce relevé tourne pendant que l'audio joue : le travail coûteux (photo de tous les processus,
    /// lecture de leurs noms) n'est refait que lorsque la liste des processus sonores a changé.
    /// </summary>
    private HashSet<int> FindAudioProcesses(MMDeviceEnumerator enumerator)
    {
        var found = new HashSet<int>();
        var now = DateTime.UtcNow;

        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            try
            {
                var manager = device.AudioSessionManager;
                manager.RefreshSessions();
                var sessions = manager.Sessions;
                int count = sessions?.Count ?? 0;
                for (int i = 0; i < count; i++)
                {
                    using var session = sessions![i];
                    if (session.IsSystemSoundsSession) continue;
                    if (session.State == AudioSessionState.AudioSessionStateExpired) continue;
                    int processId = (int)session.GetProcessID;
                    if (processId == 0 || processId == _ownProcessId) continue;

                    // Beaucoup d'applications gardent un flux ouvert sans rien jouer
                    // (Steam, Windows, les utilitaires de carte son). On ne retient que
                    // celles qui font vraiment du son, et on les garde un moment après.
                    float peak = 0f;
                    try { peak = session.AudioMeterInformation.MasterPeakValue; } catch { }
                    if (peak > SilenceThreshold) _lastSound[processId] = now;
                    else if (!_lastSound.TryGetValue(processId, out DateTime heard) ||
                             now - heard > KeepAfterSilence) continue;

                    found.Add(processId);
                }
            }
            catch
            {
                // périphérique retiré pendant le parcours : ignoré
            }
            finally
            {
                device.Dispose();
            }
        }

        foreach (int stale in _lastSound.Where(entry => now - entry.Value > KeepAfterSilence)
                     .Select(entry => entry.Key).ToList())
            _lastSound.Remove(stale);

        var excluded = _excludedApps;
        if (found.Count == 0)
        {
            _lastRaw.Clear();
            _lastExcluded = excluded;
            _lastResult = [];
            PublishExcluded([]);
            return found;
        }

        // Rien n'a bougé depuis le dernier passage : on réutilise le résultat.
        if (ReferenceEquals(excluded, _lastExcluded) && found.SetEquals(_lastRaw))
            return [.._lastResult];


        var parents = ProcessTree.GetParentMap();
        _names.Clear();

        var silenced = new List<string>();
        found.RemoveWhere(processId =>
        {
            if (!IsExcluded(processId, parents, excluded)) return false;
            string? name = NameOf(processId);
            if (name != null) silenced.Add(name);
            return true;
        });
        PublishExcluded(silenced);

        // Dédoublonnage parent / enfant.
        var result = new HashSet<int>(found);
        foreach (int processId in found)
            foreach (int other in found)
                if (processId != other && ProcessTree.IsDescendantOf(processId, other, parents))
                    result.Remove(processId);

        _lastRaw = [..found];
        _lastExcluded = excluded;
        _lastResult = [..result];
        return result;
    }

    /// <summary>
    /// Applications qui jouent du son mais qui sont exclues du mix (Discord, par exemple).
    /// L'interface les affiche en grisé pour qu'on sache qu'elles ne sont pas oubliées.
    /// </summary>
    public IReadOnlyList<string> ExcludedPlaying => Volatile.Read(ref _excludedPlaying);

    private void PublishExcluded(List<string> names)
    {
        var current = Volatile.Read(ref _excludedPlaying);
        var distinct = names.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name).ToArray();
        if (current.SequenceEqual(distinct, StringComparer.OrdinalIgnoreCase)) return;
        Volatile.Write(ref _excludedPlaying, distinct);
        SourcesChanged?.Invoke();
    }

    /// <summary>Nom d'un processus, retenu le temps d'un relevé.</summary>
    private string? NameOf(int processId)
    {
        if (_names.TryGetValue(processId, out string? cached)) return cached;
        string? name = ProcessTree.GetName(processId);
        _names[processId] = name;
        return name;
    }

    private bool IsExcluded(int processId, Dictionary<int, int> parents, string[] excluded)
    {
        int current = processId;
        for (int depth = 0; depth < 32 && current != 0; depth++)
        {
            if (current == _ownProcessId) return true;
            string? name = NameOf(current);
            if (name == null) return depth == 0; // processus disparu
            foreach (string excludedName in excluded)
                if (string.Equals(name, excludedName, StringComparison.OrdinalIgnoreCase)) return true;
            if (!parents.TryGetValue(current, out int parent) || parent == current) break;
            current = parent;
        }
        return false;
    }

    private static Slot? TryStartCapture(int processId, string name, out string? failure)
    {
        // Float 32 bits d'abord ; PCM 16 bits en secours si Windows refuse.
        WaveFormat[] formats =
        [
            AudioConstants.MixFormat,
            new WaveFormat(AudioConstants.SampleRate, 16, AudioConstants.Channels)
        ];

        Exception? lastError = null;
        foreach (var format in formats)
        {
            var slot = new Slot(processId, name) { Kind = SampleConverter.GetSampleKind(format) };
            var capture = new ProcessLoopbackCapture(processId, format);
            try
            {
                capture.DataAvailable += slot.OnData;
                capture.Stopped += slot.OnStopped;
                capture.Start();
                slot.Capture = capture;
                failure = null;
                return slot;
            }
            catch (Exception ex)
            {
                lastError = ex;
                capture.Dispose();
            }
        }

        failure = Environment.OSVersion.Version.Build < 20348
            ? "cette version de Windows ne prend peut-être pas en charge la capture par application " +
              "(documentée par Microsoft à partir du build 20348)"
            : lastError?.Message ?? "erreur inconnue";
        return null;
    }

    private void SetState(AppCaptureState state, string message)
    {
        if (State == state && StatusMessage == message) return;
        State = state;
        StatusMessage = message;
        StateChanged?.Invoke();
    }

    public void Dispose()
    {
        Stop();
        _wake.Dispose();
    }
}
