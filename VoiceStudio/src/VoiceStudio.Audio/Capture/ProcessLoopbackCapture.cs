using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wasapi.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace VoiceStudio.Audio.Capture;

public delegate void LoopbackDataHandler(ReadOnlySpan<byte> data);

/// <summary>
/// Capture le son rendu par UNE application (et ses processus enfants), sans toucher
/// au reste du système, via l'API Windows « Application Loopback »
/// (ActivateAudioInterfaceAsync + AUDIOCLIENT_ACTIVATION_PARAMS).
/// Microsoft documente cette API à partir de Windows 10 build 20348.
/// </summary>
public sealed class ProcessLoopbackCapture : IDisposable
{
    private const string VirtualAudioDeviceProcessLoopback = @"VAD\Process_Loopback";
    private const short VtBlob = 65;
    private const long BufferDuration100Ns = 200_000; // 20 ms

    private static readonly Guid IidAudioClient = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");

    private readonly int _processId;
    private readonly ManualResetEventSlim _ready = new(false);
    private Thread? _thread;
    private Exception? _startError;
    private volatile bool _stopRequested;

    public ProcessLoopbackCapture(int processId, WaveFormat format)
    {
        _processId = processId;
        WaveFormat = format;
    }

    /// <summary>Format demandé à Windows (qui convertit pour nous).</summary>
    public WaveFormat WaveFormat { get; }

    /// <summary>Données capturées. Appelé sur le thread de capture : ne pas bloquer, ne pas allouer.</summary>
    public event LoopbackDataHandler? DataAvailable;

    /// <summary>La capture s'est arrêtée (exception non nulle en cas d'erreur).</summary>
    public event Action<Exception?>? Stopped;

    /// <summary>Démarre la capture. Lève une exception si Windows refuse l'activation.</summary>
    public void Start()
    {
        if (_thread != null) throw new InvalidOperationException("Capture déjà démarrée.");

        _thread = new Thread(CaptureThread)
        {
            IsBackground = true,
            Name = "VoiceStudio.ProcessLoopback",
            Priority = ThreadPriority.AboveNormal
        };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();

        if (!_ready.Wait(TimeSpan.FromSeconds(5)))
        {
            _stopRequested = true;
            throw new TimeoutException("Windows n'a pas répondu à la demande de capture de l'application.");
        }
        if (_startError != null)
            throw new InvalidOperationException(_startError.Message, _startError);
    }

    public void Stop()
    {
        _stopRequested = true;
        var thread = _thread;
        if (thread != null && thread != Thread.CurrentThread) thread.Join(2000);
    }

    public void Dispose()
    {
        Stop();
        _ready.Dispose();
    }

    private void CaptureThread()
    {
        AudioClient? audioClient = null;
        AudioCaptureClient? captureClient = null;
        AutoResetEvent? dataEvent = null;
        Exception? error = null;
        bool started = false;

        try
        {
            audioClient = new AudioClient(ActivateForProcess(_processId));
            Initialize(audioClient, WaveFormat);

            dataEvent = new AutoResetEvent(false);
            audioClient.SetEventHandle(dataEvent.SafeWaitHandle.DangerousGetHandle());
            captureClient = audioClient.AudioCaptureClient;
            int bytesPerFrame = WaveFormat.BlockAlign;

            audioClient.Start();
            started = true;
            _ready.Set();

            while (!_stopRequested)
            {
                dataEvent.WaitOne(100);

                while (!_stopRequested && captureClient.GetNextPacketSize() > 0)
                {
                    IntPtr data = captureClient.GetBuffer(out int frames, out AudioClientBufferFlags flags);
                    if (frames > 0 && (flags & AudioClientBufferFlags.Silent) == 0)
                    {
                        DataAvailable?.Invoke(NativeSpan(data, frames * bytesPerFrame));
                    }
                    captureClient.ReleaseBuffer(frames);
                }
            }
        }
        catch (Exception ex)
        {
            error = ex;
            if (!started)
            {
                _startError = ex;
                try { _ready.Set(); } catch (ObjectDisposedException) { }
            }
        }
        finally
        {
            try { if (started) audioClient?.Stop(); } catch { /* le flux est peut-être déjà invalide */ }
            captureClient?.Dispose();
            audioClient?.Dispose();
            dataEvent?.Dispose();
        }

        if (started) Stopped?.Invoke(error);
    }

