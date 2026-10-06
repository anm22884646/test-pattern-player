using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Screen = System.Windows.Forms.Screen;
using WinFormsPanel = System.Windows.Forms.Panel;

namespace PatternPlayer;

public partial class OutputWindow : Window
{
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;

    private readonly DispatcherTimer _timer;
    private readonly IntPtr _libVlc;
    private readonly PlayerSlot _slotA;
    private readonly PlayerSlot _slotB;
    private PlayerSlot _active;
    private PlayerSlot _standby;
    private IntPtr _media;
    private Screen _screen;
    private bool _isPlaying;
    private bool _isFullscreen;
    private bool _loop;
    private bool _pauseWhenReady;
    private bool _readyRaised;
    private bool _isDisposed;
    private bool _allowClose;
    private bool _errorRaised;
    private TimeSpan _duration;
    private long _lastReportedMediaTime = -1;
    private long _lastReportedTimestamp;

    public event EventHandler<PlaybackPositionEventArgs>? PositionChanged;
    public event EventHandler<TimeSpan>? MediaReady;
    public event EventHandler<string>? PlaybackFailed;
    public event EventHandler<bool>? PlaybackStateChanged;
    public event EventHandler<bool>? FullscreenChanged;

    public bool IsFullscreen => _isFullscreen;
    public bool HasMedia => _media != IntPtr.Zero;
    public TimeSpan Duration => _duration;

    public OutputWindow(Screen screen)
    {
        InitializeComponent();
        _screen = screen;

        var vlcDirectory = VlcRuntime.FindAndInitialize();
        var args = new[]
        {
            "--no-video-title-show",
            "--no-osd",
            "--no-stats",
            "--quiet",
            "--avcodec-hw=any",
            $"--plugin-path={Path.Combine(vlcDirectory, "plugins")}" 
        };

        _libVlc = VlcNative.CreateInstance(args);
        if (_libVlc == IntPtr.Zero)
            throw new InvalidOperationException("VLC再生エンジンを起動できません。" );

        _slotA = CreatePlayerSlot("A");
        _slotB = CreatePlayerSlot("B");
        _active = _slotA;
        _standby = _slotB;
        _active.Surface.BringToFront();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        _timer.Tick += Timer_Tick;

        SourceInitialized += (_, _) => MoveToSelectedScreen();
        Loaded += OutputWindow_Loaded;
        PreviewKeyDown += OutputWindow_PreviewKeyDown;
        Closing += OutputWindow_Closing;
        Closed += (_, _) => DisposePlayer();
    }

    public void LoadMedia(string path)
    {
        StopCurrentMedia();

        _active = _slotA;
        _standby = _slotB;
        _active.Surface.BringToFront();
        _duration = TimeSpan.Zero;
        _pauseWhenReady = true;
        _readyRaised = false;
        _errorRaised = false;
        ResetPlaybackClock();

        _media = VlcNative.libvlc_media_new_path(_libVlc, path);
        if (_media == IntPtr.Zero)
        {
            PlaybackFailed?.Invoke(this, "VLCでこのファイルを開けません。" );
            return;
        }

        ConfigureSlotForMedia(_active, muted: false, preparing: false);
        ConfigureSlotForMedia(_standby, muted: true, preparing: true);

        Show();
        MoveToSelectedScreen();
        VlcNative.libvlc_media_player_play(_active.Player);
        VlcNative.libvlc_media_player_play(_standby.Player);
        _timer.Start();
    }

    public void Play()
    {
        if (!HasMedia)
            return;

        _pauseWhenReady = false;
        var state = VlcNative.libvlc_media_player_get_state(_active.Player);
        if (state is VlcState.Ended or VlcState.Stopped)
            RestartActivePlayer();
        else
            VlcNative.libvlc_media_player_set_pause(_active.Player, 0);

        _timer.Start();
        SetPlayingState(true);
    }

    public void Pause()
    {
        if (HasMedia)
            VlcNative.libvlc_media_player_set_pause(_active.Player, 1);
        SetPlayingState(false);
    }

    public void Seek(TimeSpan position)
    {
        if (!HasMedia)
            return;

        if (position < TimeSpan.Zero)
            position = TimeSpan.Zero;
        if (_duration > TimeSpan.Zero && position > _duration)
            position = _duration;

        VlcNative.libvlc_media_player_set_time(_active.Player, (long)position.TotalMilliseconds);
        ResetPlaybackClock((long)position.TotalMilliseconds);
        PositionChanged?.Invoke(this, new PlaybackPositionEventArgs(position, _duration));
    }

    public void SetLoop(bool enabled) => _loop = enabled;

    public void SetScreen(Screen screen)
    {
        _screen = screen;
        Topmost = _isFullscreen && !_screen.Primary;
        MoveToSelectedScreen();
    }

