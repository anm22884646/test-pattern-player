using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Screen = System.Windows.Forms.Screen;

namespace PatternPlayer;

public partial class MainWindow : Window
{
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;

    private readonly OutputWindow _outputWindow;
    private Screen _selectedScreen;
    private bool _isSeeking;
    private bool _isPlaying;
    private AudioCheckPlayer? _audioCheck;

    public MainWindow()
    {
        InitializeComponent();

        _selectedScreen = ChooseDefaultScreen();
        _outputWindow = new OutputWindow(_selectedScreen);
        _outputWindow.MediaReady += OutputWindow_MediaReady;
        _outputWindow.PositionChanged += OutputWindow_PositionChanged;
        _outputWindow.PlaybackFailed += OutputWindow_PlaybackFailed;
        _outputWindow.PlaybackStateChanged += (_, playing) =>
        {
            _isPlaying = playing;
            _audioCheck?.Synchronize(AudioCheckBox.IsChecked == true, playing);
            StateText.Text = playing ? "再生中" : "一時停止中";
        };
        _outputWindow.FullscreenChanged += (_, fullscreen) =>
        {
            FullscreenButton.Content = fullscreen ? "全画面を終了" : "全画面";
            UpdateWindowLayering();
        };

        SourceInitialized += (_, _) => PositionControlWindowOnPrimaryScreen();
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
        RefreshScreenButtons();
        InitializeAudioCheck();
        _outputWindow.Show();
        _outputWindow.SetLoop(LoopCheckBox.IsChecked == true);
        UpdateWindowLayering();
        Activate();

        var startupFile = Environment.GetCommandLineArgs()
            .Skip(1)
            .FirstOrDefault(File.Exists);
        if (startupFile is not null)
            LoadMediaFile(startupFile);
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
        _audioCheck?.Dispose();
        _outputWindow.Shutdown();
    }

    private static Screen ChooseDefaultScreen()
    {
        var screens = Screen.AllScreens;
        return screens.FirstOrDefault(s => !s.Primary) ?? Screen.PrimaryScreen ?? screens[0];
    }

    private void RefreshScreenButtons()
    {
        Dispatcher.Invoke(() =>
        {
            var screens = Screen.AllScreens;
            var existing = screens.FirstOrDefault(s => s.DeviceName == _selectedScreen.DeviceName);
            if (existing is null)
            {
                _outputWindow.Pause();
                _selectedScreen = Screen.PrimaryScreen ?? screens[0];
                _outputWindow.SetScreen(_selectedScreen);
                StatusText.Text = "出力画面が切断されたため、一時停止してメイン画面へ戻しました。";
            }
            else
            {
                _selectedScreen = existing;
            }

            ScreenButtons.Children.Clear();
            for (var i = 0; i < screens.Length; i++)
            {
                var screen = screens[i];
                var button = new Button
                {
                    Content = $"画面 {i + 1}",
                    Tag = screen,
                    ToolTip = $"{screen.DeviceName} · {screen.Bounds.Width} × {screen.Bounds.Height}"
                };
                button.Click += ScreenButton_Click;
                ApplyScreenButtonStyle(button, screen.DeviceName == _selectedScreen.DeviceName);
                ScreenButtons.Children.Add(button);
            }

            UpdateScreenInfo();
            UpdateWindowLayering();
        });
    }