    // Construit un Span sur la mémoire native sans bloc « unsafe ».
    private static ReadOnlySpan<byte> NativeSpan(IntPtr pointer, int length) =>
        MemoryMarshal.CreateReadOnlySpan(
            ref Unsafe.AddByteOffset(ref Unsafe.NullRef<byte>(), pointer), length);

    private static void Initialize(AudioClient client, WaveFormat format)
    {
        const AudioClientStreamFlags baseFlags =
            AudioClientStreamFlags.Loopback | AudioClientStreamFlags.EventCallback;
        try
        {
            client.Initialize(AudioClientShareMode.Shared,
                baseFlags | AudioClientStreamFlags.AutoConvertPcm | AudioClientStreamFlags.SrcDefaultQuality,
                BufferDuration100Ns, 0, format, Guid.Empty);
        }
        catch (COMException)
        {
            // Certaines versions de Windows refusent les indicateurs de conversion sur ce périphérique virtuel.
            client.Initialize(AudioClientShareMode.Shared, baseFlags,
                BufferDuration100Ns, 0, format, Guid.Empty);
        }
    }

    private static IAudioClient ActivateForProcess(int processId)
    {
        // AUDIOCLIENT_ACTIVATION_PARAMS { ActivationType = PROCESS_LOOPBACK (1),
        //   ProcessLoopbackParams { TargetProcessId, ProcessLoopbackMode = INCLUDE_TARGET_PROCESS_TREE (0) } }
        const int paramsSize = 12;
        // PROPVARIANT : vt à l'offset 0, puis BLOB { cbSize ; pBlobData } à l'offset 8.
        int propVariantSize = 8 + 2 * IntPtr.Size;

        IntPtr activationParams = Marshal.AllocCoTaskMem(paramsSize);
        IntPtr propVariant = Marshal.AllocCoTaskMem(propVariantSize);
        try
        {
            Marshal.WriteInt32(activationParams, 0, 1);
            Marshal.WriteInt32(activationParams, 4, processId);
            Marshal.WriteInt32(activationParams, 8, 0);

            for (int i = 0; i < propVariantSize; i++) Marshal.WriteByte(propVariant, i, 0);
            Marshal.WriteInt16(propVariant, 0, VtBlob);
            Marshal.WriteInt32(propVariant, 8, paramsSize);
            Marshal.WriteIntPtr(propVariant, 8 + IntPtr.Size, activationParams);

            using var handler = new CompletionHandler();
            int hr = ActivateAudioInterfaceAsync(VirtualAudioDeviceProcessLoopback, IidAudioClient,
                propVariant, handler, out IActivateAudioInterfaceAsyncOperation operation);
            Marshal.ThrowExceptionForHR(hr);

            if (!handler.Completed.Wait(TimeSpan.FromSeconds(4)))
                throw new TimeoutException("Windows n'a pas terminé l'activation de la capture.");

            GC.KeepAlive(operation);
            Marshal.ThrowExceptionForHR(handler.ActivateResult);
            return handler.AudioClient
                   ?? throw new InvalidOperationException("Windows n'a renvoyé aucune interface de capture.");
        }
        finally
        {
            Marshal.FreeCoTaskMem(propVariant);
            Marshal.FreeCoTaskMem(activationParams);
        }
    }

    [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int ActivateAudioInterfaceAsync(
        [In, MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
        [In, MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [In] IntPtr activationParams,
        [In] IActivateAudioInterfaceCompletionHandler completionHandler,
        out IActivateAudioInterfaceAsyncOperation activationOperation);

    /// <summary>Marqueur COM : l'objet peut être appelé depuis n'importe quel thread.</summary>
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90")]
    private interface IAgileObject
    {
    }

    private sealed class CompletionHandler : IActivateAudioInterfaceCompletionHandler, IAgileObject, IDisposable
    {
        public ManualResetEventSlim Completed { get; } = new(false);
        public int ActivateResult { get; private set; }
        public IAudioClient? AudioClient { get; private set; }

        public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation)
        {
            try
            {
                activateOperation.GetActivateResult(out int result, out object activatedInterface);
                ActivateResult = result;
                AudioClient = activatedInterface as IAudioClient;
            }
            catch (Exception ex)
            {
                ActivateResult = ex.HResult;
            }
            finally
            {
                try { Completed.Set(); } catch (ObjectDisposedException) { /* réponse arrivée après le délai */ }
            }
        }

        public void Dispose() => Completed.Dispose();
    }
}
