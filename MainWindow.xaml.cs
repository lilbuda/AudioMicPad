using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AudioMicPad;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<SoundItem> _sounds = new();
    private readonly ObservableCollection<PlaybackShortcutItem> _playbackShortcuts = new();
    private readonly MMDeviceEnumerator _enumerator = new();
    private AudioEngine? _engine;
    private readonly string[] _extensions = [".wav", ".mp3", ".m4a", ".aac", ".wma", ".flac", ".ogg"];
    private readonly string _settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioMicPad", "settings.json");
    private AppSettings _settings = new();

    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;
    private const int HotkeyIdBase = 1000;
    private const int PlaybackHotkeyIdBase = 2000;
    private readonly Dictionary<int, SoundItem> _registeredHotkeys = new();
    private readonly Dictionary<int, PlaybackShortcutItem> _registeredPlaybackHotkeys = new();
    private HwndSource? _source;
    private bool _isCapturingShortcut;
    private SoundItem? _capturingSoundShortcut;
    private PlaybackShortcutItem? _capturingPlaybackShortcut;

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public MainWindow()
    {
        InitializeComponent();
        LoadSettings();
        ThemeBox.ItemsSource = Enum.GetValues<AppTheme>();
        ThemeBox.SelectedItem = _settings.Theme;
        ThemeManager.Apply(_settings.Theme, this);

        InitializePlaybackShortcuts();
        PlaybackShortcutsList.ItemsSource = _playbackShortcuts;
        SoundsList.ItemsSource = _sounds;
        LoadDevices();

        FolderBox.Text = Directory.Exists(_settings.Folder) ? _settings.Folder : Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        if (Directory.Exists(FolderBox.Text)) LoadSounds(FolderBox.Text);

        Loaded += (_, _) =>
        {
            RegisterGlobalHotkeys();
            if (AutoStart.IsChecked == true) RestartEngine();
        };
        SourceInitialized += (_, _) =>
        {
            ThemeManager.Apply(_settings.Theme, this);
            RegisterGlobalHotkeys();
        };
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += SystemThemeChanged;
        Closing += (_, _) =>
        {
            SaveSettings();
            UnregisterGlobalHotkeys();
            DisposeEngine();
            Microsoft.Win32.SystemEvents.UserPreferenceChanged -= SystemThemeChanged;
        };
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        Deactivated += (_, _) =>
        {
            if (!_isCapturingShortcut) return;
            CancelShortcutCapture();
            UpdateShortcutEditor();
        };
    }

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
                _settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath)) ?? new AppSettings();
        }
        catch { _settings = new AppSettings(); }
    }

    private void InitializePlaybackShortcuts()
    {
        _playbackShortcuts.Clear();
        foreach (var action in Enum.GetValues<PlaybackAction>())
        {
            HotkeySetting? hotkey = null;
            _settings.PlaybackHotkeys?.TryGetValue(action.ToString(), out hotkey);
            _playbackShortcuts.Add(new PlaybackShortcutItem(action, hotkey?.IsValid == true ? hotkey : null));
        }
    }

    private void SaveSettings()
    {
        try
        {
            _settings.Folder = FolderBox.Text;
            _settings.MicId = (MicBox.SelectedItem as DeviceChoice)?.Id ?? _settings.MicId;
            _settings.OutputId = (OutputBox.SelectedItem as DeviceChoice)?.Id ?? _settings.OutputId;
            _settings.MonitorId = (MonitorBox.SelectedItem as DeviceChoice)?.Id ?? _settings.MonitorId;
            _settings.MonitorEnabled = MonitorBoxEnabled.IsChecked == true;
            _settings.MicVolume = MicVolume.Value;
            _settings.MusicToMicVolume = MusicToMicVolume.Value;
            _settings.MusicToHeadsetVolume = MusicToHeadsetVolume.Value;
            _settings.SoundVolume = MusicToMicVolume.Value;
            _settings.Theme = ThemeBox.SelectedItem is AppTheme theme ? theme : AppTheme.System;

            var hotkeys = _settings.Hotkeys ?? new Dictionary<string, HotkeySetting>(StringComparer.OrdinalIgnoreCase);
            foreach (var sound in _sounds)
            {
                var existingPath = hotkeys.Keys.FirstOrDefault(path => string.Equals(path, sound.FilePath, StringComparison.OrdinalIgnoreCase));
                if (existingPath != null) hotkeys.Remove(existingPath);
                if (sound.Hotkey != null) hotkeys[sound.FilePath] = sound.Hotkey;
            }
            _settings.Hotkeys = hotkeys;
            _settings.PlaybackHotkeys = _playbackShortcuts
                .Where(item => item.Hotkey != null)
                .ToDictionary(item => item.Action.ToString(), item => item.Hotkey!);

            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private void LoadDevices()
    {
        var mics = _enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .Select(d => new DeviceChoice(d.ID, d.FriendlyName)).ToList();
        var outs = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .Select(d => new DeviceChoice(d.ID, d.FriendlyName)).ToList();

        MicBox.ItemsSource = mics;
        OutputBox.ItemsSource = outs;
        MonitorBox.ItemsSource = outs;

        MicBox.SelectedItem = mics.FirstOrDefault(d => d.Id == _settings.MicId)
            ?? mics.FirstOrDefault(d => d.Name.Contains("Microphone", StringComparison.OrdinalIgnoreCase))
            ?? mics.FirstOrDefault();
        OutputBox.SelectedItem = outs.FirstOrDefault(d => d.Id == _settings.OutputId)
            ?? outs.FirstOrDefault(d => d.Name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase))
            ?? outs.FirstOrDefault();
        MonitorBox.SelectedItem = outs.FirstOrDefault(d => d.Id == _settings.MonitorId)
            ?? outs.FirstOrDefault(d => d.Name.Contains("Razer", StringComparison.OrdinalIgnoreCase))
            ?? outs.FirstOrDefault(d => !d.Name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase));

        MonitorBoxEnabled.IsChecked = _settings.MonitorEnabled;
        MicVolume.Value = Math.Clamp(_settings.MicVolume, MicVolume.Minimum, MicVolume.Maximum);
        MusicToMicVolume.Value = _settings.MusicToMicVolume ?? _settings.SoundVolume;
        MusicToHeadsetVolume.Value = _settings.MusicToHeadsetVolume ?? _settings.SoundVolume;
    }

    private void LoadSounds(string folder)
    {
        _sounds.Clear();
        if (!Directory.Exists(folder))
        {
            SoundCountText.Text = "No sounds loaded";
            UpdateShortcutEditor();
            RefreshGlobalHotkeys();
            return;
        }

        var useDefaultHotkeys = _settings.Hotkeys == null;
        foreach (var file in Directory.EnumerateFiles(folder)
            .Where(f => _extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            var item = new SoundItem(Path.GetFileNameWithoutExtension(file), file, _sounds.Count + 1);
            var savedHotkey = _settings.Hotkeys?.FirstOrDefault(pair =>
                string.Equals(pair.Key, file, StringComparison.OrdinalIgnoreCase)).Value;

            if (savedHotkey?.IsValid == true)
                item.Hotkey = savedHotkey;
            else if (useDefaultHotkeys && item.Index <= 9)
                item.Hotkey = new HotkeySetting(ModifierKeys.Control | ModifierKeys.Alt, (Key)((int)Key.D1 + item.Index - 1));

            _sounds.Add(item);
        }

        SoundCountText.Text = _sounds.Count == 1 ? "1 sound loaded" : $"{_sounds.Count} sounds loaded";
        UpdateShortcutEditor();
        RefreshGlobalHotkeys();
    }

    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            SelectedPath = Directory.Exists(FolderBox.Text) ? FolderBox.Text : Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            FolderBox.Text = dialog.SelectedPath;
            _settings.Folder = dialog.SelectedPath;
            LoadSounds(dialog.SelectedPath);
            SaveSettings();
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => LoadSounds(FolderBox.Text);

    private void SoundsList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SoundsList.SelectedItem is SoundItem item) PlayEffect(item);
    }

    private void SoundsList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        CancelShortcutCapture();
        UpdateShortcutEditor();
        if (SoundsList.SelectedItem is SoundItem item) _engine?.SelectPlaylistTrack(item.FilePath);
    }

    private void PlaySelected_Click(object sender, RoutedEventArgs e)
    {
        if (SoundsList.SelectedItem is SoundItem item)
        {
            try
            {
                _engine?.PlayPlaylist(item.FilePath, LoopSongBox.IsChecked == true, LoopPlaylistBox.IsChecked == true);
                StatusText.Text = $"Playing: {item.Name}";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Playlist playback error: " + ex.Message;
            }
        }
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        _engine?.PausePlaylist();
        StatusText.Text = "Playlist paused.";
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _engine?.StopPlaylist();
        StatusText.Text = "Playlist stopped.";
    }

    private void Previous_Click(object sender, RoutedEventArgs e) => MovePlaylist(-1);
    private void Next_Click(object sender, RoutedEventArgs e) => MovePlaylist(1);

    private void MovePlaylist(int direction)
    {
        if (_sounds.Count == 0) return;
        int current = SoundsList.SelectedIndex;
        if (current < 0) current = direction > 0 ? 0 : _sounds.Count - 1;
        else current += direction;
        if (current < 0) current = LoopPlaylistBox.IsChecked == true ? _sounds.Count - 1 : 0;
        if (current >= _sounds.Count) current = LoopPlaylistBox.IsChecked == true ? 0 : _sounds.Count - 1;
        SoundsList.SelectedIndex = current;
        SoundsList.ScrollIntoView(SoundsList.SelectedItem);
        PlaySelected_Click(this, new RoutedEventArgs());
    }

    private void LoopMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender == LoopSongBox && LoopSongBox.IsChecked == true) LoopPlaylistBox.IsChecked = false;
        if (sender == LoopPlaylistBox && LoopPlaylistBox.IsChecked == true) LoopSongBox.IsChecked = false;
        _engine?.SetLoopMode(LoopSongBox.IsChecked == true, LoopPlaylistBox.IsChecked == true);
    }

    private void PlayEffect(SoundItem item)
    {
        if (_engine == null)
        {
            StatusText.Text = "Audio engine is not running.";
            return;
        }
        try
        {
            _engine.PlayEffect(item.FilePath);
            item.Status = "Playing";
        }
        catch (Exception ex)
        {
            item.Status = "Error";
            StatusText.Text = "Playback error: " + ex.Message;
        }
    }

    private void RegisterGlobalHotkeys()
    {
        if (_source == null)
        {
            _source = PresentationSource.FromVisual(this) as HwndSource;
            if (_source == null) return;
            _source.AddHook(WndProc);
        }

        RefreshGlobalHotkeys();
    }

    private void RefreshGlobalHotkeys()
    {
        if (_source == null) return;

        UnregisterRegisteredHotkeys();

        for (var index = 0; index < _sounds.Count; index++)
        {
            var sound = _sounds[index];
            if (sound.Hotkey?.IsValid != true) continue;

            var id = HotkeyIdBase + index;
            var modifiers = ToNativeModifiers(sound.Hotkey.Modifiers) | MOD_NOREPEAT;
            var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(sound.Hotkey.Key);
            if (RegisterHotKey(_source.Handle, id, modifiers, virtualKey))
            {
                _registeredHotkeys[id] = sound;
                if (sound.Status == "Shortcut unavailable") sound.Status = "Ready";
            }
            else
            {
                sound.Status = "Shortcut unavailable";
            }
        }

        for (var index = 0; index < _playbackShortcuts.Count; index++)
        {
            var item = _playbackShortcuts[index];
            if (item.Hotkey?.IsValid != true) continue;

            var id = PlaybackHotkeyIdBase + index;
            var modifiers = ToNativeModifiers(item.Hotkey.Modifiers) | MOD_NOREPEAT;
            var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(item.Hotkey.Key);
            item.IsAvailable = RegisterHotKey(_source.Handle, id, modifiers, virtualKey);
            if (item.IsAvailable) _registeredPlaybackHotkeys[id] = item;
        }
    }

    private void UnregisterRegisteredHotkeys()
    {
        if (_source == null) return;
        foreach (var id in _registeredHotkeys.Keys)
            UnregisterHotKey(_source.Handle, id);
        _registeredHotkeys.Clear();
        foreach (var id in _registeredPlaybackHotkeys.Keys)
            UnregisterHotKey(_source.Handle, id);
        _registeredPlaybackHotkeys.Clear();
    }

    private void UnregisterGlobalHotkeys()
    {
        if (_source == null) return;
        UnregisterRegisteredHotkeys();
        _source.RemoveHook(WndProc);
        _source = null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            var id = wParam.ToInt32();
            if (_registeredHotkeys.TryGetValue(id, out var sound))
            {
                PlayEffect(sound);
                handled = true;
            }
            else if (_registeredPlaybackHotkeys.TryGetValue(id, out var playback))
            {
                ExecutePlaybackAction(playback.Action);
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    private void ExecutePlaybackAction(PlaybackAction action)
    {
        switch (action)
        {
            case PlaybackAction.Play:
                PlaySelected_Click(this, new RoutedEventArgs());
                break;
            case PlaybackAction.Pause:
                Pause_Click(this, new RoutedEventArgs());
                break;
            case PlaybackAction.Stop:
                Stop_Click(this, new RoutedEventArgs());
                break;
            case PlaybackAction.Previous:
                Previous_Click(this, new RoutedEventArgs());
                break;
            case PlaybackAction.Next:
                Next_Click(this, new RoutedEventArgs());
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }
    }

    private void AssignShortcut_Click(object sender, RoutedEventArgs e)
    {
        if (SoundsList.SelectedItem is not SoundItem item) return;
        CancelShortcutCapture();
        _isCapturingShortcut = true;
        _capturingSoundShortcut = item;
        UnregisterRegisteredHotkeys();
        AssignShortcutButton.Content = "Press shortcut...";
        ShortcutHelpText.Text = $"Press a shortcut for {item.Name}. Esc cancels; Backspace clears.";
        Focus();
        Keyboard.Focus(this);
    }

    private void ClearShortcut_Click(object sender, RoutedEventArgs e)
    {
        if (SoundsList.SelectedItem is not SoundItem item) return;
        item.Hotkey = null;
        CancelShortcutCapture();
        RefreshGlobalHotkeys();
        SaveSettings();
        UpdateShortcutEditor();
    }

    private void AssignPlaybackShortcut_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PlaybackShortcutItem item }) return;
        CancelShortcutCapture();
        _isCapturingShortcut = true;
        _capturingPlaybackShortcut = item;
        UnregisterRegisteredHotkeys();
        PlaybackShortcutHelpText.Text = $"Press a shortcut for {item.Name}. Esc cancels; Backspace clears.";
        Focus();
        Keyboard.Focus(this);
    }

    private void ClearPlaybackShortcut_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PlaybackShortcutItem item }) return;
        item.Hotkey = null;
        CancelShortcutCapture();
        RefreshGlobalHotkeys();
        SaveSettings();
        UpdatePlaybackShortcutHelp();
    }

    private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_isCapturingShortcut) return;
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            CancelShortcutCapture();
            UpdateShortcutEditor();
            UpdatePlaybackShortcutHelp();
            return;
        }

        if (key is Key.Back or Key.Delete)
        {
            if (_capturingSoundShortcut != null) _capturingSoundShortcut.Hotkey = null;
            if (_capturingPlaybackShortcut != null) _capturingPlaybackShortcut.Hotkey = null;
            CancelShortcutCapture();
            RefreshGlobalHotkeys();
            SaveSettings();
            UpdateShortcutEditor();
            UpdatePlaybackShortcutHelp();
            return;
        }

        if (IsModifierKey(key)) return;

        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.None && !CanUseWithoutModifiers(key))
        {
            SetShortcutCaptureMessage("Use Ctrl, Alt, Shift, or Windows with this key. Function and media keys can be used alone.");
            return;
        }

        var duplicateOwner = FindShortcutOwner(modifiers, key);
        if (duplicateOwner != null)
        {
            SetShortcutCaptureMessage($"{HotkeySetting.Format(modifiers, key)} is already assigned to {duplicateOwner}.");
            return;
        }

        var hotkey = new HotkeySetting(modifiers, key);
        if (_capturingSoundShortcut != null) _capturingSoundShortcut.Hotkey = hotkey;
        if (_capturingPlaybackShortcut != null) _capturingPlaybackShortcut.Hotkey = hotkey;
        CancelShortcutCapture();
        RefreshGlobalHotkeys();
        SaveSettings();
        UpdateShortcutEditor();
        UpdatePlaybackShortcutHelp();
    }

    private void CancelShortcutCapture()
    {
        var wasCapturing = _isCapturingShortcut;
        _isCapturingShortcut = false;
        _capturingSoundShortcut = null;
        _capturingPlaybackShortcut = null;
        AssignShortcutButton.Content = "Assign shortcut";
        if (wasCapturing)
        {
            RefreshGlobalHotkeys();
            UpdateShortcutEditor();
            UpdatePlaybackShortcutHelp();
        }
    }

    private string? FindShortcutOwner(ModifierKeys modifiers, Key key)
    {
        var sound = _sounds.FirstOrDefault(item => item != _capturingSoundShortcut &&
            item.Hotkey?.Key == key && item.Hotkey.Modifiers == modifiers);
        if (sound != null) return sound.Name;

        var playback = _playbackShortcuts.FirstOrDefault(item => item != _capturingPlaybackShortcut &&
            item.Hotkey?.Key == key && item.Hotkey.Modifiers == modifiers);
        return playback?.Name;
    }

    private void SetShortcutCaptureMessage(string message)
    {
        if (_capturingSoundShortcut != null) ShortcutHelpText.Text = message;
        if (_capturingPlaybackShortcut != null) PlaybackShortcutHelpText.Text = message;
    }

    private void UpdateShortcutEditor()
    {
        if (SoundsList.SelectedItem is not SoundItem item)
        {
            AssignShortcutButton.IsEnabled = false;
            ClearShortcutButton.IsEnabled = false;
            ShortcutHelpText.Text = "Select a sound to assign a global shortcut.";
            return;
        }

        AssignShortcutButton.IsEnabled = true;
        ClearShortcutButton.IsEnabled = item.Hotkey != null;
        ShortcutHelpText.Text = item.Hotkey == null
            ? $"No shortcut assigned to {item.Name}."
            : $"{item.Key} plays {item.Name}, even when this window is not focused.";
    }

    private void UpdatePlaybackShortcutHelp()
    {
        PlaybackShortcutHelpText.Text = "Assign optional global shortcuts for the playback controls.";
    }

    private static uint ToNativeModifiers(ModifierKeys modifiers)
    {
        var native = 0u;
        if (modifiers.HasFlag(ModifierKeys.Control)) native |= MOD_CONTROL;
        if (modifiers.HasFlag(ModifierKeys.Alt)) native |= MOD_ALT;
        if (modifiers.HasFlag(ModifierKeys.Shift)) native |= MOD_SHIFT;
        if (modifiers.HasFlag(ModifierKeys.Windows)) native |= MOD_WIN;
        return native;
    }

    private static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
        Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    private static bool CanUseWithoutModifiers(Key key) =>
        key is >= Key.F1 and <= Key.F24 or
        Key.MediaPlayPause or Key.MediaNextTrack or Key.MediaPreviousTrack or Key.MediaStop or
        Key.VolumeUp or Key.VolumeDown or Key.VolumeMute;

    private void ThemeBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ThemeBox.SelectedItem is not AppTheme theme) return;
        _settings.Theme = theme;
        ThemeManager.Apply(theme, this);
        if (IsLoaded) SaveSettings();
    }

    private void Info_Click(object sender, RoutedEventArgs e)
    {
        var helpWindow = new HelpWindow
        {
            Owner = this
        };
        ThemeManager.Apply(_settings.Theme, helpWindow);
        helpWindow.SourceInitialized += (_, _) => ThemeManager.Apply(_settings.Theme, helpWindow);
        helpWindow.ShowDialog();
    }

    private void SystemThemeChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (_settings.Theme != AppTheme.System) return;
        Dispatcher.BeginInvoke(() => ThemeManager.Apply(AppTheme.System, this));
    }

    private void Restart_Click(object sender, RoutedEventArgs e) => RestartEngine();

    private void RestartEngine()
    {
        try
        {
            if (MicBox.SelectedItem is not DeviceChoice mic || OutputBox.SelectedItem is not DeviceChoice output)
            {
                StatusText.Text = "Select a microphone and a CABLE output.";
                return;
            }
            DisposeEngine();
            var monitor = MonitorBoxEnabled.IsChecked == true && MonitorBox.SelectedItem is DeviceChoice m ? m : null;
            _engine = new AudioEngine(
                mic.Id,
                output.Id,
                monitor?.Id,
                MicVolume.Value,
                MusicToMicVolume.Value,
                MusicToHeadsetVolume.Value,
                _sounds.Select(s => s.FilePath).ToList());
            _engine.Start();
            SaveSettings();
            StatusText.Text = monitor == null
                ? $"Running. Voice + sounds → {output.Name}"
                : $"Running. Voice + sounds → {output.Name}; sounds → {monitor.Name}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Could not start audio engine: " + ex.Message;
        }
    }

    private void StopAll_Click(object sender, RoutedEventArgs e)
    {
        _engine?.StopAll();
        foreach (var sound in _sounds) sound.Status = "Ready";
        StatusText.Text = "All audio stopped.";
    }

    private void DisposeEngine()
    {
        _engine?.Dispose();
        _engine = null;
    }

    private void MicBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) { }
    private void OutputBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) { }
    private void MonitorBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) { }
    private void MonitorEnabled_Click(object sender, RoutedEventArgs e) { if (IsLoaded) RestartEngine(); }
    private void MicVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => _engine?.SetMicVolume(e.NewValue);
    private void MusicToMicVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => _engine?.SetMusicToMicVolume(e.NewValue);
    private void MusicToHeadsetVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => _engine?.SetMusicToHeadsetVolume(e.NewValue);
}