    public void ToggleFullscreen()
    {
        _isFullscreen = !_isFullscreen;
        WindowStyle = _isFullscreen ? WindowStyle.None : WindowStyle.SingleBorderWindow;
        ResizeMode = _isFullscreen ? ResizeMode.NoResize : ResizeMode.CanResize;
        Topmost = _isFullscreen && !_screen.Primary;
        MoveToSelectedScreen();
        FullscreenChanged?.Invoke(this, _isFullscreen);
    }

    public void ExitFullscreen()
    {
        if (_isFullscreen)
            ToggleFullscreen();
    }

    public void Shutdown()
    {
        _allowClose = true;
        Close();
    }

    private PlayerSlot CreatePlayerSlot(string name)
    {
        var surface = new WinFormsPanel
        {
            Name = $"VideoSurface{name}",
            Dock = System.Windows.Forms.DockStyle.Fill,
            BackColor = Color.Black
        };
        VideoPanel.Controls.Add(surface);

        var player = VlcNative.libvlc_media_player_new(_libVlc);
        if (player == IntPtr.Zero)
            throw new InvalidOperationException("VLCプレーヤーを作成できません。" );

        return new PlayerSlot(name, player, surface);
    }

    private void ConfigureSlotForMedia(PlayerSlot slot, bool muted, bool preparing)
    {
        slot.IsPrepared = false;
        slot.IsPreparing = preparing;
        VlcNative.libvlc_media_player_set_media(slot.Player, _media);
        AttachVideoSurface(slot);
        VlcNative.libvlc_audio_set_mute(slot.Player, muted ? 1 : 0);
    }

    private void OutputWindow_Loaded(object sender, RoutedEventArgs e)
    {
        AttachVideoSurface(_slotA);
        AttachVideoSurface(_slotB);
    }

    private static void AttachVideoSurface(PlayerSlot slot)
    {
        if (slot.Player != IntPtr.Zero)
            VlcNative.libvlc_media_player_set_hwnd(slot.Player, slot.Surface.Handle);
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (!HasMedia)
            return;

        PrepareStandbyIfReady();

        var state = VlcNative.libvlc_media_player_get_state(_active.Player);
        var length = VlcNative.libvlc_media_player_get_length(_active.Player);
        var time = Math.Max(VlcNative.libvlc_media_player_get_time(_active.Player), 0);
        var now = Stopwatch.GetTimestamp();

        if (time != _lastReportedMediaTime)
        {
            _lastReportedMediaTime = time;
            _lastReportedTimestamp = now;
        }

        if (length > 0 && _duration.TotalMilliseconds != length)
        {
            _duration = TimeSpan.FromMilliseconds(length);
            if (!_readyRaised)
            {
                _readyRaised = true;
                MediaReady?.Invoke(this, _duration);
            }
        }

        if (_pauseWhenReady && state == VlcState.Playing)
        {
            _pauseWhenReady = false;
            VlcNative.libvlc_media_player_set_time(_active.Player, 0);
            VlcNative.libvlc_media_player_set_pause(_active.Player, 1);
            time = 0;
            ResetPlaybackClock(0);
            SetPlayingState(false);
        }
        else if (state == VlcState.Playing)
        {
            SetPlayingState(true);
        }

        if (_loop && !_pauseWhenReady && _standby.IsPrepared &&
            state == VlcState.Playing && length > 0 && time > 0)
        {
            var framesPerSecond = VlcNative.libvlc_media_player_get_fps(_active.Player);
            var oneFrameMilliseconds = framesPerSecond > 0.1f
                ? 1000.0 / framesPerSecond
                : 40.0;
            var swapThreshold = Math.Max(16.0, oneFrameMilliseconds + 8.0);
            var elapsedSinceReport = _lastReportedTimestamp > 0
                ? (now - _lastReportedTimestamp) * 1000.0 / Stopwatch.Frequency
                : 0.0;
            var predictedTime = Math.Min(length, time + elapsedSinceReport);

            if (length - predictedTime <= swapThreshold)
            {
                PerformBufferedLoopSwap(framesPerSecond, predictedTime);
                time = 0;
                state = VlcState.Playing;
            }
        }

        PositionChanged?.Invoke(this,
            new PlaybackPositionEventArgs(TimeSpan.FromMilliseconds(time), _duration));

        if (state == VlcState.Ended)
        {
            if (_loop && _standby.IsPrepared)
            {
                DiagnosticLog.Write("LoopSwapAtEnded");
                PerformBufferedLoopSwap(0, length);
            }
            else if (_loop)
            {
                DiagnosticLog.Write("LoopFallbackRestart standby-not-ready");
                RestartActivePlayer();
            }
            else
            {
                SetPlayingState(false);
            }
        }
        else if (state == VlcState.Error && !_errorRaised)
        {
            _errorRaised = true;
            SetPlayingState(false);
            PlaybackFailed?.Invoke(this, "VLCでこのファイルをデコードまたは再生できません。" );
        }
    }

