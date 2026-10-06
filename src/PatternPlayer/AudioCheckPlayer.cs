using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace PatternPlayer;

internal sealed class AudioCheckPlayer : IDisposable
{
    private readonly IntPtr _instance;
    private readonly IntPtr _player;
    private readonly DispatcherTimer _timer;
    private bool _enabled, _playing;
    internal sealed class Device
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public override string ToString() => Name;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeDevice { public IntPtr Next, Id, Description; }
    public AudioCheckPlayer()
    {
        var directory = VlcRuntime.FindAndInitialize();
        _instance = VlcNative.CreateInstance(new[] { "--no-video", "--quiet", "--aout=mmdevice", "--plugin-path=" + Path.Combine(directory, "plugins") });
        if (_instance == IntPtr.Zero) throw new InvalidOperationException("音声エンジンを起動できません。");
        _player = VlcNative.libvlc_media_player_new(_instance);
        if (_player == IntPtr.Zero) { VlcNative.libvlc_release(_instance); throw new InvalidOperationException("音声プレイヤーを作成できません。"); }
        var path = Path.Combine(AppContext.BaseDirectory, "Audio", "TestingBeat.mp3");
        if (!File.Exists(path)) { Dispose(); throw new FileNotFoundException("Audio Check 音源が見つかりません。", path); }
        var media = VlcNative.libvlc_media_new_path(_instance, path);
        VlcNative.libvlc_media_player_set_media(_player, media);
        VlcNative.libvlc_media_release(media);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, _) => { if (_enabled && _playing && VlcNative.libvlc_media_player_get_state(_player) == VlcState.Ended) { VlcNative.libvlc_media_player_stop(_player); VlcNative.libvlc_media_player_play(_player); } };
        _timer.Start();
    }
    public List<Device> GetDevices()
    {
        var result = new List<Device> { new Device { Name = "Windows の既定の出力" } };
        var head = libvlc_audio_output_device_enum(_player);
        try { for (var p = head; p != IntPtr.Zero;) { var d = Marshal.PtrToStructure<NativeDevice>(p); result.Add(new Device { Id = Utf8(d.Id), Name = Utf8(d.Description) }); p = d.Next; } }
        finally { if (head != IntPtr.Zero) libvlc_audio_output_device_list_release(head); }
        return result;
    }
    private static string Utf8(IntPtr p)
    {
        if (p == IntPtr.Zero) return "";
        var length = 0; while (Marshal.ReadByte(p, length) != 0) length++;
        var bytes = new byte[length]; Marshal.Copy(p, bytes, 0, length); return System.Text.Encoding.UTF8.GetString(bytes);
    }
    public void Select(Device device)
    {
        VlcNative.libvlc_media_player_stop(_player);
        var bytes = System.Text.Encoding.UTF8.GetBytes(device.Id + "\0");
        var p = Marshal.AllocHGlobal(bytes.Length);
        try { Marshal.Copy(bytes, 0, p, bytes.Length); libvlc_audio_output_device_set(_player, IntPtr.Zero, device.Id.Length == 0 ? IntPtr.Zero : p); }
        finally { Marshal.FreeHGlobal(p); }
        Synchronize(_enabled, _playing);
    }
    public void Synchronize(bool enabled, bool playing)
    {
        _enabled = enabled; _playing = playing;
        if (!enabled) { VlcNative.libvlc_media_player_stop(_player); return; }
        if (!playing) { VlcNative.libvlc_media_player_set_pause(_player, 1); return; }
        var state = VlcNative.libvlc_media_player_get_state(_player);
        if (state == VlcState.Paused) VlcNative.libvlc_media_player_set_pause(_player, 0);
        else if (state != VlcState.Playing && state != VlcState.Opening && state != VlcState.Buffering)
            VlcNative.libvlc_media_player_play(_player);
    }
    public void Dispose() { _timer?.Stop(); VlcNative.libvlc_media_player_stop(_player); VlcNative.libvlc_media_player_release(_player); VlcNative.libvlc_release(_instance); }
    [DllImport("libvlc.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr libvlc_audio_output_device_enum(IntPtr player);
    [DllImport("libvlc.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void libvlc_audio_output_device_list_release(IntPtr list);
    [DllImport("libvlc.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void libvlc_audio_output_device_set(IntPtr player, IntPtr module, IntPtr device);
}