    private void ApplyScreenButtonStyle(Button button, bool selected)
    {
        button.Background = selected
            ? (Brush)FindResource("Accent")
            : new SolidColorBrush(Color.FromRgb(37, 44, 53));
        button.Foreground = selected ? Brushes.Black : (Brush)FindResource("TextPrimary");
        button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private void UpdateScreenInfo()
    {
        var index = Array.FindIndex(Screen.AllScreens,
            s => s.DeviceName == _selectedScreen.DeviceName) + 1;
        ScreenInfoText.Text = $"現在の出力：画面 {Math.Max(index, 1)} · {_selectedScreen.Bounds.Width} × {_selectedScreen.Bounds.Height}";
    }

    private void ScreenButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Screen screen })
            return;

        _selectedScreen = screen;
        _outputWindow.Show();
        _outputWindow.SetScreen(screen);
        RefreshScreenButtons();
        UpdateWindowLayering();
        StatusText.Text = $"出力を {screen.DeviceName} に移動しました";
    }

    private void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e) => RefreshScreenButtons();

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "テスト動画を選択",
            Filter = "動画ファイル|*.mp4;*.mov;*.m4v;*.wmv;*.avi|すべてのファイル|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
            return;

        LoadMediaFile(dialog.FileName);
    }

    private void LoadMediaFile(string path)
        => LoadMediaFile(path, null);

    private void LoadMediaFile(string path, string? displayName)
    {
        _audioCheck?.Synchronize(false, false);
        FileNameText.Text = displayName ?? Path.GetFileName(path);
        FileNameText.ToolTip = path;
        StatusText.Text = "動画を読み込んでいます…";
        TimelineSlider.IsEnabled = false;
        PlayButton.IsEnabled = false;
        PauseButton.IsEnabled = false;
        _outputWindow.LoadMedia(path);
        Activate();
    }

    private void BuiltInPattern_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string fileName } button)
            return;

        var path = Path.Combine(AppContext.BaseDirectory, "TestPatterns", fileName);
        if (!File.Exists(path))
        {
            MessageBox.Show(this,
                $"内蔵テストパターンが見つかりません。\n\n{path}",
                "ファイルが見つかりません", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        LoadMediaFile(path, button.Content?.ToString());
    }

    private void InitializeAudioCheck()
    {
        try
        {
            _audioCheck = new AudioCheckPlayer();
            var devices = _audioCheck.GetDevices();
            AudioDeviceBox.ItemsSource = devices;
            var hdmi = devices.Where(d => d.Name.IndexOf("HDMI", StringComparison.OrdinalIgnoreCase) >= 0 || d.Name.IndexOf("DisplayPort", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            AudioDeviceBox.SelectedItem = hdmi.Count == 1 ? hdmi[0] : devices[0];
        }
        catch (Exception ex)
        {
            AudioCheckBox.IsEnabled = false;
            StatusText.Text = "Audio Check: " + ex.Message;
        }
    }

    private void AudioCheck_Changed(object sender, RoutedEventArgs e)
        => _audioCheck?.Synchronize(AudioCheckBox.IsChecked == true, _isPlaying);

    private void AudioDevice_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (AudioDeviceBox.SelectedItem is AudioCheckPlayer.Device device)
            _audioCheck?.Select(device);
    }

    private void AudioRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (_audioCheck == null) return;
        var id = (AudioDeviceBox.SelectedItem as AudioCheckPlayer.Device)?.Id;
        var devices = _audioCheck.GetDevices();
        AudioDeviceBox.ItemsSource = devices;
        AudioDeviceBox.SelectedItem = devices.FirstOrDefault(d => d.Id == id) ?? devices[0];
    }

    private void Play_Click(object sender, RoutedEventArgs e) => _outputWindow.Play();

    private void Pause_Click(object sender, RoutedEventArgs e) => _outputWindow.Pause();

    private void Fullscreen_Click(object sender, RoutedEventArgs e)
    {
        _outputWindow.Show();
        _outputWindow.ToggleFullscreen();
        Activate();
    }

    private void Loop_Changed(object sender, RoutedEventArgs e)
    {
        if (_outputWindow is not null)
            _outputWindow.SetLoop(LoopCheckBox.IsChecked == true);
    }

    private void OutputWindow_MediaReady(object? sender, TimeSpan duration)
    {
        Dispatcher.Invoke(() =>
        {
            TimelineSlider.Maximum = Math.Max(duration.TotalMilliseconds, 1);
            TimelineSlider.Value = 0;
            TimelineSlider.IsEnabled = duration > TimeSpan.Zero;
            PlayButton.IsEnabled = true;
            PauseButton.IsEnabled = true;
            DurationText.Text = FormatTime(duration);
            CurrentTimeText.Text = FormatTime(TimeSpan.Zero);
            StateText.Text = "一時停止中";
            StatusText.Text = "動画を読み込みました。先頭フレームで停止しています。";
            _isPlaying = false;
        });
    }

    private void OutputWindow_PositionChanged(object? sender, PlaybackPositionEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (!_isSeeking)
                TimelineSlider.Value = Math.Min(Math.Max(e.Position.TotalMilliseconds, 0), TimelineSlider.Maximum);
            CurrentTimeText.Text = FormatTime(e.Position);
        });
    }

    private void OutputWindow_PlaybackFailed(object? sender, string message)
    {
        Dispatcher.Invoke(() =>
        {
            PlayButton.IsEnabled = false;
            PauseButton.IsEnabled = false;
            TimelineSlider.IsEnabled = false;
            StateText.Text = "再生エラー";
            StatusText.Text = "動画を再生できません";
            MessageBox.Show(this,
                $"この動画を再生できません。\n\n{message}\n\nH.264でエンコードされたMP4またはMOVを推奨します。",
                "再生エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
        });
    }

    private void Timeline_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isSeeking = true;
    }

    private void Timeline_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isSeeking = false;
        _outputWindow.Seek(TimeSpan.FromMilliseconds(TimelineSlider.Value));
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Space && _outputWindow.HasMedia)
        {
            if (_isPlaying) _outputWindow.Pause(); else _outputWindow.Play();
            e.Handled = true;
        }
        else if (e.Key == Key.F11)
        {
            Fullscreen_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _outputWindow.IsFullscreen)
        {
            _outputWindow.ExitFullscreen();
            e.Handled = true;
        }
    }

    private static string FormatTime(TimeSpan time)
    {
        if (time.TotalHours >= 1)
            return $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}";
        return $"{time.Minutes:00}:{time.Seconds:00}";
    }

    private void PositionControlWindowOnPrimaryScreen()
    {
        var primary = Screen.PrimaryScreen;
        if (primary is null)
            return;

        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null)
            return;

        var workingArea = primary.WorkingArea;
        var topLeft = source.CompositionTarget.TransformFromDevice.Transform(
            new System.Windows.Point(workingArea.Left, workingArea.Top));
        var bottomRight = source.CompositionTarget.TransformFromDevice.Transform(
            new System.Windows.Point(workingArea.Right, workingArea.Bottom));

        Left = topLeft.X + Math.Max(0, (bottomRight.X - topLeft.X - ActualWidth) / 2);
        Top = topLeft.Y + Math.Max(0, (bottomRight.Y - topLeft.Y - ActualHeight) / 2);
    }

    private void UpdateWindowLayering()
    {
        ApplyWindowLayering();
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
            new Action(ApplyWindowLayering));
    }

    private void ApplyWindowLayering()
    {
        // Keep the operator controls reachable when the clean output shares the primary display.
        var keepAboveOutput = _selectedScreen.Primary;

        if (keepAboveOutput && IsLoaded)
        {
            Activate();
            Focus();
        }

        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            var layeringApplied = SetWindowPos(handle, keepAboveOutput ? IntPtr.Zero : new IntPtr(-2),
                0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
            DiagnosticLog.Write(
                $"ControlLayering handle={handle} primary={_selectedScreen.Primary} aboveOutput={keepAboveOutput} applied={layeringApplied} error={Marshal.GetLastWin32Error()}");
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint flags);
}