    private void PrepareStandbyIfReady()
    {
        if (!_standby.IsPreparing)
            return;

        var state = VlcNative.libvlc_media_player_get_state(_standby.Player);
        if (state == VlcState.Playing)
        {
            VlcNative.libvlc_media_player_set_pause(_standby.Player, 1);
            VlcNative.libvlc_media_player_set_time(_standby.Player, 0);
            VlcNative.libvlc_media_player_next_frame(_standby.Player);
            _standby.IsPreparing = false;
            _standby.IsPrepared = true;
            DiagnosticLog.Write($"StandbyReady slot={_standby.Name}");
        }
        else if (state == VlcState.Error)
        {
            _standby.IsPreparing = false;
            _standby.IsPrepared = false;
            DiagnosticLog.Write($"StandbyError slot={_standby.Name}");
        }
    }

    private void PerformBufferedLoopSwap(float framesPerSecond, double predictedTime)
    {
        var outgoing = _active;
        var incoming = _standby;

        incoming.Surface.BringToFront();
        VlcNative.libvlc_audio_set_mute(incoming.Player, 0);
        VlcNative.libvlc_media_player_set_pause(incoming.Player, 0);
        VlcNative.libvlc_audio_set_mute(outgoing.Player, 1);

        _active = incoming;
        _standby = outgoing;
        _active.IsPrepared = false;
        ResetPlaybackClock(0);
        SetPlayingState(true);

        VlcNative.libvlc_media_player_stop(_standby.Player);
        ConfigureSlotForMedia(_standby, muted: true, preparing: true);
        VlcNative.libvlc_media_player_play(_standby.Player);

        DiagnosticLog.Write(
            $"LoopBufferedSwap active={_active.Name} standby={_standby.Name} fps={framesPerSecond:0.###} predictedTimeMs={predictedTime:0.###}");
    }

    private void RestartActivePlayer()
    {
        if (_media == IntPtr.Zero)
            return;

        VlcNative.libvlc_media_player_stop(_active.Player);
        ConfigureSlotForMedia(_active, muted: false, preparing: false);
        _active.Surface.BringToFront();
        VlcNative.libvlc_media_player_play(_active.Player);
        ResetPlaybackClock(0);
        SetPlayingState(true);
    }

    private void ResetPlaybackClock(long mediaTime = -1)
    {
        _lastReportedMediaTime = mediaTime;
        _lastReportedTimestamp = mediaTime >= 0 ? Stopwatch.GetTimestamp() : 0;
    }

    private void StopCurrentMedia()
    {
        _timer?.Stop();
        StopSlot(_slotA);
        StopSlot(_slotB);

        if (_media != IntPtr.Zero)
        {
            VlcNative.libvlc_media_release(_media);
            _media = IntPtr.Zero;
        }
        SetPlayingState(false);
    }

    private static void StopSlot(PlayerSlot slot)
    {
        if (slot.Player != IntPtr.Zero)
            VlcNative.libvlc_media_player_stop(slot.Player);
        slot.IsPreparing = false;
        slot.IsPrepared = false;
    }

    private void SetPlayingState(bool playing)
    {
        if (_isPlaying == playing)
            return;
        _isPlaying = playing;
        PlaybackStateChanged?.Invoke(this, playing);
    }

    private void MoveToSelectedScreen()
    {
        if (!IsInitialized)
            return;

        var bounds = _screen.Bounds;
        int left;
        int top;
        int width;
        int height;

        if (_isFullscreen)
        {
            left = bounds.Left;
            top = bounds.Top;
            width = bounds.Width;
            height = bounds.Height;
        }
        else
        {
            width = Math.Max(640, (int)(bounds.Width * 0.72));
            height = Math.Max(360, (int)(bounds.Height * 0.72));
            width = Math.Min(width, bounds.Width);
            height = Math.Min(height, bounds.Height);
            left = bounds.Left + (bounds.Width - width) / 2;
            top = bounds.Top + (bounds.Height - height) / 2;
        }

        var handle = new WindowInteropHelper(this).Handle;
        var keepOutputTopmost = _isFullscreen && !_screen.Primary;
        SetWindowPos(handle, keepOutputTopmost ? new IntPtr(-1) : IntPtr.Zero,
            left, top, width, height, SwpNoActivate | SwpShowWindow);
    }