public sealed class AppSettings
{
    public string Folder { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
    public string? MicId { get; set; }
    public string? OutputId { get; set; }
    public string? MonitorId { get; set; }
    public bool MonitorEnabled { get; set; } = true;
    public double MicVolume { get; set; } = 1;
    public double? MusicToMicVolume { get; set; }
    public double? MusicToHeadsetVolume { get; set; }
    public double SoundVolume { get; set; } = 1;
    public AppTheme Theme { get; set; } = AppTheme.System;
    public Dictionary<string, HotkeySetting>? Hotkeys { get; set; }
    public Dictionary<string, HotkeySetting>? PlaybackHotkeys { get; set; }
}

public enum PlaybackAction
{
    Play,
    Pause,
    Stop,
    Previous,
    Next
}

public sealed class PlaybackShortcutItem : System.ComponentModel.INotifyPropertyChanged
{
    private HotkeySetting? _hotkey;
    private bool _isAvailable = true;

    public PlaybackAction Action { get; }
    public string Name => Action.ToString();
    public bool HasHotkey => Hotkey != null;
    public string Key => Hotkey == null
        ? "Not assigned"
        : IsAvailable ? Hotkey.ToString() : $"{Hotkey} (unavailable)";

    public HotkeySetting? Hotkey
    {
        get => _hotkey;
        set
        {
            _hotkey = value;
            _isAvailable = true;
            NotifyShortcutChanged();
        }
    }

