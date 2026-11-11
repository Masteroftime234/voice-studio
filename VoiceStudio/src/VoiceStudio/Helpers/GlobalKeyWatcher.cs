using System.Runtime.InteropServices;

namespace VoiceStudio.Helpers;

/// <summary>
/// Surveille des touches du clavier même quand Voice Studio n'est pas la fenêtre active
/// (push-to-talk, raccourcis du soundboard).
///
/// L'état des touches est relevé 40 fois par seconde. Ce relevé n'enregistre rien :
/// seules les touches explicitement surveillées sont consultées, une par une.
/// </summary>
public sealed class GlobalKeyWatcher : IDisposable
{
    private readonly System.Threading.Timer _timer;
    private readonly object _gate = new();
    private readonly Dictionary<int, bool> _states = new();

    public GlobalKeyWatcher()
    {
        _timer = new System.Threading.Timer(_ => Poll(), null, 25, 25);
    }

    /// <summary>Touche enfoncée, puis relâchée. Levé sur un thread d'arrière-plan.</summary>
    public event Action<int, bool>? KeyStateChanged;

    public void Watch(int virtualKey)
    {
        if (virtualKey == 0) return;
        lock (_gate) _states.TryAdd(virtualKey, false);
    }

    public void Forget(int virtualKey)
    {
        lock (_gate) _states.Remove(virtualKey);
    }

    public void WatchOnly(IEnumerable<int> virtualKeys)
    {
        var wanted = virtualKeys.Where(key => key != 0).Distinct().ToList();
        lock (_gate)
        {
            foreach (int key in _states.Keys.Where(key => !wanted.Contains(key)).ToList())
                _states.Remove(key);
            foreach (int key in wanted) _states.TryAdd(key, false);
        }
    }

    private void Poll()
    {
        List<(int Key, bool Down)>? changes = null;
        lock (_gate)
        {
            foreach (int key in _states.Keys.ToList())
            {
                bool down = (GetAsyncKeyState(key) & 0x8000) != 0;
                if (down == _states[key]) continue;
                _states[key] = down;
                (changes ??= []).Add((key, down));
            }
        }
        if (changes == null) return;
        foreach (var (key, down) in changes) KeyStateChanged?.Invoke(key, down);
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    public void Dispose() => _timer.Dispose();
}