    private void OutputWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _isFullscreen)
        {
            ExitFullscreen();
            e.Handled = true;
        }
        else if (e.Key == Key.Space)
        {
            if (_isPlaying) Pause(); else Play();
            e.Handled = true;
        }
        else if (e.Key == Key.F11)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
    }

    private void OutputWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private void DisposePlayer()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;

        StopCurrentMedia();
        VlcNative.libvlc_media_player_release(_slotA.Player);
        VlcNative.libvlc_media_player_release(_slotB.Player);
        if (_libVlc != IntPtr.Zero)
            VlcNative.libvlc_release(_libVlc);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint flags);

    private sealed class PlayerSlot(string name, IntPtr player, WinFormsPanel surface)
    {
        public string Name { get; } = name;
        public IntPtr Player { get; } = player;
        public WinFormsPanel Surface { get; } = surface;
        public bool IsPreparing { get; set; }
        public bool IsPrepared { get; set; }
    }
}

internal static class VlcRuntime
{
    private static bool _initialized;
    private static string? _directory;

    public static string FindAndInitialize()
    {
        if (_initialized && _directory is not null)
            return _directory;

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "vlc"),
            @"C:\Program Files\VideoLAN\VLC",
            @"C:\Program Files (x86)\VideoLAN\VLC"
        };

        _directory = candidates.FirstOrDefault(path => File.Exists(Path.Combine(path, "libvlc.dll")))
            ?? throw new FileNotFoundException("VLC再生エンジンが見つかりません。vlcフォルダーが完全であることを確認してください。" );

        SetDllDirectory(_directory);
        if (LoadLibrary(Path.Combine(_directory, "libvlccore.dll")) == IntPtr.Zero ||
            LoadLibrary(Path.Combine(_directory, "libvlc.dll")) == IntPtr.Zero)
            throw new DllNotFoundException("VLC再生エンジンの読み込みに失敗しました。" );
        _initialized = true;
        return _directory;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpFileName);
}

internal enum VlcState
{
    NothingSpecial = 0,
    Opening = 1,
    Buffering = 2,
    Playing = 3,
    Paused = 4,
    Stopped = 5,
    Ended = 6,
    Error = 7
}

internal static class VlcNative
{
    private const string Library = "libvlc.dll";

    internal static IntPtr CreateInstance(string[] arguments)
    {
        var argumentPointers = new IntPtr[arguments.Length];
        var argv = Marshal.AllocHGlobal(IntPtr.Size * arguments.Length);

        try
        {
            for (var i = 0; i < arguments.Length; i++)
            {
                var bytes = Encoding.UTF8.GetBytes(arguments[i]);
                argumentPointers[i] = Marshal.AllocHGlobal(bytes.Length + 1);
                Marshal.Copy(bytes, 0, argumentPointers[i], bytes.Length);
                Marshal.WriteByte(argumentPointers[i], bytes.Length, 0);
                Marshal.WriteIntPtr(argv, i * IntPtr.Size, argumentPointers[i]);
            }

            return libvlc_new(arguments.Length, argv);
        }
        finally
        {
            foreach (var pointer in argumentPointers)
            {
                if (pointer != IntPtr.Zero)
                    Marshal.FreeHGlobal(pointer);
            }
            Marshal.FreeHGlobal(argv);
        }
    }

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr libvlc_new(int argc, IntPtr argv);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void libvlc_release(IntPtr instance);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr libvlc_media_new_path(IntPtr instance,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void libvlc_media_release(IntPtr media);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr libvlc_media_player_new(IntPtr instance);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void libvlc_media_player_release(IntPtr player);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void libvlc_media_player_set_media(IntPtr player, IntPtr media);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int libvlc_media_player_play(IntPtr player);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void libvlc_media_player_set_pause(IntPtr player, int pause);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void libvlc_media_player_stop(IntPtr player);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern long libvlc_media_player_get_length(IntPtr player);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern long libvlc_media_player_get_time(IntPtr player);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int libvlc_media_player_set_time(IntPtr player, long time);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern VlcState libvlc_media_player_get_state(IntPtr player);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern float libvlc_media_player_get_fps(IntPtr player);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void libvlc_media_player_next_frame(IntPtr player);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void libvlc_media_player_set_hwnd(IntPtr player, IntPtr drawable);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int libvlc_audio_set_mute(IntPtr player, int mute);
}

public sealed class PlaybackPositionEventArgs(TimeSpan position, TimeSpan duration) : EventArgs
{
    public TimeSpan Position { get; } = position;
    public TimeSpan Duration { get; } = duration;
}

internal static class DiagnosticLog
{
    private static readonly string? LogPath =
        Environment.GetEnvironmentVariable("PATTERN_PLAYER_DIAGNOSTIC_LOG");

    public static void Write(string message)
    {
        if (string.IsNullOrWhiteSpace(LogPath))
            return;

        try
        {
            File.AppendAllText(LogPath,
                $"{DateTime.Now:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never interrupt playback.
        }
    }
}