    public bool IsAvailable
    {
        get => _isAvailable;
        set
        {
            if (_isAvailable == value) return;
            _isAvailable = value;
            PropertyChanged?.Invoke(this, new(nameof(IsAvailable)));
            PropertyChanged?.Invoke(this, new(nameof(Key)));
        }
    }

    public PlaybackShortcutItem(PlaybackAction action, HotkeySetting? hotkey)
    {
        Action = action;
        _hotkey = hotkey;
    }

    private void NotifyShortcutChanged()
    {
        PropertyChanged?.Invoke(this, new(nameof(Hotkey)));
        PropertyChanged?.Invoke(this, new(nameof(HasHotkey)));
        PropertyChanged?.Invoke(this, new(nameof(Key)));
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

public sealed class HotkeySetting
{
    public ModifierKeys Modifiers { get; set; }
    public Key Key { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValid => Key != Key.None && KeyInterop.VirtualKeyFromKey(Key) != 0;

    public HotkeySetting() { }

    public HotkeySetting(ModifierKeys modifiers, Key key)
    {
        Modifiers = modifiers;
        Key = key;
    }

    public override string ToString() => Format(Modifiers, Key);

    public static string Format(ModifierKeys modifiers, Key key)
    {
        var parts = new List<string>(5);
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");

        var keyText = new KeyConverter().ConvertToInvariantString(key) ?? key.ToString();
        parts.Add(keyText);
        return string.Join("+", parts);
    }
}

public sealed class DeviceChoice
{
    public string Id { get; }
    public string Name { get; }
    public DeviceChoice(string id, string name) { Id = id; Name = name; }
    public override string ToString() => Name;
}

public sealed class SoundItem : System.ComponentModel.INotifyPropertyChanged
{
    public string Name { get; }
    public string FilePath { get; }
    public int Index { get; }
    public string IndexDisplay => Index.ToString();
    private HotkeySetting? _hotkey;
    private string _status = "Ready";
    public HotkeySetting? Hotkey
    {
        get => _hotkey;
        set
        {
            _hotkey = value;
            PropertyChanged?.Invoke(this, new(nameof(Hotkey)));
            PropertyChanged?.Invoke(this, new(nameof(Key)));
        }
    }
    public string Key => Hotkey?.ToString() ?? "—";
    public string Status { get => _status; set { _status = value; PropertyChanged?.Invoke(this, new(nameof(Status))); } }
    public SoundItem(string name, string filePath, int index) { Name = name; FilePath = filePath; Index = index; }
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

internal sealed class AudioEngine : IDisposable
{
    private const int SampleRate = 48000;
    private const int Channels = 2;
    private const int AudioLatencyMilliseconds = 20;
    private const int MicrophoneBufferMilliseconds = 100;
    private readonly MMDevice _mic;
    private readonly MMDevice _cableOutput;
    private readonly MMDevice? _monitor;
    private readonly WasapiCapture _capture;
    private readonly BufferedWaveProvider _micBuffer;
    private readonly MixingSampleProvider _cableMixer;
    private readonly MixingSampleProvider _monitorMixer;
    private readonly MixingSampleProvider _cableSoundMixer;
    private readonly MixingSampleProvider _monitorSoundMixer;
    private readonly WasapiOut _cableOut;
    private readonly WasapiOut? _monitorOut;
    private readonly VolumeSampleProvider _micVolume;
    private readonly VolumeSampleProvider _musicToMicVolume;
    private readonly VolumeSampleProvider _musicToHeadsetVolume;
    private readonly List<IDisposable> _activeReaders = new();
    private readonly object _gate = new();
    private readonly List<string> _playlist;
    private PlaylistPlayer? _playlistPlayer;

    public AudioEngine(
        string micId,
        string cableOutputId,
        string? monitorId,
        double micVolume,
        double musicToMicVolume,
        double musicToHeadsetVolume,
        List<string> playlist)
    {
        var en = new MMDeviceEnumerator();
        _mic = en.GetDevice(micId);
        _cableOutput = en.GetDevice(cableOutputId);
        _monitor = monitorId == null ? null : en.GetDevice(monitorId);
        _playlist = playlist;

        _capture = new WasapiCapture(_mic, true, AudioLatencyMilliseconds) { ShareMode = AudioClientShareMode.Shared };
        _capture.WaveFormat = new WaveFormat(SampleRate, 16, 1);
        _micBuffer = new BufferedWaveProvider(_capture.WaveFormat)
        {
            BufferDuration = TimeSpan.FromMilliseconds(MicrophoneBufferMilliseconds),
            DiscardOnBufferOverflow = true,
            ReadFully = true
        };

        _cableMixer = NewMixer();
        _monitorMixer = NewMixer();
        _cableSoundMixer = NewMixer();
        _monitorSoundMixer = NewMixer();

        var micMono = _micBuffer.ToSampleProvider();
        var micStereo = new MonoToStereoSampleProvider(micMono);
        _micVolume = new VolumeSampleProvider(micStereo) { Volume = (float)micVolume };
        _musicToMicVolume = new VolumeSampleProvider(_cableSoundMixer) { Volume = (float)musicToMicVolume };
        _musicToHeadsetVolume = new VolumeSampleProvider(_monitorSoundMixer) { Volume = (float)musicToHeadsetVolume };
        _cableMixer.AddMixerInput(_micVolume);
        _cableMixer.AddMixerInput(_musicToMicVolume);
        _monitorMixer.AddMixerInput(_musicToHeadsetVolume);

        _cableOut = new WasapiOut(_cableOutput, AudioClientShareMode.Shared, true, AudioLatencyMilliseconds);
        _cableOut.Init(_cableMixer.ToWaveProvider16());

        if (_monitor != null)
        {
            _monitorOut = new WasapiOut(_monitor, AudioClientShareMode.Shared, true, AudioLatencyMilliseconds);
            _monitorOut.Init(_monitorMixer.ToWaveProvider16());
        }

        _capture.DataAvailable += Capture_DataAvailable;
    }

    private static MixingSampleProvider NewMixer() => new(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels)) { ReadFully = true };

    private static ISampleProvider EnsureFloat(ISampleProvider provider) =>
        provider.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat
            ? provider
            : new FloatFormatSampleProvider(provider);

    private void Capture_DataAvailable(object? sender, WaveInEventArgs e) => _micBuffer.AddSamples(e.Buffer, 0, e.BytesRecorded);

    public void Start()
    {
        _cableOut.Play();
        _monitorOut?.Play();
        _capture.StartRecording();
    }

    public void SetMicVolume(double value) => _micVolume.Volume = (float)value;
    public void SetMusicToMicVolume(double value) => _musicToMicVolume.Volume = (float)value;
    public void SetMusicToHeadsetVolume(double value) => _musicToHeadsetVolume.Volume = (float)value;

    public void PlayEffect(string path)
    {
        var cable = CreateProvider(path);
        AddSoundToMixer(cable, _cableSoundMixer);
        if (_monitorOut != null)
        {
            var monitor = CreateProvider(path);
            AddSoundToMixer(monitor, _monitorSoundMixer);
        }
    }

    private ISampleProvider CreateProvider(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Sound file not found", path);
        var reader = new MediaFoundationReader(path);
        ISampleProvider sample = EnsureFloat(reader.ToSampleProvider());
        if (sample.WaveFormat.SampleRate != SampleRate)
            sample = new WdlResamplingSampleProvider(sample, SampleRate);
        if (sample.WaveFormat.Channels == 1)
            sample = new MonoToStereoSampleProvider(sample);
        else if (sample.WaveFormat.Channels > 2)
            sample = new MonoToStereoSampleProvider(new StereoToMonoSampleProvider(sample));
        return new OwnedSampleProvider(sample, reader);
    }

    private void AddSoundToMixer(ISampleProvider provider, MixingSampleProvider mixer)
    {
        if (provider is not OwnedSampleProvider owned) return;
        AutoRemovingProvider? auto = null;
        auto = new AutoRemovingProvider(owned.Inner, owned.Owner, () =>
        {
            if (auto != null) mixer.RemoveMixerInput(auto);
            lock (_gate) _activeReaders.Remove(owned.Owner);
            owned.Owner.Dispose();
        });
        lock (_gate) _activeReaders.Add(owned.Owner);
        mixer.AddMixerInput(auto);
    }

    public void SelectPlaylistTrack(string path) { _playlistPlayer?.Select(path); }

    public void PlayPlaylist(string path, bool loopSong, bool loopPlaylist)
    {
        if (_playlistPlayer == null)
            _playlistPlayer = new PlaylistPlayer(this, _playlist);
        _playlistPlayer.Play(path, loopSong, loopPlaylist);
    }

    private void AddPlaylistProvider(ISampleProvider provider, MixingSampleProvider mixer)
    {
        mixer.AddMixerInput(provider);
    }

    public void PausePlaylist() => _playlistPlayer?.Pause();
    public void StopPlaylist() => _playlistPlayer?.Stop();
    public void SetLoopMode(bool loopSong, bool loopPlaylist) => _playlistPlayer?.SetLoopMode(loopSong, loopPlaylist);

    public void StopAll()
    {
        _playlistPlayer?.Stop();
        lock (_gate)
        {
            foreach (var r in _activeReaders.ToList()) r.Dispose();
            _activeReaders.Clear();
        }
        _cableSoundMixer.RemoveAllMixerInputs();
        _monitorSoundMixer.RemoveAllMixerInputs();
    }

    public void Dispose()
    {
        try { _playlistPlayer?.Dispose(); } catch { }
        try { _capture.StopRecording(); } catch { }
        _capture.DataAvailable -= Capture_DataAvailable;
        _capture.Dispose();
        _cableOut.Stop();
        _cableOut.Dispose();
        _monitorOut?.Stop();
        _monitorOut?.Dispose();
        _micBuffer.ClearBuffer();
        lock (_gate)
        {
            foreach (var r in _activeReaders) r.Dispose();
            _activeReaders.Clear();
        }
        _mic.Dispose();
        _cableOutput.Dispose();
        _monitor?.Dispose();
    }

    private sealed class OwnedSampleProvider : ISampleProvider
    {
        public ISampleProvider Inner { get; }
        public IDisposable Owner { get; }
        public OwnedSampleProvider(ISampleProvider inner, IDisposable owner) { Inner = inner; Owner = owner; }
        public WaveFormat WaveFormat => Inner.WaveFormat;
        public int Read(float[] buffer, int offset, int count) => Inner.Read(buffer, offset, count);
    }

    private sealed class FloatFormatSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _inner;
        private readonly WaveFormat _waveFormat;

        public FloatFormatSampleProvider(ISampleProvider inner)
        {
            _inner = inner;
            _waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(inner.WaveFormat.SampleRate, inner.WaveFormat.Channels);
        }

        public WaveFormat WaveFormat => _waveFormat;
        public int Read(float[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
    }

    private sealed class PlaylistPlayer : IDisposable
    {
        private readonly AudioEngine _engine;
        private readonly List<string> _files;
        private string? _current;
        private bool _loopSong;
        private bool _loopPlaylist;
        private bool _paused;
        private TimeSpan _position = TimeSpan.Zero;
        private System.Timers.Timer? _timer;
        private MediaFoundationReader? _cableReader;
        private MediaFoundationReader? _monitorReader;
        private ISampleProvider? _cableProvider;
        private ISampleProvider? _monitorProvider;

        public PlaylistPlayer(AudioEngine engine, List<string> files)
        {
            _engine = engine;
            _files = files;
        }

        public void Select(string path) => _current = path;

        public void Play(string path, bool loopSong, bool loopPlaylist)
        {
            if (_paused && string.Equals(_current, path, StringComparison.OrdinalIgnoreCase))
            {
                _loopSong = loopSong;
                _loopPlaylist = loopPlaylist;
                _paused = false;
                StartCurrent();
                return;
            }

            StopCurrent();
            _current = path;
            _loopSong = loopSong;
            _loopPlaylist = loopPlaylist;
            _position = TimeSpan.Zero;
            _paused = false;
            StartCurrent();
        }

        public void Pause()
        {
            if (_paused || _cableReader == null) return;
            _position = _cableReader.CurrentTime;
            _paused = true;
            StopCurrent(disposeOnly: true);
        }

        public void Stop()
        {
            _paused = false;
            _position = TimeSpan.Zero;
            StopCurrent();
        }

        public void SetLoopMode(bool loopSong, bool loopPlaylist)
        {
            _loopSong = loopSong;
            _loopPlaylist = loopPlaylist;
        }

        private void StartCurrent()
        {
            if (_current == null || !File.Exists(_current)) return;

            StopCurrent();
            _cableReader = new MediaFoundationReader(_current);
            _cableReader.CurrentTime = _position;
            _cableProvider = PrepareReader(_cableReader);
            _engine.AddPlaylistProvider(_cableProvider, _engine._cableSoundMixer);

            if (_engine._monitorOut != null)
            {
                _monitorReader = new MediaFoundationReader(_current);
                _monitorReader.CurrentTime = _position;
                _monitorProvider = PrepareReader(_monitorReader);
                _engine.AddPlaylistProvider(_monitorProvider, _engine._monitorSoundMixer);
            }

            var remaining = Math.Max(50, (_cableReader.TotalTime - _position).TotalMilliseconds);
            _timer = new System.Timers.Timer(remaining);
            _timer.AutoReset = false;
            _timer.Elapsed += (_, _) =>
            {
                if (_paused) return;
                if (_loopSong)
                {
                    _position = TimeSpan.Zero;
                    StartCurrent();
                    return;
                }
                if (_loopPlaylist && _files.Count > 0)
                {
                    var idx = _files.FindIndex(f => string.Equals(f, _current, StringComparison.OrdinalIgnoreCase));
                    var next = idx < 0 ? 0 : (idx + 1) % _files.Count;
                    _current = _files[next];
                    _position = TimeSpan.Zero;
                    StartCurrent();
                    return;
                }
                StopCurrent();
            };
            _timer.Start();
        }

        private static ISampleProvider PrepareReader(MediaFoundationReader reader)
        {
            ISampleProvider sample = EnsureFloat(reader.ToSampleProvider());
            if (sample.WaveFormat.SampleRate != SampleRate)
                sample = new WdlResamplingSampleProvider(sample, SampleRate);
            if (sample.WaveFormat.Channels == 1)
                sample = new MonoToStereoSampleProvider(sample);
            else if (sample.WaveFormat.Channels > 2)
                sample = new MonoToStereoSampleProvider(new StereoToMonoSampleProvider(sample));
            return sample;
        }

        private void StopCurrent(bool disposeOnly = false)
        {
            _timer?.Stop();
            _timer?.Dispose();
            _timer = null;

            if (_cableProvider != null) _engine._cableSoundMixer.RemoveMixerInput(_cableProvider);
            if (_monitorProvider != null) _engine._monitorSoundMixer.RemoveMixerInput(_monitorProvider);

            _cableProvider = null;
            _monitorProvider = null;
            _cableReader?.Dispose();
            _monitorReader?.Dispose();
            _cableReader = null;
            _monitorReader = null;

            if (!disposeOnly) _position = TimeSpan.Zero;
        }

        public void Dispose() => StopCurrent();
    }

}

internal sealed class AutoRemovingProvider : ISampleProvider
{
    private readonly ISampleProvider _inner;
    private readonly IDisposable _owner;
    private readonly Action _finished;
    private int _done;
    public AutoRemovingProvider(ISampleProvider inner, IDisposable owner, Action finished) { _inner = inner; _owner = owner; _finished = finished; }
    public WaveFormat WaveFormat => _inner.WaveFormat;
    public int Read(float[] buffer, int offset, int count)
    {
        var read = _inner.Read(buffer, offset, count);
        if (read == 0 && Interlocked.Exchange(ref _done, 1) == 0) _finished();
        return read;
    }
}
