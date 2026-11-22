using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using SignalDx.Audio;
using SignalDx.Band;
using SignalDx.Cat;
using SignalDx.Controls;
using SignalDx.Dsp;
using SignalDx.Ft8;
using SignalDx.Geo;
using SignalDx.Logging;
using SignalDx.Models;
using SignalDx.Services;
using SignalDx.Wspr;

namespace SignalDx.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    readonly SettingsStore _settingsStore = new();
    readonly QsoLogStore _logStore = new();
    readonly WorkedCallStore _workedCalls = new();
    readonly WantedStore _wanted = new();
    readonly AudioCaptureService _capture = new();
    readonly AudioPlaybackService _playback = new();
    readonly CallAlertService _callAlert = new();
    readonly Dictionary<string, DateTime> _wantedAlertedUtc = new(StringComparer.OrdinalIgnoreCase);
    string _newWantedValue = "";
    int _selectedWantedKindIndex;
    readonly SpectrumAnalyzer _spectrum = new(4096);
    readonly SlotTiming _timing = new();
    readonly SimulatedDecoder _sim = new();
    readonly JtdxDecoderService _jtdx = new();
    readonly AutoSequencer _seq = new();
    int _jtdxDecodeSeq;
    bool _logPromptOpen;
    readonly CatService _cat = new();
    readonly ExternalLogRouter _external = new();
    readonly QrzLookupClient _qrzLookup = new();
    readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(100) };
    readonly DispatcherTimer _spectrumTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    readonly DispatcherTimer _heartbeat = new() { Interval = TimeSpan.FromSeconds(15) };
    readonly DispatcherTimer _settingsPersistTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };

    StationSettings _settings;
    string _dxCall = "";
    string _dxGrid = "";
    string _report = "-10";
    string _status = "Ready";
    string _catStatus = "CAT off";
    string _rigFreqText = "—";
    string _rigModeText = "—";
    string _rigPttText = "RX";
    string _rigInfoText = "";
    string _alcText = "ALC —";
    string _rfPowerText = "RF —";
    string _rxLevelText = "IN —";
    string _qrzInfoText = "";
    string _utcClockText = "00:00:00";
    string _currentFreqText = "—";
    string _freeText = "";
    string _newCustomMacroLabel = "CQ DX";
    string _newCustomMacroTemplate = "CQ DX {mycall} {mygrid}";
    string _slotLabel = "00:00";
    string _phaseLabel = "Idle";
    double _slotProgress;
    double _alcLevel;
    double _rxLevelNorm;
    bool _isTransmitting;
    bool _isTuning;
    CancellationTokenSource? _tuneCts;
    bool _rigPtt;
    int _qrzLookupSeq;
    DecodeHit? _selectedActivityDecode;
    DecodeHit? _selectedRxDecode;
    WaterfallControl? _waterfall;
    DateTime? _qsoStartUtc;
    /// <summary>True after the log dialog was offered for the current QSO (either side's first 73).</summary>
    bool _qsoLogPrompted;
    /// <summary>After Clear on RX Frequency, do not refill from Band Activity history.</summary>
    bool _rxHistorySuppressed;
    /// <summary>When true, every TX period sends CQ until a real grid reply arrives (or user picks a station).</summary>
    bool _holdCq;
    /// <summary>
    /// Auto-seq: wait for decode of the prior period before keying this slot,
    /// so a returned call advances the macro before we TX (instead of sending the old one).
    /// </summary>
    bool _deferTxUntilDecode;
    /// <summary>JTDX/WSJT-X “Tx next” — force TX on the next WSPR 2-minute slot once.</summary>
    bool _wsprTxNextArmed;
    /// <summary>Pre-scheduled TX for the upcoming WSPR slot (from Tx % random draw).</summary>
    bool _wsprScheduleThisSlot;
    string _wsprScheduleText = "";
    /// <summary>True while we are pushing DATA-U / digi mode back to the radio.</summary>
    bool _forcingDigiMode;
    DateTime _lastDigiModeForceUtc = DateTime.MinValue;
    DateTime _cqSessionUtc;
    float[]? _txAudioCache;
    string? _txAudioCacheKey;
    int _selectedHamlibModelIndex;
    string _hamlibModelFilter = "";
    string _hamlibModelCountText = "";
    List<HamlibModel> _hamlibModels = [];

    public MainViewModel()
    {
        _settings = _settingsStore.Load();
        if (string.IsNullOrWhiteSpace(_settings.AdifPath))
            _settings.AdifPath = AdifExporter.DefaultPath;
        _settings.MyCall = (_settings.MyCall ?? "").Trim().ToUpperInvariant();
        _settings.MyGrid = (_settings.MyGrid ?? "").Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(_settings.CqTemplate))
            _settings.CqTemplate = "CQ {mycall} {mygrid}";
        // null = first run → seed directed CQ macros; empty list = user cleared them.
        _settings.CustomMacros ??= Ft8MessageBuilder.DefaultCustomMacros
            .Select(m => new CustomMacroEntry { Label = m.Label, Template = m.Template })
            .ToList();
        // Older settings.json omitted Wanted* → deserialized as false; turn alerts on once.
        if (!_settings.WantedFeatureInitialized)
        {
            _settings.WantedAlertEnabled = true;
            _settings.WantedAlertCqOnly = true;
            _settings.WantedFeatureInitialized = true;
            _settingsStore.Save(_settings);
        }
        InitializeAndRestoreAudioHzMemory();
        PreferJtdxIfAvailable();

        foreach (var e in _logStore.Entries)
            Log.Add(e);
        // Seed / refresh the running worked-calls text file from the QSO log.
        _workedCalls.MergeFromLog(_logStore.Entries);

        RebuildMacroList();
        ReloadWantedList();

        InputDevices = new ObservableCollection<string>();
        OutputDevices = new ObservableCollection<string>();
        ReloadAudioDeviceLists(save: false);
        ComPorts = new ObservableCollection<string>();
        CatBackends = new ObservableCollection<string>(Enum.GetNames<CatBackend>());
        HamlibModes = new ObservableCollection<string>([
            "DATA-U", "PKTUSB", "DIGU", "USB-D", "USB", "LSB", "PKTLSB", "DATA-L", "FM", "AM", "CW"
        ]);
        // This app always drives digi USB-DATA — never keep plain USB/LSB as the preferred CAT mode.
        if (!LooksDigiMode(_settings.HamlibMode))
            _settings.HamlibMode = "DATA-U";
        WaterfallPalettes = new ObservableCollection<string>(WaterfallPalette.Names);
        RefreshPorts();
        ReloadHamlibModels(forceRefresh: false);
        RebuildBandPresetList();

        StartMonitorCommand = new RelayCommand(StartMonitor);
        StopMonitorCommand = new RelayCommand(StopMonitor);
        EnableTxCommand = new RelayCommand(() => EnableTx = !EnableTx);
        LogQsoCommand = new RelayCommand(() => _ = LogSelectedQsoAsync(), () => !string.IsNullOrWhiteSpace(DxCall));
        ClearActivityCommand = new RelayCommand(() =>
        {
            SelectedActivityDecode = null;
            SelectedRxDecode = null;
            Activity.Clear();
            _rxHistorySuppressed = false;
            RebuildRxFilter();
        });
        ClearRxActivityCommand = new RelayCommand(() =>
        {
            SelectedRxDecode = null;
            RxActivity.Clear();
            _rxHistorySuppressed = true;
            // Snap RX marker back to TX when the RX frequency pane is cleared.
            RxHz = TxHz;
        });
        SendMacroCommand = new RelayCommand(p => SendMacro(p as MacroDisplayItem), _ => EnableTx && !IsTransmitting);
        HaltTxCommand = new RelayCommand(() => _ = HaltTxAsync(), () => IsTransmitting || IsTuning);
        TuneCommand = new RelayCommand(() => _ = ToggleTuneAsync(), () => !IsTransmitting || IsTuning);
        SaveSettingsCommand = new RelayCommand(() => _ = SaveSettingsAsync());
        OpenSettingsCommand = new RelayCommand(OpenSettings);
        ConnectCatCommand = new RelayCommand(() => _ = ConnectCatAsync());
        DisconnectCatCommand = new RelayCommand(() => _ = DisconnectCatAsync());
        ApplyBandCommand = new RelayCommand(ApplySelectedBand);
        ExportAdifCommand = new RelayCommand(ExportAdif);
        StartCqCommand = new RelayCommand(StartCq);
        WsprTxNextCommand = new RelayCommand(ArmWsprTxNext, () => IsWsprMode && !IsTransmitting);
        ResetSeqCommand = new RelayCommand(() =>
        {
            _holdCq = false;
            _seq.Reset();
            PhaseLabel = "Idle";
        });
        ResetTxMacrosCommand = new RelayCommand(ResetTxMacros);
        AddCustomMacroCommand = new RelayCommand(AddCustomMacro);
        RemoveCustomMacroCommand = new RelayCommand(p => RemoveCustomMacro(p as MacroDisplayItem));
        ApplyManualDxCommand = new RelayCommand(ApplyManualDxCall);
        AddWantedCommand = new RelayCommand(AddWantedItem, () => !string.IsNullOrWhiteSpace(NewWantedValue));
        RemoveWantedCommand = new RelayCommand(p => RemoveWantedItem(p as WantedEntry));
        RefreshPortsCommand = new RelayCommand(RefreshPorts);
        RefreshHamlibModelsCommand = new RelayCommand(() => ReloadHamlibModels(forceRefresh: true));
        StartRigctldCommand = new RelayCommand(StartRigctld);
        StopRigctldCommand = new RelayCommand(() => _cat.StopRigctld());
        SetRigFreqCommand = new RelayCommand(() => _ = SetRigFreqAsync());
        SetRigModeCommand = new RelayCommand(() => _ = SetRigModeAsync());
        TogglePttCommand = new RelayCommand(() => _ = TogglePttAsync());
        RefreshRigCommand = new RelayCommand(() => _ = RefreshRigFromRadioAsync());
        FindRigctldCommand = new RelayCommand(FindRigctld);
        FindJtdxCommand = new RelayCommand(FindJtdx);
        LookupQrzCommand = new RelayCommand(
            () => LookupQrzInBrowser(),
            () => !string.IsNullOrWhiteSpace(ResolveQrzCall()));
        OpenQrzCommand = new RelayCommand(
            () => LookupQrzInBrowser(),
            () => !string.IsNullOrWhiteSpace(ResolveQrzCall()));

        _cat.StatusChanged += s => Application.Current.Dispatcher.BeginInvoke(() => CatStatus = s);
        // Radio VFO is source of truth for Dial/Band — follow CAT frequency changes.
        _cat.FrequencyChanged += hz => Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (!RigPtt)
                ApplyDialFromRadio(hz);
            else
                RigFreqText = $"{hz / 1_000_000d:0.000000} MHz";
        });
        _cat.SnapshotChanged += snap => Application.Current.Dispatcher.BeginInvoke(() => ApplySnapshot(snap));
        _external.StatusChanged += s => Application.Current.Dispatcher.BeginInvoke(() => Status = s);

        _timing.SetMode(Mode);
        _external.Configure(_settings);
        SyncWsprPowerFromSettings();
        if (IsWsprMode)
        {
            RefreshWsprFreeText();
            ScheduleNextWsprSlot(announce: false);
        }

        _clock.Tick += OnClock;
        _spectrumTimer.Tick += OnSpectrumTick;
        _heartbeat.Tick += (_, _) => _external.Heartbeat(_settings);
        _settingsPersistTimer.Tick += (_, _) =>
        {
            _settingsPersistTimer.Stop();
            SaveSettingsNow();
        };
        _clock.Start();
        _spectrumTimer.Start();
        _heartbeat.Start();

        PhaseLabel = _seq.Phase.ToString();
        UtcClockText = DateTime.UtcNow.ToString("HH:mm:ss");
        RefreshCurrentFreqText();
        Raise(nameof(TxEven));
        Raise(nameof(AutoSequence));
        Raise(nameof(Mode));

        ApplyDriveToAlcMeter();
        Status = "Starting…";
    }

    /// <summary>Call after the main window is visible so CAT/audio cannot block first paint.</summary>
    public void StartAfterUiReady()
    {
        // Restore combo selection after the window binds ItemsSource (Clear during Monitor used to blank it).
        ReloadAudioDeviceLists(save: false);
        RestoreAudioDeviceSelectionUi();
        StartMonitor();
        RestoreAudioDeviceSelectionUi();
        if (_settings.CatBackend != CatBackend.None)
            _ = ConnectCatAsync();
        if (!_capture.IsRunning && !_cat.IsConnected)
            Status = "Set call/grid, connect CAT if used, start monitor, enable TX when ready.";
    }

    public void RestoreAudioDeviceSelectionUi()
    {
        Raise(nameof(SelectedInputDevice));
        Raise(nameof(SelectedOutputDevice));
        Raise(nameof(SelectedInputIndex));
        Raise(nameof(SelectedOutputIndex));
        // ComboBox often ignores the first raise right after ItemsSource changes — nudge again.
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            Raise(nameof(SelectedInputDevice));
            Raise(nameof(SelectedOutputDevice));
        }, DispatcherPriority.Loaded);
    }

    public ObservableCollection<DecodeHit> Activity { get; } = [];
    public ObservableCollection<DecodeHit> RxActivity { get; } = [];
    public ObservableCollection<QsoEntry> Log { get; } = [];
    public ObservableCollection<MacroDisplayItem> Macros { get; } = [];
    public ObservableCollection<WantedEntry> WantedItems { get; } = [];
    public ObservableCollection<string> WantedKindChoices { get; } = ["Call", "State", "Country"];
    public ObservableCollection<string> InputDevices { get; }
    public ObservableCollection<string> OutputDevices { get; }
    public ObservableCollection<string> ComPorts { get; }
    public ObservableCollection<string> CatBackends { get; }
    public ObservableCollection<string> BandPresets { get; } = [];
    public ObservableCollection<string> Modes { get; } = ["FT8", "FT4", "WSPR2"];

    public bool IsWsprMode =>
        Mode.Equals("WSPR2", StringComparison.OrdinalIgnoreCase) ||
        Mode.Equals("WSPR", StringComparison.OrdinalIgnoreCase);

    public bool IsFtDigiMode => !IsWsprMode;

    public ObservableCollection<string> WsprPowerChoices { get; } = new(
        WsprEncoder.ValidPowerDbm.Select(WsprEncoder.FormatPowerLabel));

    public ObservableCollection<string> HamlibModelChoices { get; } = [];
    public ObservableCollection<string> HamlibModes { get; }
    public ObservableCollection<string> WaterfallPalettes { get; }

    public ICommand StartMonitorCommand { get; }
    public ICommand StopMonitorCommand { get; }
    public ICommand EnableTxCommand { get; }
    public ICommand LogQsoCommand { get; }
    public ICommand ClearActivityCommand { get; }
    public ICommand ClearRxActivityCommand { get; }
    public ICommand SendMacroCommand { get; }
    public ICommand HaltTxCommand { get; }
    public ICommand TuneCommand { get; }
    public ICommand SaveSettingsCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand ConnectCatCommand { get; }
    public ICommand DisconnectCatCommand { get; }
    public ICommand ApplyBandCommand { get; }
    public ICommand ExportAdifCommand { get; }
    public ICommand StartCqCommand { get; }
    public ICommand WsprTxNextCommand { get; }
    public ICommand ResetSeqCommand { get; }
    public ICommand ResetTxMacrosCommand { get; }
    public ICommand AddCustomMacroCommand { get; }
    public ICommand RemoveCustomMacroCommand { get; }
    public ICommand ApplyManualDxCommand { get; }
    public ICommand AddWantedCommand { get; }
    public ICommand RemoveWantedCommand { get; }
    public ICommand RefreshPortsCommand { get; }
    public ICommand RefreshHamlibModelsCommand { get; }
    public ICommand StartRigctldCommand { get; }
    public ICommand StopRigctldCommand { get; }
    public ICommand SetRigFreqCommand { get; }
    public ICommand SetRigModeCommand { get; }
    public ICommand LookupQrzCommand { get; }
    public ICommand OpenQrzCommand { get; }
    public ICommand TogglePttCommand { get; }
    public ICommand RefreshRigCommand { get; }
    public ICommand FindRigctldCommand { get; }
    public ICommand FindJtdxCommand { get; }

    public ObservableCollection<string> DecoderEngines { get; } = ["Simulate", "Jtdx"];

    public string AboutProductName => AppInfo.ProductName;
    public string AboutCreator => AppInfo.CreatorName;
    public string AboutCreatorCall => AppInfo.CreatorCall;
    public string AboutCreatorEmail => AppInfo.CreatorEmail;
    public string AboutVersion => AppInfo.Version;
    public string AboutVersionDate => AppInfo.VersionDateDisplay;
    public string AboutDateCreated => AppInfo.DateCreatedDisplay;
    public string AboutTagline => AppInfo.Tagline;
    public string AboutAcknowledgments => AppInfo.Acknowledgments;

    public string MyCall
    {
        get => _settings.MyCall;
        set
        {
            _settings.MyCall = (value ?? "").Trim().ToUpperInvariant();
            Raise();
            RefreshMacroPreviews();
            if (IsWsprMode) RefreshWsprFreeText();
        }
    }

    public string MyGrid
    {
        get => _settings.MyGrid;
        set
        {
            _settings.MyGrid = value;
            Raise();
            RefreshMacroPreviews();
            if (IsWsprMode) RefreshWsprFreeText();
        }
    }

    public string Band
    {
        get => _settings.Band;
        set
        {
            bool bandChanged = !string.Equals(_settings.Band, value, StringComparison.OrdinalIgnoreCase);
            _settings.Band = value;
            Raise();
            if (bandChanged)
                ClearActivityPanesForDialChange();
        }
    }

    public string Mode
    {
        get => _settings.Mode;
        set
        {
            string previous = _settings.Mode;
            if (string.Equals(previous, value, StringComparison.OrdinalIgnoreCase))
                return;
            SwapAudioHzForModeSwitch(previous, value);
            _settings.Mode = value;
            _timing.SetMode(value);
            ApplyModeToWaterfall();
            if (IsWsprMode)
                EnterWsprMode();
            else if (IsWsprModeName(previous))
                LeaveWsprMode();
            Raise();
            Raise(nameof(IsWsprMode));
            Raise(nameof(IsFtDigiMode));
            Raise(nameof(TxHzToolTip));
            CommandManager.InvalidateRequerySuggested();
            RebuildBandPresetList();
        }
    }

    static bool IsWsprModeName(string? mode) =>
        mode is not null &&
        (mode.Equals("WSPR2", StringComparison.OrdinalIgnoreCase) ||
         mode.Equals("WSPR", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Keep FT8/FT4 and WSPR audio markers separate so entering WSPR does not
    /// permanently overwrite digi Tx/Rx (and vice versa).
    /// </summary>
    void SwapAudioHzForModeSwitch(string previous, string next)
    {
        bool wasWspr = IsWsprModeName(previous);
        bool nowWspr = IsWsprModeName(next);
        if (wasWspr == nowWspr) return;

        RememberAudioHzForMode(wasWspr);
        ApplyAudioHzFromModeMemory(nowWspr);
        ScheduleSettingsPersist();
    }

    void InitializeAndRestoreAudioHzMemory()
    {
        if (!_settings.AudioHzMemoryInitialized)
        {
            // First run of this feature: seed both banks from whatever is live now.
            bool wspr = IsWsprModeName(_settings.Mode);
            RememberAudioHzForMode(wspr);
            if (wspr)
            {
                // Digi bank still default — keep last live digi if Tx was already digi-range.
                if (_settings.DigiTxHz is < 200 or > 3000) _settings.DigiTxHz = 1500;
                if (_settings.DigiRxHz is < 200 or > 3000) _settings.DigiRxHz = _settings.DigiTxHz;
            }
            else
            {
                if (_settings.WsprAudioTxHz is < 1400 or > 1600) _settings.WsprAudioTxHz = 1500;
                if (_settings.WsprAudioRxHz is < 1400 or > 1600) _settings.WsprAudioRxHz = _settings.WsprAudioTxHz;
            }
            _settings.AudioHzMemoryInitialized = true;
            _settingsStore.Save(_settings);
        }

        // Authoritative restore for the mode we are launching into.
        ApplyAudioHzFromModeMemory(IsWsprModeName(_settings.Mode));
    }

    void RememberAudioHzForMode(bool wspr)
    {
        if (wspr)
        {
            _settings.WsprAudioTxHz = ClampWsprAudioHz(TxHz);
            _settings.WsprAudioRxHz = ClampWsprAudioHz(RxHz);
        }
        else
        {
            _settings.DigiTxHz = ClampDigiAudioHz(TxHz);
            _settings.DigiRxHz = ClampDigiAudioHz(RxHz);
        }
        _settings.AudioHzMemoryInitialized = true;
    }

    void ApplyAudioHzFromModeMemory(bool wspr)
    {
        if (wspr)
        {
            int tx = ClampWsprAudioHz(_settings.WsprAudioTxHz);
            int rx = ClampWsprAudioHz(_settings.WsprAudioRxHz);
            SetAudioHzCore(tx, rx, remember: false);
        }
        else
        {
            int tx = ClampDigiAudioHz(_settings.DigiTxHz);
            int rx = ClampDigiAudioHz(_settings.DigiRxHz);
            SetAudioHzCore(tx, rx, remember: false);
        }
    }

    void SetAudioHzCore(int tx, int rx, bool remember)
    {
        _settings.TxHz = tx;
        _settings.RxHz = rx;
        if (remember)
            RememberAudioHzForMode(IsWsprMode);
        if (_waterfall is not null)
        {
            _waterfall.TxHz = tx;
            _waterfall.RxHz = rx;
        }
        Raise(nameof(TxHz));
        Raise(nameof(RxHz));
        InvalidateTxAudioCache();
        RebuildRxFilter();
        RefreshCurrentFreqText();
    }

    static int ClampDigiAudioHz(int hz) => hz is >= 200 and <= 3000 ? hz : 1500;
    static int ClampWsprAudioHz(int hz) => hz is >= 1400 and <= 1600 ? hz : 1500;

    void ScheduleSettingsPersist()
    {
        _settingsPersistTimer.Stop();
        _settingsPersistTimer.Start();
    }

    void EnterWsprMode()
    {
        // Beacon mode — no FT8 auto-seq / CQ hold.
        _holdCq = false;
        AutoSequence = false;
        SyncWsprPowerFromSettings();
        RefreshWsprFreeText();
        ScheduleNextWsprSlot(announce: false);
        Status = "WSPR-2 — Enable TX + Tx% (or Tx next). Spots → wsprnet.org when Upload spots is on.";
    }

    /// <summary>
    /// Drop WSPR beacon state when returning to FT8/FT4 so FreeText / cache / auto-seq
    /// are not still armed for "CALL GRID dBm".
    /// </summary>
    void LeaveWsprMode()
    {
        _wsprTxNextArmed = false;
        _wsprScheduleThisSlot = false;
        WsprScheduleText = "";
        _holdCq = false;
        _deferTxUntilDecode = false;
        // WSPR Enable TX must not carry into FT8 — otherwise the next even/odd slot
        // keys CQ automatically from the FreeText we load below.
        if (EnableTx)
            EnableTx = false;
        if (IsTransmitting || IsTuning)
            _ = HaltTxAsync();
        InvalidateTxAudioCache();
        AutoSequence = true;
        _seq.Reset();
        PhaseLabel = "Idle";
        // Replace leftover beacon text — Enable TX again when ready to call CQ.
        FreeText = BuildCqMessage();
        PrimeTxAudioCache(FreeText);
        RefreshMacroPreviews();
        Status = $"{Mode} mode — WSPR cleared; Enable TX when ready";
    }

    public string TxHzToolTip => IsWsprMode
        ? "WSPR audio Tx Hz (use 1400–1600; avoid crowded 1500)"
        : "FT8/FT4 Tx audio Hz";

    public int WsprTxPercent
    {
        get => _settings.WsprTxPercent;
        set
        {
            int v = Math.Clamp(value, 0, 100);
            if (_settings.WsprTxPercent == v) return;
            _settings.WsprTxPercent = v;
            Raise();
            if (IsWsprMode)
                ScheduleNextWsprSlot(announce: true);
        }
    }

    public bool WsprUploadSpots
    {
        get => _settings.WsprUploadSpots;
        set
        {
            if (_settings.WsprUploadSpots == value) return;
            _settings.WsprUploadSpots = value;
            Raise();
            _external.Configure(_settings);
        }
    }

    public string SelectedWsprPowerLabel
    {
        get => WsprEncoder.FormatPowerLabel(_settings.WsprPowerDbm);
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            // "37 dBm (5 W)" → 37
            int space = value.IndexOf(' ');
            if (space <= 0 || !int.TryParse(value[..space], out int dbm)) return;
            dbm = WsprEncoder.SnapPower(dbm);
            if (_settings.WsprPowerDbm == dbm) return;
            _settings.WsprPowerDbm = dbm;
            _settings.TxPower = FormatWatts(WsprEncoder.DbmToWatts(dbm));
            Raise();
            Raise(nameof(TxPower));
            if (IsWsprMode) RefreshWsprFreeText();
            _ = PushRfPowerAsync();
        }
    }

    public string WsprScheduleText
    {
        get => _wsprScheduleText;
        set => SetField(ref _wsprScheduleText, value ?? "");
    }

    public bool WsprTxNextArmed
    {
        get => _wsprTxNextArmed;
        private set
        {
            if (_wsprTxNextArmed == value) return;
            _wsprTxNextArmed = value;
            Raise();
            RefreshWsprScheduleText();
        }
    }

    void ArmWsprTxNext()
    {
        if (!IsWsprMode) return;
        RefreshWsprFreeText();
        EnableTx = true;
        WsprTxNextArmed = true;
        Status = $"WSPR Tx next armed → {FreeText} @ {TxHz} Hz (Tx%={WsprTxPercent})";
        // If we are still early enough in the current 2-min slot, key now.
        if (!IsTransmitting)
            TryStartTxNowIfOurSlot(WsprEncoder.SignalDurationSec + WsprEncoder.LeadInSec - 1.0);
    }

    void SyncWsprPowerFromSettings()
    {
        // Prefer explicit dBm; if watts changed elsewhere, snap dBm from watts.
        int fromWatts = WsprEncoder.WattsToDbm(ParseWatts(TxPower));
        if (_settings.WsprPowerDbm <= 0)
            _settings.WsprPowerDbm = fromWatts;
        _settings.WsprPowerDbm = WsprEncoder.SnapPower(_settings.WsprPowerDbm);
        Raise(nameof(SelectedWsprPowerLabel));
    }

    static string FormatWatts(double watts)
    {
        if (watts >= 1) return watts.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        if (watts >= 0.001) return (watts * 1000).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        return watts.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Random TX schedule like WSJT-X/JTDX: next slot TX with probability Tx%.</summary>
    void ScheduleNextWsprSlot(bool announce)
    {
        int pct = Math.Clamp(_settings.WsprTxPercent, 0, 100);
        if (pct <= 0)
            _wsprScheduleThisSlot = false;
        else if (pct >= 100)
            _wsprScheduleThisSlot = true;
        else
            _wsprScheduleThisSlot = Random.Shared.Next(100) < pct;

        RefreshWsprScheduleText();
        if (announce && IsWsprMode)
        {
            Status = _wsprScheduleThisSlot
                ? $"WSPR next slot: TX scheduled (Tx%={pct})"
                : $"WSPR next slot: RX only (Tx%={pct})";
        }
    }

    void RefreshWsprScheduleText()
    {
        if (!IsWsprMode)
        {
            WsprScheduleText = "";
            return;
        }

        var bits = new List<string>();
        if (_wsprTxNextArmed) bits.Add("Tx next");
        bits.Add(_wsprScheduleThisSlot ? "next=TX" : "next=RX");
        bits.Add($"Tx%{WsprTxPercent}");
        WsprScheduleText = string.Join(" · ", bits);
    }

    bool ShouldTransmitWsprThisSlot()
    {
        if (!EnableTx || IsTransmitting) return false;
        if (_wsprTxNextArmed) return true;
        if (WsprTxPercent <= 0) return false;
        if (WsprTxPercent >= 100) return true;
        return _wsprScheduleThisSlot;
    }

    void ApplyModeToWaterfall()
    {
        if (_waterfall is null) return;
        if (IsWsprMode)
        {
            // Classic WSPR USB audio passband — clean narrow view, soft traces.
            // Tx/Rx Hz are swapped by SwapAudioHzForModeSwitch — do not overwrite digi markers here.
            _waterfall.WsprDisplayMode = true;
            _waterfall.ChannelWidthHz = 5;
            _waterfall.MinHz = 1400;
            _waterfall.MaxHz = 1600;
            _waterfall.SnapHz = 1;
            _waterfall.SlotSeconds = 120;
            _waterfall.TxHz = TxHz;
            _waterfall.RxHz = RxHz;
            _waterfall.ResetDisplay();
        }
        else
        {
            _waterfall.WsprDisplayMode = false;
            _waterfall.ChannelWidthHz = Mode.Equals("FT4", StringComparison.OrdinalIgnoreCase) ? 90 : 50;
            _waterfall.MinHz = 200;
            _waterfall.MaxHz = 3000;
            _waterfall.SnapHz = 6.25;
            _waterfall.SlotSeconds = Mode.Equals("FT4", StringComparison.OrdinalIgnoreCase) ? 7 : 15;
            _waterfall.TxHz = TxHz;
            _waterfall.RxHz = RxHz;
            _waterfall.ResetDisplay();
        }
    }

    void RefreshWsprFreeText()
    {
        FreeText = BuildWsprMessage();
        InvalidateTxAudioCache();
        PrimeTxAudioCache(FreeText);
    }

    string BuildWsprMessage()
    {
        try
        {
            int dbm = WsprEncoder.SnapPower(_settings.WsprPowerDbm);
            if (dbm <= 0)
                dbm = WsprEncoder.WattsToDbm(ParseWatts(TxPower));
            string grid4 = (MyGrid ?? "FN42").Trim().ToUpperInvariant();
            if (grid4.Length > 4) grid4 = grid4[..4];
            return WsprEncoder.FormatMessage(MyCall, grid4, dbm);
        }
        catch (Exception ex)
        {
            return $"WSPR encode: {ex.Message}";
        }
    }

    static double ParseWatts(string? text)
    {
        if (double.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double w) && w > 0)
            return w;
        if (double.TryParse(text, out w) && w > 0) return w;
        return 5;
    }

    /// <summary>Parse "CALL GRID POWER" from FreeText / Band Activity.</summary>
    static bool TryParseWsprMessage(string message, out string call, out string grid4, out int dbm)
    {
        call = "";
        grid4 = "";
        dbm = 0;
        var parts = (message ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return false;
        if (!int.TryParse(parts[^1], out dbm)) return false;
        grid4 = parts[^2].Trim().ToUpperInvariant();
        if (grid4.Length != 4) return false;
        call = string.Join(" ", parts[..^2]).Trim().ToUpperInvariant();
        return call.Length >= 3;
    }

    static bool LooksLikeWsprFreeText(string? text) =>
        TryParseWsprMessage(text ?? "", out _, out _, out _);

    public double DialMhz
    {
        get => _settings.DialMhz;
        set
        {
            _settings.DialMhz = value;
            string band = BandPlan.BandFromMhz(value);
            bool bandChanged = !string.Equals(_settings.Band, band, StringComparison.OrdinalIgnoreCase);
            if (bandChanged)
            {
                _settings.Band = band;
                Raise(nameof(Band));
            }
            Raise();
            RefreshCurrentFreqText();
            // Only clear Band Activity / RX when hopping bands (e.g. 10m → 20m), not VFO tweaks.
            if (bandChanged)
                ClearActivityPanesForDialChange();
        }
    }

    public int TxHz
    {
        get => _settings.TxHz;
        set
        {
            if (_settings.TxHz == value) return;
            _settings.TxHz = value;
            if (IsWsprMode)
                _settings.WsprAudioTxHz = ClampWsprAudioHz(value);
            else
                _settings.DigiTxHz = ClampDigiAudioHz(value);
            _settings.AudioHzMemoryInitialized = true;
            Raise();
            if (_waterfall is not null) _waterfall.TxHz = value;
            InvalidateTxAudioCache();
            RebuildRxFilter();
            RefreshCurrentFreqText();
            ScheduleSettingsPersist();
        }
    }

    public int RxHz
    {
        get => _settings.RxHz;
        set
        {
            if (_settings.RxHz == value) return;
            _settings.RxHz = value;
            if (IsWsprMode)
                _settings.WsprAudioRxHz = ClampWsprAudioHz(value);
            else
                _settings.DigiRxHz = ClampDigiAudioHz(value);
            _settings.AudioHzMemoryInitialized = true;
            Raise();
            if (_waterfall is not null) _waterfall.RxHz = value;
            RebuildRxFilter();
            RefreshCurrentFreqText();
            ScheduleSettingsPersist();
        }
    }

    public string UtcClockText
    {
        get => _utcClockText;
        set => SetField(ref _utcClockText, value);
    }

    public string CurrentFreqText
    {
        get => _currentFreqText;
        set => SetField(ref _currentFreqText, value);
    }

    public bool EnableTx
    {
        get => _settings.EnableTx;
        set
        {
            _settings.EnableTx = value;
            Raise();
            Status = value ? "TX armed." : "TX disabled.";
        }
    }

    public bool SimulateDecodes
    {
        get => _settings.SimulateDecodes;
        set { _settings.SimulateDecodes = value; Raise(); Raise(nameof(DecoderStatusText)); }
    }

    public string SelectedDecoderEngine
    {
        get => _settings.DecoderEngine.ToString();
        set
        {
            if (Enum.TryParse(value, ignoreCase: true, out DecoderEngine eng))
            {
                _settings.DecoderEngine = eng;
                Raise();
                Raise(nameof(DecoderStatusText));
            }
        }
    }

    public string JtdxJt9Path
    {
        get => _settings.JtdxJt9Path;
        set { _settings.JtdxJt9Path = value; Raise(); Raise(nameof(DecoderStatusText)); }
    }

    public int JtdxDecodeDepth
    {
        get => _settings.JtdxDecodeDepth;
        set { _settings.JtdxDecodeDepth = Math.Clamp(value, 1, 3); Raise(); }
    }

    public string DecoderStatusText
    {
        get
        {
            if (SimulateDecodes || _settings.DecoderEngine == DecoderEngine.Simulate)
                return "Decoder: Simulate";
            if (_jtdx.IsAvailable(_settings.JtdxJt9Path))
                return "Decoder: JTDX";
            return "Decoder: JTDX (path missing — sim fallback)";
        }
    }

    public bool AutoSequence
    {
        get => _settings.AutoSequence;
        set { _settings.AutoSequence = value; Raise(); }
    }

    public bool CallAlertEnabled
    {
        get => _settings.CallAlertEnabled;
        set { _settings.CallAlertEnabled = value; Raise(); }
    }

    public string CallAlertSoundPath
    {
        get => _settings.CallAlertSoundPath;
        set { _settings.CallAlertSoundPath = value ?? ""; Raise(); }
    }

    public bool WantedAlertEnabled
    {
        get => _settings.WantedAlertEnabled;
        set { _settings.WantedAlertEnabled = value; Raise(); }
    }

    public bool WantedAlertCqOnly
    {
        get => _settings.WantedAlertCqOnly;
        set { _settings.WantedAlertCqOnly = value; Raise(); }
    }

    public string WantedAlertSoundPath
    {
        get => _settings.WantedAlertSoundPath;
        set { _settings.WantedAlertSoundPath = value ?? ""; Raise(); }
    }

    public string NewWantedValue
    {
        get => _newWantedValue;
        set
        {
            if (!SetField(ref _newWantedValue, value ?? "")) return;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public int SelectedWantedKindIndex
    {
        get => _selectedWantedKindIndex;
        set => SetField(ref _selectedWantedKindIndex, Math.Clamp(value, 0, 2));
    }

    public string WantedFilePath => _wanted.FilePath;

    void ReloadWantedList()
    {
        WantedItems.Clear();
        foreach (var e in _wanted.Snapshot())
            WantedItems.Add(e);
    }

    void AddWantedItem()
    {
        var kind = SelectedWantedKindIndex switch
        {
            1 => WantedKind.State,
            2 => WantedKind.Country,
            _ => WantedKind.Call
        };
        var entry = WantedEntry.TryCreate(kind, NewWantedValue);
        if (entry is null)
        {
            Status = "Enter a call, state (e.g. IL), or country name";
            return;
        }

        if (!_wanted.Add(entry))
        {
            Status = $"Already on Wanted list → {entry.Display}";
            return;
        }

        WantedItems.Add(entry);
        NewWantedValue = "";
        Status = $"Wanted + {entry.Display}";
        RefreshWantedFlagsInActivity();
    }

    void RemoveWantedItem(WantedEntry? entry)
    {
        if (entry is null) return;
        if (!_wanted.Remove(entry)) return;
        var match = WantedItems.FirstOrDefault(e =>
            e.Kind == entry.Kind &&
            string.Equals(e.Value, entry.Value, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
            WantedItems.Remove(match);
        Status = $"Wanted − {entry.Display}";
        RefreshWantedFlagsInActivity();
    }

    void RefreshWantedFlagsInActivity()
    {
        foreach (var hit in Activity)
            hit.IsWanted = _wanted.Match(hit) is not null;
        foreach (var hit in RxActivity)
            hit.IsWanted = _wanted.Match(hit) is not null;
    }

    bool TryWantedAlert(DecodeHit hit, out string reason)
    {
        reason = "";
        if (!WantedAlertEnabled) return false;
        if (WantedAlertCqOnly && !hit.IsCq) return false;
        string? match = _wanted.Match(hit);
        if (match is null) return false;

        // Dedup: same transmitter + match reason within 3 minutes.
        string key = $"{hit.DxCall}|{match}";
        var now = DateTime.UtcNow;
        if (_wantedAlertedUtc.TryGetValue(key, out var last) && (now - last).TotalMinutes < 3)
            return false;
        _wantedAlertedUtc[key] = now;

        // Prune old keys
        if (_wantedAlertedUtc.Count > 200)
        {
            foreach (var stale in _wantedAlertedUtc.Where(kv => (now - kv.Value).TotalMinutes > 10).Select(kv => kv.Key).ToList())
                _wantedAlertedUtc.Remove(stale);
        }

        reason = match;
        return true;
    }

    public bool TxEven
    {
        get => _settings.TxEven;
        set { _settings.TxEven = value; Raise(); }
    }

    public bool LockTxToRx
    {
        get => _settings.LockTxToRx;
        set
        {
            _settings.LockTxToRx = value;
            Raise();
            if (_waterfall is not null)
                _waterfall.LockTxToRx = value;
            if (value)
                TxHz = RxHz;
        }
    }

    public string SelectedWaterfallPalette
    {
        get => _settings.WaterfallPalette;
        set
        {
            _settings.WaterfallPalette = value;
            Raise();
            ApplyWaterfallPalette();
        }
    }

    public string TxPower
    {
        get => _settings.TxPower;
        set
        {
            _settings.TxPower = value;
            if (IsWsprMode)
            {
                _settings.WsprPowerDbm = WsprEncoder.WattsToDbm(ParseWatts(value));
                Raise(nameof(SelectedWsprPowerLabel));
            }
            Raise();
            _ = PushRfPowerAsync();
            if (IsWsprMode) RefreshWsprFreeText();
        }
    }

    /// <summary>TX audio drive 0–100 (Slider binds as double).</summary>
    public double TxDrivePercent
    {
        get => _settings.TxDrivePercent;
        set
        {
            int v = (int)Math.Round(Math.Clamp(value, 0, 100));
            if (_settings.TxDrivePercent == v) return;
            _settings.TxDrivePercent = v;
            Raise();
            Raise(nameof(TxDriveLabel));
            ApplyDriveLevel(v);
        }
    }

    public string TxDriveLabel => $"Drive {_settings.TxDrivePercent}%";

    void ApplyDriveLevel(int percent)
    {
        float gain = Math.Clamp(percent / 100f, 0f, 1f);
        _playback.Gain = gain;
        ApplyDriveToAlcMeter();
        // Also push MIC/USB/DATA level over CAT when the radio exposes it (HRD/Hamlib).
        if (_cat.IsConnected)
            _ = _cat.SetAudioDriveAsync(gain);
    }

    public bool CatRfPowerEnabled
    {
        get => _settings.CatRfPowerEnabled;
        set { _settings.CatRfPowerEnabled = value; Raise(); }
    }

    public double RadioMaxWatts
    {
        get => _settings.RadioMaxWatts;
        set
        {
            _settings.RadioMaxWatts = Math.Max(1, value);
            Raise();
            _ = PushRfPowerAsync();
        }
    }

    public string AlcText
    {
        get => _alcText;
        set => SetField(ref _alcText, value);
    }

    public string RfPowerText
    {
        get => _rfPowerText;
        set => SetField(ref _rfPowerText, value);
    }

    public double AlcLevel
    {
        get => _alcLevel;
        set => SetField(ref _alcLevel, value);
    }

    public string RxLevelText
    {
        get => _rxLevelText;
        set => SetField(ref _rxLevelText, value);
    }

    /// <summary>0–1 bar for RX audio (maps −54 dBFS → 0, 0 dBFS → 1).</summary>
    public double RxLevelNorm
    {
        get => _rxLevelNorm;
        set => SetField(ref _rxLevelNorm, value);
    }

    public string DxCall
    {
        get => _dxCall;
        set
        {
            string normalized = (value ?? "").Trim().ToUpperInvariant();
            if (!SetField(ref _dxCall, normalized)) return;
            RefreshMacroPreviews();
        }
    }

    public string DxGrid
    {
        get => _dxGrid;
        set
        {
            string normalized = (value ?? "").Trim().ToUpperInvariant();
            if (!SetField(ref _dxGrid, normalized)) return;
            RefreshMacroPreviews();
        }
    }

    public string Report
    {
        get => _report;
        set
        {
            if (!SetField(ref _report, value)) return;
            RefreshMacroPreviews();
        }
    }

    public string FreeText
    {
        get => _freeText;
        set
        {
            if (!SetField(ref _freeText, value)) return;
            RefreshMacroPreviews();
            InvalidateTxAudioCache();
            if (EnableTx && !string.IsNullOrWhiteSpace(value))
                PrimeTxAudioCache(value);
        }
    }

    public string NewCustomMacroLabel
    {
        get => _newCustomMacroLabel;
        set => SetField(ref _newCustomMacroLabel, value ?? "");
    }

    public string NewCustomMacroTemplate
    {
        get => _newCustomMacroTemplate;
        set => SetField(ref _newCustomMacroTemplate, value ?? "");
    }

    public string Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public string CatStatus
    {
        get => _catStatus;
        set => SetField(ref _catStatus, value);
    }

    public string RigFreqText
    {
        get => _rigFreqText;
        set => SetField(ref _rigFreqText, value);
    }

    public string RigModeText
    {
        get => _rigModeText;
        set => SetField(ref _rigModeText, value);
    }

    public string RigPttText
    {
        get => _rigPttText;
        set => SetField(ref _rigPttText, value);
    }

    public string RigInfoText
    {
        get => _rigInfoText;
        set => SetField(ref _rigInfoText, value);
    }

    public bool RigPtt
    {
        get => _rigPtt;
        set => SetField(ref _rigPtt, value);
    }

    public string SlotLabel
    {
        get => _slotLabel;
        set => SetField(ref _slotLabel, value);
    }

    public string PhaseLabel
    {
        get => _phaseLabel;
        set => SetField(ref _phaseLabel, value);
    }

    public double SlotProgress
    {
        get => _slotProgress;
        set => SetField(ref _slotProgress, value);
    }

    public bool IsTransmitting
    {
        get => _isTransmitting;
        private set
        {
            if (_isTransmitting == value) return;
            _isTransmitting = value;
            Raise();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>Continuous tone for SWR/ALC (max 60s); toggle off anytime.</summary>
    public bool IsTuning
    {
        get => _isTuning;
        private set
        {
            if (_isTuning == value) return;
            _isTuning = value;
            Raise();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>Band Activity selection only — must not share SelectedItem with the RX grid (WPF crash).</summary>
    public DecodeHit? SelectedActivityDecode
    {
        get => _selectedActivityDecode;
        set
        {
            if (!SetField(ref _selectedActivityDecode, value)) return;
            if (value is not null)
                ApplySelectedDecode(value);
        }
    }

    /// <summary>RX Frequency pane selection only.</summary>
    public DecodeHit? SelectedRxDecode
    {
        get => _selectedRxDecode;
        set
        {
            if (!SetField(ref _selectedRxDecode, value)) return;
            if (value is not null)
                ApplySelectedDecode(value);
        }
    }

    void RefreshCurrentFreqText()
    {
        double dial = DialMhz;
        double txRf = dial + TxHz / 1_000_000d;
        double rxRf = dial + RxHz / 1_000_000d;
        CurrentFreqText = TxHz == RxHz
            ? $"{txRf:0.000000} MHz"
            : $"TX {txRf:0.000000}  RX {rxRf:0.000000}";
    }

    void ApplySelectedDecode(DecodeHit value)
    {
        // Always load the transmitting station (caller), never the called party.
        var (txCall, txGrid, _) = Ft8MessageBuilder.Parse(value.Message);
        string? call = !string.IsNullOrWhiteSpace(txCall) ? txCall : value.DxCall;
        if (!string.IsNullOrWhiteSpace(call) &&
            !call.Equals(MyCall, StringComparison.OrdinalIgnoreCase))
            DxCall = call;

        string? grid = !string.IsNullOrWhiteSpace(txGrid) ? txGrid : value.DxGrid;
        if (!string.IsNullOrWhiteSpace(grid))
            DxGrid = grid!;

        RxHz = value.Hz;
        if (LockTxToRx)
            TxHz = value.Hz;
        Report = FormatSnr(value.SnrDb);
        _qsoStartUtc = DateTime.UtcNow;
        _qsoLogPrompted = false;
        // Single-click only loads call/Hz/SNR into the UI.
        // Never StartWorking here — that resets mid-QSO (e.g. after a CQ reply) back to the grid call.
        // Double-click / EnableTxForStation arms a new station; CQ answer advances the sequencer itself.
        if (QrzLookupOnSelect && !string.IsNullOrWhiteSpace(DxCall))
            _ = LookupQrzAsync(DxCall, openBrowser: false); // API enrich only — don't pop a browser on every select
    }

    string? ResolveQrzCall()
    {
        if (!string.IsNullOrWhiteSpace(SelectedRxDecode?.DxCall))
            return SelectedRxDecode.DxCall;
        if (!string.IsNullOrWhiteSpace(SelectedActivityDecode?.DxCall))
            return SelectedActivityDecode.DxCall;
        if (SelectedRxDecode is not null)
        {
            var (dx, _, _) = Ft8MessageBuilder.Parse(SelectedRxDecode.Message);
            if (!string.IsNullOrWhiteSpace(dx)) return dx;
        }
        if (SelectedActivityDecode is not null)
        {
            var (dx, _, _) = Ft8MessageBuilder.Parse(SelectedActivityDecode.Message);
            if (!string.IsNullOrWhiteSpace(dx)) return dx;
        }
        return string.IsNullOrWhiteSpace(DxCall) ? null : DxCall;
    }

    void LookupQrzInBrowser()
    {
        string? call = ResolveQrzCall();
        if (string.IsNullOrWhiteSpace(call))
        {
            Status = "Select a decode or set DX call for QRZ";
            return;
        }
        if (string.IsNullOrWhiteSpace(DxCall))
            DxCall = call;
        OpenQrzInBrowser(call);
        Status = $"QRZ → {call}";
        _ = LookupQrzAsync(call, openBrowser: false);
    }

    /// <summary>Double-click a Band/RX decode: load station, arm TX (and auto-seq reply).</summary>
    public void EnableTxForStation(DecodeHit? hit)
    {
        if (hit is null) return;
        ApplySelectedDecode(hit);
        if (string.IsNullOrWhiteSpace(DxCall))
        {
            Status = "No callsign on that decode — TX not armed.";
            return;
        }

        // Watch them on RX only — never move Tx Hz unless the operator (or Lock Tx=Rx) changes it.
        RxHz = hit.Hz;

        // Reply in the opposite 15s period from the decode we just heard.
        TxEven = !IsEvenPeriod(hit.Utc);

        // User explicitly chose a station — leave CQ hold / any leftover WSPR beacon.
        _holdCq = false;
        if (IsWsprMode)
        {
            Status = "WSPR-2 is beacon-only — switch to FT8/FT4 to call a station.";
            return;
        }

        // Stale WSPR FreeText after a mode switch must not block FT8 arming.
        bool staleWsprText = LooksLikeWsprFreeText(FreeText);

        // Arm next TX from the decode content (CQ→Tx1, grid→Tx2, report→Tx3, R-report→Tx4).
        // Never blindly StartWorking mid-QSO — that rewound to Tx1 after their signal report.
        // Double-click always loads the station macro unless FreeText is a deliberate FT8 edit
        // with AutoSequence off (and not leftover WSPR).
        if (string.IsNullOrWhiteSpace(FreeText) || AutoSequence || staleWsprText)
        {
            bool sameMidQso =
                !string.IsNullOrWhiteSpace(_seq.DxCall) &&
                _seq.DxCall.Equals(DxCall, StringComparison.OrdinalIgnoreCase) &&
                _seq.Phase is QsoPhase.CallingDx or QsoPhase.SentReport
                    or QsoPhase.SentRogerReport or QsoPhase.Sent73
                    or QsoPhase.SendingSeventyThree;

            if (sameMidQso)
            {
                // Already working them — advance from this decode if it is a new reply.
                _seq.TryAdvanceFromDecode(hit.Message, _settings);
            }
            else
            {
                _seq.ArmFromDecode(hit.Message, _settings, Report);
                if (string.IsNullOrWhiteSpace(_seq.DxCall))
                    _seq.StartWorking(DxCall, DxGrid, Report);
            }

            if (!string.IsNullOrWhiteSpace(_seq.DxCall))
                DxCall = _seq.DxCall;
            if (!string.IsNullOrWhiteSpace(_seq.DxGrid))
                DxGrid = _seq.DxGrid;
            if (_seq.Phase is QsoPhase.SentReport or QsoPhase.SentRogerReport)
            {
                Report = FormatSnr(hit.SnrDb);
                _seq.Report = Report;
            }

            PhaseLabel = _seq.Phase.ToString();
            FreeText = _seq.NextTxMessage(_settings) ?? FreeText;
        }

        EnableTx = true;
        InvalidateTxAudioCache();
        PrimeTxAudioCache(FreeText);
        RebuildRxFilter();

        // Correct odd/even period → key now even mid-sequence (unkeys at period end).
        if (TryStartTxNowIfOurSlot())
            return;

        string period = TxEven ? "even" : "odd";
        Status = $"TX armed → {DxCall} @ {TxHz} Hz ({period})  ({FreeText})";
    }

    /// <summary>
    /// Start transmitting immediately when the current FT8/FT4 period matches TxEven.
    /// Mid-sequence starts are allowed; PTT/audio stop at the period boundary.
    /// </summary>
    /// <param name="minRemainingSec">
    /// Skip only when less than this many seconds remain in the period (default 1s).
    /// </param>
    bool TryStartTxNowIfOurSlot(double? minRemainingSec = null)
    {
        if (!EnableTx || IsTransmitting) return false;
        if (IsWsprMode)
        {
            // WSPR needs nearly a full 110.6 s tone — only start near the slot edge.
            minRemainingSec ??= WsprEncoder.SignalDurationSec + WsprEncoder.LeadInSec - 2.0;
            if (!ShouldTransmitWsprThisSlot() && !_wsprTxNextArmed)
                return false;
        }
        else if (string.IsNullOrWhiteSpace(FreeText) && !_holdCq &&
            _seq.Phase is QsoPhase.Idle or QsoPhase.Complete)
            return false;

        _timing.Tick(DateTime.UtcNow);
        if (!IsWsprMode && TxEven != _timing.IsEvenSlot)
            return false;

        double remaining = _timing.SlotSeconds - _timing.SecondsIntoSlot;
        double minRemain = minRemainingSec ?? 1.0;
        if (remaining < minRemain)
            return false;

        string msg;
        if (IsWsprMode)
            msg = BuildWsprMessage();
        else if (_holdCq)
            msg = BuildCqMessage();
        else if (AutoSequence)
            msg = _seq.NextTxMessage(_settings) ?? FreeText;
        else
            msg = FreeText;

        if (string.IsNullOrWhiteSpace(msg)) return false;

        FreeText = msg;
        PrimeTxAudioCache(FreeText);
        if (IsWsprMode && _wsprTxNextArmed)
            WsprTxNextArmed = false;
        string period = TxEven ? "even" : "odd";
        Status = IsWsprMode
            ? $"WSPR TX now ({remaining:0.0}s left) → {FreeText}"
            : $"TX now ({period}, {remaining:0.0}s left) → {DxCall}: {FreeText}";
        _ = TransmitMessageAsync(FreeText);
        return true;
    }

    bool IsEvenPeriod(DateTime utc)
    {
        int slotSec = Math.Max(1, _timing.SlotSeconds);
        long slot = (long)(utc.ToUniversalTime() - DateTime.UnixEpoch).TotalSeconds / slotSec;
        return (slot & 1) == 0;
    }

    public int SelectedInputIndex =>
        Math.Max(0, Math.Min(_settings.InputDeviceIndex, Math.Max(0, InputDevices.Count - 1)));

    public int SelectedOutputIndex =>
        Math.Max(0, Math.Min(_settings.OutputDeviceIndex, Math.Max(0, OutputDevices.Count - 1)));

    /// <summary>Bound to the In combo — persist by friendly name so order changes don't lose the device.</summary>
    public string SelectedInputDevice
    {
        get => ResolveDisplayName(InputDevices, _settings.InputDeviceName, _settings.InputDeviceIndex);
        set
        {
            // ComboBox clears selection while ItemsSource refreshes — never wipe the saved name.
            if (string.IsNullOrWhiteSpace(value)) return;
            if (string.Equals(_settings.InputDeviceName, value, StringComparison.OrdinalIgnoreCase) &&
                _settings.InputDeviceIndex == IndexOfDevice(InputDevices, value))
                return;
            _settings.InputDeviceName = value;
            _settings.InputDeviceIndex = IndexOfDevice(InputDevices, value);
            SyncInputDeviceIdFromName(value);
            Raise();
            Raise(nameof(SelectedInputIndex));
            SaveSettingsNow();
        }
    }

    /// <summary>Bound to the Out combo — persist by friendly name.</summary>
    public string SelectedOutputDevice
    {
        get => ResolveDisplayName(OutputDevices, _settings.OutputDeviceName, _settings.OutputDeviceIndex);
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            if (string.Equals(_settings.OutputDeviceName, value, StringComparison.OrdinalIgnoreCase) &&
                _settings.OutputDeviceIndex == IndexOfDevice(OutputDevices, value))
                return;
            _settings.OutputDeviceName = value;
            _settings.OutputDeviceIndex = IndexOfDevice(OutputDevices, value);
            SyncOutputDeviceIdFromName(value);
            Raise();
            Raise(nameof(SelectedOutputIndex));
            SaveSettingsNow();
        }
    }

    void ReloadAudioDeviceLists(bool save)
    {
        var capture = AudioCaptureService.ListDevices();
        var render = AudioPlaybackService.ListDevices();

        ReplaceCollection(InputDevices, capture.Select(d => d.Name));
        ReplaceCollection(OutputDevices, render.Select(d => d.Name));

        int inIdx = ResolveDeviceIndex(InputDevices, _settings.InputDeviceName, _settings.InputDeviceIndex);
        if (inIdx >= 0 && inIdx < capture.Count)
        {
            _settings.InputDeviceIndex = inIdx;
            _settings.InputDeviceName = capture[inIdx].Name;
            if (!string.IsNullOrWhiteSpace(capture[inIdx].Id))
                _settings.InputDeviceId = capture[inIdx].Id;
        }

        int outIdx = ResolveDeviceIndex(OutputDevices, _settings.OutputDeviceName, _settings.OutputDeviceIndex);
        if (outIdx >= 0 && outIdx < render.Count)
        {
            _settings.OutputDeviceIndex = outIdx;
            _settings.OutputDeviceName = render[outIdx].Name;
            if (!string.IsNullOrWhiteSpace(render[outIdx].Id))
                _settings.OutputDeviceId = render[outIdx].Id;
        }

        if (save)
            SaveSettingsNow();
    }

    static void ReplaceCollection(ObservableCollection<string> target, IEnumerable<string> names)
    {
        var list = names.ToList();
        if (target.Count == list.Count)
        {
            bool same = true;
            for (int i = 0; i < list.Count; i++)
            {
                if (!string.Equals(target[i], list[i], StringComparison.Ordinal))
                {
                    same = false;
                    break;
                }
            }
            if (same) return;
        }

        target.Clear();
        foreach (var n in list)
            target.Add(n);
    }

    void SyncInputDeviceIdFromName(string name)
    {
        foreach (var d in AudioCaptureService.ListDevices())
        {
            if (d.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                _settings.InputDeviceId = d.Id;
                return;
            }
        }
    }

    void SyncOutputDeviceIdFromName(string name)
    {
        foreach (var d in AudioPlaybackService.ListDevices())
        {
            if (d.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                _settings.OutputDeviceId = d.Id;
                return;
            }
        }
    }

    static string ResolveDisplayName(IReadOnlyList<string> devices, string? preferredName, int savedIndex)
    {
        if (devices.Count == 0) return "";
        int idx = ResolveDeviceIndex(devices, preferredName, savedIndex);
        if (idx < 0 || idx >= devices.Count) return devices[0];
        return devices[idx];
    }

    static int IndexOfDevice(IReadOnlyList<string> devices, string name)
    {
        for (int i = 0; i < devices.Count; i++)
        {
            if (devices[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return devices.Count > 0 ? 0 : -1;
    }

    static int ResolveDeviceIndex(IReadOnlyList<string> devices, string? preferredName, int savedIndex)
    {
        if (devices.Count == 0) return -1;

        if (!string.IsNullOrWhiteSpace(preferredName))
        {
            for (int i = 0; i < devices.Count; i++)
            {
                if (devices[i].Equals(preferredName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            // Partial match — Windows sometimes appends suffixes after reboot.
            for (int i = 0; i < devices.Count; i++)
            {
                if (devices[i].Contains(preferredName, StringComparison.OrdinalIgnoreCase) ||
                    preferredName.Contains(devices[i], StringComparison.OrdinalIgnoreCase))
                    return i;
            }
        }

        if (savedIndex >= 0 && savedIndex < devices.Count)
            return savedIndex;
        return 0;
    }

    int _selectedBandPresetIndex = -1;
    List<BandPreset> _bandPresetItems = [];

    public int SelectedBandPresetIndex
    {
        get => _selectedBandPresetIndex;
        set
        {
            if (_selectedBandPresetIndex == value) return;
            _selectedBandPresetIndex = value;
            Raise();
            // Band/dial are applied only via Apply band (or ApplySelectedBand) — never from
            // ComboBox index changes during startup binding, which would wipe the saved band.
        }
    }

    // CAT bindings
    public string SelectedCatBackend
    {
        get => _settings.CatBackend.ToString();
        set
        {
            if (Enum.TryParse(value, out CatBackend b))
                _settings.CatBackend = b;
            Raise();
        }
    }

    public string CatHost
    {
        get => _settings.CatHost;
        set { _settings.CatHost = value; Raise(); }
    }

    public int CatPort
    {
        get => _settings.CatPort;
        set { _settings.CatPort = value; Raise(); }
    }

    public string CatComPort
    {
        get => _settings.CatComPort;
        set
        {
            _settings.CatComPort = HamlibRigctldLauncher.ExtractPort(value ?? "");
            Raise();
        }
    }

    public int CatBaud
    {
        get => _settings.CatBaud;
        set { _settings.CatBaud = value; Raise(); }
    }

    public bool CatPttEnabled
    {
        get => _settings.CatPttEnabled;
        set { _settings.CatPttEnabled = value; Raise(); }
    }

    public bool CatFollowDial
    {
        get => _settings.CatFollowDial;
        set { _settings.CatFollowDial = value; Raise(); }
    }

    public int SelectedHamlibModelIndex
    {
        get => _selectedHamlibModelIndex;
        set
        {
            _selectedHamlibModelIndex = value;
            if (value >= 0 && value < _hamlibModels.Count)
            {
                _settings.HamlibModelId = _hamlibModels[value].Id;
                if (_settings.HamlibModelId == HrdCatController.ModelId)
                {
                    _settings.CatBackend = CatBackend.HamRadioDeluxe;
                    if (string.IsNullOrWhiteSpace(_settings.CatHost) || _settings.CatHost == "127.0.0.1")
                        _settings.CatHost = "localhost";
                    Raise(nameof(SelectedCatBackend));
                    Raise(nameof(CatHost));
                }
            }
            Raise();
            Raise(nameof(HamlibModelId));
            Raise(nameof(SelectedHamlibModelLabel));
            Raise(nameof(IsHrdSelected));
        }
    }

    public string SelectedHamlibModelLabel =>
        _selectedHamlibModelIndex >= 0 && _selectedHamlibModelIndex < _hamlibModels.Count
            ? _hamlibModels[_selectedHamlibModelIndex].DisplayName
            : $"#{HamlibModelId}";

    public string HamlibModelFilter
    {
        get => _hamlibModelFilter;
        set
        {
            if (!SetField(ref _hamlibModelFilter, value ?? "")) return;
            ApplyHamlibModelFilter();
        }
    }

    public string HamlibModelCountText
    {
        get => _hamlibModelCountText;
        set => SetField(ref _hamlibModelCountText, value);
    }

    public int HamlibModelId
    {
        get => _settings.HamlibModelId;
        set
        {
            _settings.HamlibModelId = value;
            Raise();
            SelectModelById(value);
        }
    }

    public string HamlibDevice
    {
        get => _settings.HamlibDevice;
        set
        {
            // Persist bare COMx / host, keep friendly labels only in the UI list.
            string port = HamlibRigctldLauncher.ExtractPort(value ?? "");
            _settings.HamlibDevice = port;
            _settings.CatComPort = port;
            Raise();
            Raise(nameof(CatComPort));
            Raise(nameof(HamlibDeviceDisplay));
        }
    }

    public string HamlibDeviceDisplay
    {
        get
        {
            string port = HamlibRigctldLauncher.ExtractPort(_settings.HamlibDevice);
            var match = ComPorts.FirstOrDefault(p =>
                HamlibRigctldLauncher.ExtractPort(p).Equals(port, StringComparison.OrdinalIgnoreCase));
            return match ?? port;
        }
        set => HamlibDevice = value;
    }

    public string HamlibRigctldPath
    {
        get => _settings.HamlibRigctldPath;
        set { _settings.HamlibRigctldPath = value; Raise(); }
    }

    public string HamlibMode
    {
        get => _settings.HamlibMode;
        set
        {
            string m = (value ?? "").Trim();
            // Prefer digi labels; plain USB/LSB break Yaesu/Icom USB-audio digi paths.
            if (!LooksDigiMode(m))
                m = "DATA-U";
            _settings.HamlibMode = m;
            Raise();
        }
    }

    static bool LooksDigiMode(string? m)
    {
        if (string.IsNullOrWhiteSpace(m)) return false;
        return m.Contains("DATA", StringComparison.OrdinalIgnoreCase) ||
               m.Contains("PKT", StringComparison.OrdinalIgnoreCase) ||
               m.Contains("DIG", StringComparison.OrdinalIgnoreCase) ||
               m.Contains("USB-D", StringComparison.OrdinalIgnoreCase) ||
               m.Contains("USB_D", StringComparison.OrdinalIgnoreCase);
    }

    public bool HamlibAutoStart
    {
        get => _settings.HamlibAutoStart;
        set { _settings.HamlibAutoStart = value; Raise(); }
    }

    public int HrdPort
    {
        get => _settings.HrdPort;
        set { _settings.HrdPort = value; Raise(); }
    }

    public bool IsHrdSelected =>
        HamlibModelId == HrdCatController.ModelId
        || SelectedCatBackend == nameof(CatBackend.HamRadioDeluxe);

    // Logging bindings
    public bool UdpLogEnabled
    {
        get => _settings.UdpLogEnabled;
        set { _settings.UdpLogEnabled = value; Raise(); }
    }

    public string UdpLogHost
    {
        get => _settings.UdpLogHost;
        set { _settings.UdpLogHost = value; Raise(); }
    }

    public int UdpLogPort
    {
        get => _settings.UdpLogPort;
        set { _settings.UdpLogPort = value; Raise(); }
    }

    public bool AdifAppendEnabled
    {
        get => _settings.AdifAppendEnabled;
        set { _settings.AdifAppendEnabled = value; Raise(); }
    }

    public string AdifPath
    {
        get => _settings.AdifPath;
        set { _settings.AdifPath = value; Raise(); }
    }

    public bool ClubLogEnabled
    {
        get => _settings.ClubLogEnabled;
        set { _settings.ClubLogEnabled = value; Raise(); }
    }

    public string ClubLogEmail
    {
        get => _settings.ClubLogEmail;
        set { _settings.ClubLogEmail = value; Raise(); }
    }

    public string ClubLogPassword
    {
        get => _settings.ClubLogPassword;
        set { _settings.ClubLogPassword = value; Raise(); }
    }

    public bool QrzEnabled
    {
        get => _settings.QrzEnabled;
        set { _settings.QrzEnabled = value; Raise(); }
    }

    public string QrzApiKey
    {
        get => _settings.QrzApiKey;
        set { _settings.QrzApiKey = value; Raise(); }
    }

    public string QrzUsername
    {
        get => _settings.QrzUsername;
        set
        {
            _settings.QrzUsername = value;
            _qrzLookup.ClearSession();
            Raise();
        }
    }

    public string QrzPassword
    {
        get => _settings.QrzPassword;
        set
        {
            _settings.QrzPassword = value;
            _qrzLookup.ClearSession();
            Raise();
        }
    }

    public bool QrzLookupOnSelect
    {
        get => _settings.QrzLookupOnSelect;
        set { _settings.QrzLookupOnSelect = value; Raise(); }
    }

    public bool QrzFillGrid
    {
        get => _settings.QrzFillGrid;
        set { _settings.QrzFillGrid = value; Raise(); }
    }

    public bool PskReporterEnabled
    {
        get => _settings.PskReporterEnabled;
        set
        {
            _settings.PskReporterEnabled = value;
            Raise();
            _external.Configure(_settings);
        }
    }

    public string QrzInfoText
    {
        get => _qrzInfoText;
        set => SetField(ref _qrzInfoText, value);
    }

    public void AttachWaterfall(WaterfallControl waterfall)
    {
        if (_waterfall is not null)
            _waterfall.FrequencyPicked -= OnWaterfallFrequencyPicked;

        _waterfall = waterfall;
        _waterfall.TxHz = TxHz;
        _waterfall.RxHz = RxHz;
        _waterfall.LockTxToRx = LockTxToRx;
        _waterfall.SampleRate = AudioCaptureService.SampleRate;
        _waterfall.FftSize = _spectrum.FftSize;
        ApplyModeToWaterfall();
        ApplyWaterfallPalette();
        _waterfall.FrequencyPicked += OnWaterfallFrequencyPicked;
    }

    void ApplyWaterfallPalette()
    {
        if (_waterfall is null) return;
        _waterfall.PaletteKind = WaterfallPalette.Parse(SelectedWaterfallPalette);
    }

    void OnWaterfallFrequencyPicked(object? sender, FrequencyPickEventArgs e)
    {
        int hz = e.Hz;

        if (e.SnapToDecode)
        {
            var near = Activity
                .OrderBy(a => Math.Abs(a.Hz - hz))
                .FirstOrDefault(a => Math.Abs(a.Hz - hz) <= 40);
            if (near is not null)
            {
                hz = near.Hz;
                // Selecting a call on the waterfall loads it like clicking Band Activity.
                if (e.SetRx)
                    SelectedActivityDecode = near;
            }
        }

        if (e.SetRx)
            RxHz = hz;
        if (e.SetTx || LockTxToRx)
            TxHz = hz;

        if (_waterfall is not null)
        {
            _waterfall.RxHz = RxHz;
            _waterfall.TxHz = TxHz;
        }

        Status = LockTxToRx || (e.SetRx && e.SetTx)
            ? $"Waterfall select {hz} Hz (Rx+Tx)"
            : e.SetTx
                ? $"Waterfall TX {hz} Hz"
                : $"Waterfall RX {hz} Hz";
    }

    void OpenSettings()
    {
        var owner = Application.Current.MainWindow;
        var win = new SettingsWindow(this)
        {
            Owner = owner
        };
        win.ShowDialog();
    }

    void RefreshPorts()
    {
        ComPorts.Clear();
        foreach (var d in CatDeviceEnumerator.ListDevices())
            ComPorts.Add(d.DisplayName);
        Raise(nameof(HamlibDeviceDisplay));
        Status = $"Devices: {ComPorts.Count} port(s) found";
    }

    void ReloadHamlibModels(bool forceRefresh)
    {
        var models = HamlibCatalog.GetModels(_settings.HamlibRigctldPath, forceRefresh);
        ApplyHamlibModelFilter(models);
        if (string.IsNullOrWhiteSpace(_settings.HamlibRigctldPath))
        {
            string? found = HamlibPaths.FindRigctld();
            if (found is not null)
                _settings.HamlibRigctldPath = found;
        }
        Status = forceRefresh
            ? $"Loaded {models.Count} Hamlib models from rigctl/catalog"
            : $"Hamlib models ready ({models.Count})";
    }

    void ApplyHamlibModelFilter(IReadOnlyList<HamlibModel>? source = null)
    {
        source ??= HamlibCatalog.GetModels(_settings.HamlibRigctldPath);
        _hamlibModels = string.IsNullOrWhiteSpace(_hamlibModelFilter)
            ? source.ToList()
            : HamlibCatalog.Filter(_hamlibModelFilter, _settings.HamlibRigctldPath).ToList();

        HamlibModelChoices.Clear();
        foreach (var m in _hamlibModels)
            HamlibModelChoices.Add(m.DisplayName);

        HamlibModelCountText = string.IsNullOrWhiteSpace(_hamlibModelFilter)
            ? $"{_hamlibModels.Count} models"
            : $"{_hamlibModels.Count} match / {source.Count} total";

        SelectModelById(_settings.HamlibModelId);
    }

    void SelectModelById(int id)
    {
        int idx = _hamlibModels.FindIndex(m => m.Id == id);
        if (idx < 0 && _hamlibModels.Count > 0)
            idx = 0;
        _selectedHamlibModelIndex = idx;
        if (idx >= 0)
            _settings.HamlibModelId = _hamlibModels[idx].Id;
        Raise(nameof(SelectedHamlibModelIndex));
        Raise(nameof(SelectedHamlibModelLabel));
        Raise(nameof(HamlibModelId));
    }

    void StartMonitor()
    {
        try
        {
            string preferName = _settings.InputDeviceName;
            string preferId = _settings.InputDeviceId;
            int preferIdx = _settings.InputDeviceIndex;

            // Refresh names without blanking the combo when nothing changed.
            ReloadAudioDeviceLists(save: false);

            var devices = AudioCaptureService.ListDevices();
            if (devices.Count == 0)
            {
                Status = "No capture device — spectrum idle / sim only.";
                RestoreAudioDeviceSelectionUi();
                return;
            }

            int idx = ResolveDeviceIndex(InputDevices, preferName, preferIdx);
            if (!string.IsNullOrWhiteSpace(preferId))
            {
                for (int i = 0; i < devices.Count; i++)
                {
                    if (devices[i].Id.Equals(preferId, StringComparison.OrdinalIgnoreCase))
                    {
                        idx = i;
                        break;
                    }
                }
            }

            idx = Math.Clamp(idx, 0, devices.Count - 1);
            _settings.InputDeviceIndex = idx;
            _settings.InputDeviceName = devices[idx].Name;
            _settings.InputDeviceId = devices[idx].Id;
            SaveSettingsNow();

            _capture.Start(idx, preferredName: _settings.InputDeviceName, preferredId: _settings.InputDeviceId);
            RestoreAudioDeviceSelectionUi();
            Status = $"Monitoring: {_settings.InputDeviceName}";
            AppendDecodeLog($"Monitor start idx={idx} device={_settings.InputDeviceName} id={_settings.InputDeviceId}");
        }
        catch (Exception ex)
        {
            Status = $"Monitor failed: {ex.Message}";
            AppendDecodeLog($"Monitor failed: {ex}");
            RestoreAudioDeviceSelectionUi();
        }
    }

    void StopMonitor()
    {
        _capture.Stop();
        Status = "Monitor stopped.";
    }

    async Task ConnectCatAsync()
    {
        if (_settings.HamlibModelId == HrdCatController.ModelId)
            _settings.CatBackend = CatBackend.HamRadioDeluxe;

        bool isHrd = _settings.CatBackend == CatBackend.HamRadioDeluxe
                     || _settings.HamlibModelId == HrdCatController.ModelId;

        if (!isHrd && _settings.CatBackend == CatBackend.HamlibTcp && HamlibAutoStart && !_cat.IsRigctldManaged)
            StartRigctld();

        await _cat.ApplySettingsAsync(_settings);
        CatStatus = _cat.IsConnected
            ? (isHrd ? $"HRD OK ({_cat.BackendName})" : $"Hamlib OK ({_cat.BackendName})")
            : CatStatus;
        Raise(nameof(SelectedCatBackend));
        Raise(nameof(IsHrdSelected));
        if (_cat.IsConnected)
        {
            await PushRfPowerAsync();
            // Always land in DATA-U / digi — band hops and many radios default to plain USB.
            string digi = await EnsureDigiModeAsync();
            // Read VFO a couple of times — HRD/rigctld can return empty on the first poll.
            long? hz = null;
            for (int i = 0; i < 3 && hz is null; i++)
            {
                await _cat.RefreshAsync();
                hz = _cat.LastSnapshot.FrequencyHz;
                if (hz is null)
                    await Task.Delay(200);
            }
            if (hz is long freq && freq > 0)
                ApplyDialFromRadio(freq);
            else
                Status = $"CAT connected ({digi}) — could not read frequency; set band manually or Refresh";
        }
    }

    async Task DisconnectCatAsync()
    {
        await _cat.StopAsync(stopLauncher: false);
        CatStatus = "CAT off";
        RigFreqText = "—";
        RigModeText = "—";
        RigPttText = "RX";
        RigInfoText = "";
        AlcText = "ALC —";
        RfPowerText = "RF —";
        AlcLevel = 0;
    }

    void StartRigctld()
    {
        SelectedCatBackend = nameof(CatBackend.HamlibTcp);
        var (ok, msg) = _cat.StartRigctld(_settings);
        Status = msg;
        if (!ok) CatStatus = msg;
    }

    void FindRigctld()
    {
        string? path = HamlibPaths.FindRigctld(_settings.HamlibRigctldPath);
        if (path is null)
        {
            Status = "rigctld not found. Install Hamlib (hamlib-w32) or browse to rigctld.exe.";
            return;
        }
        HamlibRigctldPath = path;
        ReloadHamlibModels(forceRefresh: true);
        Status = $"Found Hamlib: {path} ({HamlibCatalog.GetModels(path).Count} models)";
    }

    async Task SetRigFreqAsync()
    {
        try
        {
            await _cat.SetDialMhzAsync(DialMhz);
            // Frequency changes often flip Yaesu/Icom out of DATA-U — put digi back.
            string digi = await EnsureDigiModeAsync();
            Status = $"Set rig {DialMhz:0.000000} MHz ({digi})";
        }
        catch (Exception ex)
        {
            Status = $"Set freq failed: {ex.Message}";
        }
    }

    async Task SetRigModeAsync()
    {
        try
        {
            string digi = await EnsureDigiModeAsync();
            Status = $"CAT mode {digi}";
            await _cat.RefreshAsync();
        }
        catch (Exception ex)
        {
            Status = $"Set mode failed: {ex.Message}";
        }
    }

    async Task TogglePttAsync()
    {
        bool next = !RigPtt;
        await _cat.SetPttAsync(next, enabled: true);
        RigPtt = next;
        RigPttText = next ? "TX" : "RX";
    }

    async Task RefreshRigFromRadioAsync()
    {
        await _cat.RefreshAsync();
        ApplyDialFromRadio(_cat.LastSnapshot.FrequencyHz);
    }

    long _lastAppliedDialHz;

    /// <summary>Clear Band Activity and RX Frequency when dial/band frequency changes.</summary>
    void ClearActivityPanesForDialChange()
    {
        SelectedActivityDecode = null;
        SelectedRxDecode = null;
        Activity.Clear();
        RxActivity.Clear();
        _rxHistorySuppressed = false;
        RxHz = TxHz;
    }

    /// <summary>Set operator DialMhz/Band from a CAT frequency (Hz).</summary>
    void ApplyDialFromRadio(long? frequencyHz)
    {
        if (frequencyHz is not long hz || hz <= 0) return;
        double mhz = hz / 1_000_000d;
        if (mhz is < 0.1 or > 2000)
        {
            Status = $"Ignored bogus CAT frequency {hz} Hz";
            return;
        }

        RigFreqText = $"{mhz:0.000000} MHz";

        // Ignore tiny dither so we don't rewrite settings every poll.
        if (Math.Abs(hz - _lastAppliedDialHz) < 50)
            return;
        _lastAppliedDialHz = hz;

        DialMhz = mhz; // also updates Band via BandPlan
        RebuildBandPresetList();
        SaveSettingsNow();
        Status = $"Band from radio: {Band} @ {mhz:0.0000} MHz ({hz} Hz)";
    }

    void ApplySnapshot(RigSnapshot snap)
    {
        if (snap.FrequencyHz is long hz && !RigPtt)
            ApplyDialFromRadio(hz);
        else if (snap.FrequencyHz is long hzDisp)
            RigFreqText = $"{hzDisp / 1_000_000d:0.000000} MHz";
        if (!string.IsNullOrWhiteSpace(snap.Mode))
        {
            RigModeText = snap.PassbandHz is > 0 ? $"{snap.Mode} {snap.PassbandHz}Hz" : snap.Mode;
            if (LooksDigiMode(snap.Mode))
            {
                // Remember the digi label the radio actually accepted (PKTUSB vs DATA-U, etc.).
                if (HamlibModes.Contains(snap.Mode) || !string.IsNullOrWhiteSpace(snap.Mode))
                    HamlibMode = snap.Mode;
            }
            else if (_cat.IsConnected && !_forcingDigiMode)
            {
                // Band/VFO changes commonly drop to plain USB — force digi back.
                _ = RestoreDigiModeAsync(snap.Mode);
            }
        }
        if (snap.Ptt is bool ptt)
        {
            RigPtt = ptt;
            RigPttText = ptt ? "TX" : "RX";
        }
        if (snap.Alc is double alc)
        {
            AlcLevel = Math.Clamp(alc, 0, 1);
            AlcText = $"ALC {AlcLevel * 100:0}%";
        }
        else
        {
            // No CAT ALC — keep the Drive slider reflected on the meter.
            ApplyDriveToAlcMeter();
        }

        if (snap.RfPower is double rf)
            RfPowerText = $"RF {rf * 100:0}%";
        if (!string.IsNullOrWhiteSpace(snap.RigInfo))
            RigInfoText = snap.RigInfo!;
        else if (!string.IsNullOrWhiteSpace(snap.Vfo))
            RigInfoText = $"VFO {snap.Vfo}";
    }

    void ApplyDriveToAlcMeter()
    {
        double drive = Math.Clamp(_settings.TxDrivePercent / 100.0, 0, 1);
        AlcLevel = drive;
        AlcText = $"DRV {_settings.TxDrivePercent}%";
        Raise(nameof(AlcLevel));
        Raise(nameof(AlcText));
    }

    async Task PushRfPowerAsync()
    {
        if (!CatRfPowerEnabled || !_cat.IsConnected) return;
        if (!double.TryParse(TxPower, out double watts) || watts <= 0) return;
        double max = Math.Max(1, RadioMaxWatts);
        double level = Math.Clamp(watts / max, 0, 1);
        try
        {
            await _cat.SetRfPowerAsync(level);
        }
        catch (Exception ex)
        {
            Status = $"RF power set failed: {ex.Message}";
        }
    }

    void RebuildBandPresetList()
    {
        // Show current mode first (primaries then secondaries), then the other mode.
        string mode = Mode;
        _bandPresetItems = BandPlan.ForMode(mode)
            .OrderByDescending(p => p.Primary)
            .ThenBy(p => p.DialMhz)
            .Concat(BandPlan.Presets.Where(p => !p.Mode.Equals(mode, StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p.Mode).ThenByDescending(p => p.Primary).ThenBy(p => p.DialMhz))
            .ToList();

        BandPresets.Clear();
        foreach (var p in _bandPresetItems)
            BandPresets.Add(BandPlan.FormatPreset(p));

        int match = _bandPresetItems.FindIndex(p =>
            p.Mode.Equals(mode, StringComparison.OrdinalIgnoreCase) &&
            Math.Abs(p.DialMhz - DialMhz) < 0.00005);
        if (match < 0)
            match = _bandPresetItems.FindIndex(p =>
                p.Band.Equals(Band, StringComparison.OrdinalIgnoreCase) &&
                p.Mode.Equals(mode, StringComparison.OrdinalIgnoreCase));
        if (match < 0)
            match = _bandPresetItems.FindIndex(p =>
                p.Primary && p.Mode.Equals(mode, StringComparison.OrdinalIgnoreCase) &&
                p.Band.Equals(Band, StringComparison.OrdinalIgnoreCase));

        _selectedBandPresetIndex = match >= 0 ? match : -1;
        Raise(nameof(SelectedBandPresetIndex));
    }

    void ApplySelectedBand()
    {
        if (_selectedBandPresetIndex < 0 || _selectedBandPresetIndex >= _bandPresetItems.Count)
            return;
        var p = _bandPresetItems[_selectedBandPresetIndex];
        // Set fields without recursive RebuildBandPresetList from Mode setter mid-apply.
        string previousMode = _settings.Mode;
        if (!string.Equals(previousMode, p.Mode, StringComparison.OrdinalIgnoreCase))
            SwapAudioHzForModeSwitch(previousMode, p.Mode);
        _settings.Mode = p.Mode;
        _timing.SetMode(p.Mode);
        ApplyModeToWaterfall();
        if (IsWsprMode)
            EnterWsprMode();
        else if (IsWsprModeName(previousMode))
            LeaveWsprMode();
        Raise(nameof(Mode));
        Raise(nameof(IsWsprMode));
        Raise(nameof(IsFtDigiMode));
        Raise(nameof(TxHzToolTip));
        CommandManager.InvalidateRequerySuggested();
        Band = p.Band;
        DialMhz = p.DialMhz;
        if (_cat.IsConnected)
            _ = PushBandToRadioAsync(p);
        else
            Status = $"Band {p.Band} {p.Mode} @ {p.DialMhz:0.0000} MHz (CAT not connected)";
    }

    async Task PushBandToRadioAsync(BandPreset p)
    {
        try
        {
            await _cat.SetDialMhzAsync(p.DialMhz);
            // Always re-assert digi after dial change — never leave the radio in plain USB.
            string digi = await EnsureDigiModeAsync();
            await _cat.RefreshAsync();
            Status = $"Band {p.Band} {p.Mode} → radio {p.DialMhz:0.0000} MHz ({digi})";
        }
        catch (Exception ex)
        {
            Status = $"Band {p.Band} set locally; radio failed: {ex.Message}";
        }
    }

    async Task RestoreDigiModeAsync(string? fromMode)
    {
        if ((DateTime.UtcNow - _lastDigiModeForceUtc).TotalSeconds < 2.5)
            return;
        _lastDigiModeForceUtc = DateTime.UtcNow;
        _forcingDigiMode = true;
        try
        {
            string digi = await EnsureDigiModeAsync();
            if (!string.Equals(fromMode, digi, StringComparison.OrdinalIgnoreCase))
                Status = $"Radio was {fromMode} — restored {digi}";
        }
        catch (Exception ex)
        {
            Status = $"Could not restore DATA-U: {ex.Message}";
        }
        finally
        {
            _forcingDigiMode = false;
        }
    }

    void StartCq()
    {
        if (IsWsprMode)
        {
            ArmWsprTxNext();
            return;
        }

        ArmCqHold();
        // Arm for the next matching period — do not key mid-slot (late TX is hard to complete QSOs with).
        Status = $"CQ hold ON ({(TxEven ? "even" : "odd")}) → {FreeText}";
    }

    /// <summary>
    /// Enter CQ-hold: clear any prior DX/QSO so the next TX slot keeps calling CQ
    /// (not the previous station left in the sequencer).
    /// </summary>
    void ArmCqHold()
    {
        SelectedActivityDecode = null;
        SelectedRxDecode = null;
        _holdCq = true;
        _cqSessionUtc = DateTime.UtcNow;
        _qsoLogPrompted = false;
        _deferTxUntilDecode = false;
        _seq.StartCq();
        PhaseLabel = _seq.Phase.ToString();
        DxCall = "";
        DxGrid = "";
        FreeText = BuildCqMessage();
        EnableTx = true;
        InvalidateTxAudioCache();
        PrimeTxAudioCache(FreeText);
    }

    void OnClock(object? sender, EventArgs e)
    {
        _timing.Tick(DateTime.UtcNow);
        UtcClockText = _timing.UtcNow.ToString("HH:mm:ss");
        SlotProgress = _timing.Progress;
        SlotLabel =
            $"{_timing.Mode} {_timing.SecondsIntoSlot:00.0}/{_timing.SlotSeconds}s  {(_timing.IsEvenSlot ? "even" : "odd")}";
        RefreshCurrentFreqText();

        if (_timing.JustRolled)
        {
            bool ourSlot;
            if (IsWsprMode)
            {
                // Every 120 s slot is a candidate; Tx% / Tx next decide (not FT8 even/odd).
                ourSlot = ShouldTransmitWsprThisSlot();
            }
            else
            {
                ourSlot = EnableTx && TxEven == _timing.IsEvenSlot && !IsTransmitting;
            }

            // Auto-seq: decode first — a reply in the prior period must advance the macro
            // before we key, otherwise we re-send CQ/Tx1 and wait another cycle.
            // WSPR is beacon-only — no decode-before-TX deferral.
            if (ourSlot && AutoSequence && !IsWsprMode)
                _deferTxUntilDecode = true;

            TriggerSlotDecode();

            if (_deferTxUntilDecode)
                return; // FinishDeferredTxAfterDecode() keys after decode

            if (ourSlot)
                TransmitArmedMessage();

            // After deciding this slot, roll the random schedule for the following one.
            if (IsWsprMode)
                ScheduleNextWsprSlot(announce: false);
        }
    }

    /// <summary>TX whatever is currently armed (CQ hold / seq / FreeText / WSPR beacon).</summary>
    void TransmitArmedMessage()
    {
        if (!EnableTx || IsTransmitting) return;

        if (IsWsprMode)
        {
            if (!ShouldTransmitWsprThisSlot()) return;
            string beacon = BuildWsprMessage();
            FreeText = beacon;
            PhaseLabel = "WsprBeacon";
            // Consume one-shot Tx next before keying.
            if (_wsprTxNextArmed)
                WsprTxNextArmed = false;
            _ = TransmitMessageAsync(beacon);
            return;
        }

        if (TxEven != _timing.IsEvenSlot) return;

        if (_holdCq)
        {
            string cq = BuildCqMessage();
            FreeText = cq;
            PhaseLabel = "CallingCq";
            _ = TransmitMessageAsync(cq);
            return;
        }

        if (AutoSequence)
        {
            string? next = _seq.NextTxMessage(_settings);
            if (!string.IsNullOrWhiteSpace(next) && _seq.Phase is not QsoPhase.Idle and not QsoPhase.Complete)
            {
                FreeText = next;
                _ = TransmitMessageAsync(next);
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(FreeText))
            _ = TransmitMessageAsync(FreeText);
    }

    /// <summary>
    /// After decode: if we deferred TX for this slot, key now with the (possibly advanced) macro.
    /// Allows a later start than double-click because decode ate time at the beginning of the period.
    /// </summary>
    void FinishDeferredTxAfterDecode()
    {
        if (!_deferTxUntilDecode) return;
        _deferTxUntilDecode = false;

        if (!EnableTx) return;
        // Already keyed (e.g. double-click) — leave status alone.
        if (IsTransmitting) return;

        _timing.Tick(DateTime.UtcNow);
        if (TxEven != _timing.IsEvenSlot) return;

        // Decode may finish mid-period — key now if this is still our sequence.
        if (TryStartTxNowIfOurSlot())
            return;

        string period = TxEven ? "even" : "odd";
        Status = $"TX armed ({period}) next slot → {FreeText}";
    }

    void OnSpectrumTick(object? sender, EventArgs e)
    {
        var frame = _capture.GetLatestFrame();
        UpdateRxAudioLevel(frame);

        if (_waterfall is null) return;

        const int fftN = 4096;
        if (frame.Length == 0)
        {
            var noise = new float[fftN];
            var rng = Random.Shared;
            for (int i = 0; i < noise.Length; i++)
                noise[i] = (float)((rng.NextDouble() - 0.5) * 0.002);
            _spectrum.Analyze(noise);
        }
        else
        {
            _spectrum.Analyze(frame.Length >= fftN ? frame.AsSpan(^fftN) : frame);
        }

        _waterfall.PushSpectrum(_spectrum.MagnitudesDb);
    }

    void UpdateRxAudioLevel(float[] frame)
    {
        if (frame.Length == 0 || !_capture.IsRunning)
        {
            RxLevelNorm = 0;
            RxLevelText = "IN —";
            return;
        }

        double sumSq = 0;
        for (int i = 0; i < frame.Length; i++)
        {
            double s = frame[i];
            sumSq += s * s;
        }

        double rms = Math.Sqrt(sumSq / frame.Length);
        double db = rms <= 1e-10 ? -60 : 20.0 * Math.Log10(rms);
        db = Math.Clamp(db, -60, 0);
        // WSJT-X-style bar: −54 dBFS → empty, 0 dBFS → full (mid-scale is a good digi level).
        RxLevelNorm = Math.Clamp((db + 54.0) / 54.0, 0, 1);
        RxLevelText = $"IN {db:0} dB";
    }

    void PreferJtdxIfAvailable()
    {
        // Prefer installer-bundled tools when settings are empty or point at a missing file.
        if (string.IsNullOrWhiteSpace(_settings.JtdxJt9Path) || !File.Exists(_settings.JtdxJt9Path))
        {
            string? found = JtdxPaths.FindJt9();
            if (!string.IsNullOrWhiteSpace(found))
            {
                _settings.JtdxJt9Path = found;
                _settings.DecoderEngine = DecoderEngine.Jtdx;
                _settings.SimulateDecodes = false;
            }
        }

        if (string.IsNullOrWhiteSpace(_settings.HamlibRigctldPath) || !File.Exists(_settings.HamlibRigctldPath))
        {
            string? rig = HamlibPaths.FindRigctld();
            if (!string.IsNullOrWhiteSpace(rig))
                _settings.HamlibRigctldPath = rig;
        }
    }

    void FindJtdx()
    {
        string? path = JtdxPaths.FindJt9(_settings.JtdxJt9Path);
        if (string.IsNullOrWhiteSpace(path))
        {
            Status = "jtdxjt9.exe not found. Install JTDX (contest build recommended).";
            return;
        }

        JtdxJt9Path = path;
        SelectedDecoderEngine = nameof(DecoderEngine.Jtdx);
        SimulateDecodes = false;
        Status = $"JTDX decoder: {path}";
    }

    bool ShouldUseJtdx() =>
        !SimulateDecodes
        && _settings.DecoderEngine == DecoderEngine.Jtdx
        && _jtdx.IsAvailable(_settings.JtdxJt9Path);

    void TriggerSlotDecode()
    {
        if (ShouldUseJtdx())
        {
            if (_jtdx.IsBusy)
            {
                Status = "JTDX still decoding previous slot…";
                AppendDecodeLog("skip: decoder busy");
                FinishDeferredTxAfterDecode(); // don't skip our TX slot entirely
                return;
            }
            _ = DecodeWithJtdxAsync();
            return;
        }

        if (SimulateDecodes || _settings.DecoderEngine == DecoderEngine.Simulate ||
            !_jtdx.IsAvailable(_settings.JtdxJt9Path))
        {
            InjectSimulatedDecodes();
            return;
        }

        FinishDeferredTxAfterDecode();
    }

    async Task DecodeWithJtdxAsync()
    {
        int seq = ++_jtdxDecodeSeq;
        // JustRolled fires at the start of the new slot — decode the previous period.
        DateTime slotUtc = _timing.UtcNow.AddSeconds(-(_timing.SlotSeconds > 0 ? _timing.SlotSeconds : 15));
        bool ft4 = Mode.Equals("FT4", StringComparison.OrdinalIgnoreCase);
        double slotSec = ft4 ? 7.5 : 15.0;

        if (!_capture.IsRunning)
        {
            Status = "JTDX: Start Monitor and select the radio/VAC input device.";
            AppendDecodeLog("abort: monitor not running");
            FinishDeferredTxAfterDecode();
            return;
        }

        // Slightly longer than the period so the decoder sees the full FT8/FT4 transmission.
        float[] audio = _capture.CopyRecent(slotSec + 0.4);
        double gotSec = audio.Length / (double)AudioCaptureService.SampleRate;
        AppendDecodeLog(
            $"attempt slot={slotUtc:HH:mm:ss} buf={_capture.BufferedSeconds:0.0}s copy={gotSec:0.0}s mon={_capture.IsRunning} in={RxLevelText}");

        if (audio.Length < AudioCaptureService.SampleRate * 5)
        {
            Status =
                $"JTDX: only {gotSec:0.0}s buffered (need ~{slotSec:0}s) — Start Monitor, pick VAC/radio input, wait one slot.";
            FinishDeferredTxAfterDecode();
            return;
        }

        Status = $"JTDX decoding ({gotSec:0.0}s)…";
        try
        {
            var hits = await _jtdx.DecodeSlotAsync(audio, _settings, slotUtc).ConfigureAwait(true);
            if (seq != _jtdxDecodeSeq) return;
            AppendDecodeLog($"done hits={hits.Count}");
            string label = hits.Count == 0
                ? "JTDX — check IN level / input device"
                : "JTDX";
            PushDecodes(hits, label, slotUtc);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("busy", StringComparison.OrdinalIgnoreCase))
        {
            Status = "JTDX still decoding previous slot…";
            FinishDeferredTxAfterDecode();
        }
        catch (OperationCanceledException)
        {
            // disposed / shutdown
            _deferTxUntilDecode = false;
        }
        catch (Exception ex)
        {
            if (seq != _jtdxDecodeSeq) return;
            AppendDecodeLog($"error: {ex}");
            Status = $"JTDX decode failed: {ex.Message}";
            FinishDeferredTxAfterDecode();
        }
    }

    static void AppendDecodeLog(string line)
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SignalDx", "decode");
            Directory.CreateDirectory(dir);
            File.AppendAllText(
                Path.Combine(dir, "decode-log.txt"),
                $"{DateTime.UtcNow:HH:mm:ss.fff}Z {line}{Environment.NewLine}");
        }
        catch
        {
            // ignore
        }
    }

    void InjectSimulatedDecodes()
    {
        var hits = _sim.GenerateSlot(_timing.UtcNow, _settings);
        PushDecodes(hits, "sim", _timing.UtcNow);
    }

    void PushDecodes(IReadOnlyList<DecodeHit> hits, string engineLabel, DateTime utc)
    {
        void body()
        {
            bool callAlert = false;
            bool wantedAlert = false;
            string? wantedReason = null;
            bool autoReplied = false;
            foreach (var hit in hits)
            {
                MarkWorkingFlags(hit);
                string? want = _wanted.Match(hit);
                hit.IsWanted = want is not null;
                Activity.Insert(0, hit);
                if (Activity.Count > 500)
                    Activity.RemoveAt(Activity.Count - 1);

                if (hit.IsToMe)
                    callAlert = true;

                if (TryWantedAlert(hit, out string reason))
                {
                    wantedAlert = true;
                    wantedReason = $"{hit.DxCall} ({reason})";
                }

                // Auto-seq returns the correct next macro when a transmitter answers.
                // Double-click (EnableTxForStation) remains available as a manual override.
                if (AutoSequence && TryAutoReplyToDecode(hit))
                    autoReplied = true;
            }

            UpdateRxActivityAfterNewHits(hits);
            _external.ReportDecodes(hits, _settings);
            if (callAlert && CallAlertEnabled)
                _callAlert.Play(CallAlertSoundPath);
            // Wanted audio is independent of "call my callsign" — always play when armed.
            if (wantedAlert && WantedAlertEnabled)
            {
                string wav = !string.IsNullOrWhiteSpace(WantedAlertSoundPath)
                    ? WantedAlertSoundPath
                    : CallAlertSoundPath;
                _callAlert.Play(wav);
            }
            if (wantedAlert && !string.IsNullOrWhiteSpace(wantedReason))
                Status = $"★ Wanted {wantedReason}";
            else if (!autoReplied && !_holdCq && !_deferTxUntilDecode)
                Status = $"Decoded {hits.Count} ({engineLabel}) @ {utc:HH:mm:ss} UTC";

            // Key this slot now with the (possibly advanced) macro — do not wait another cycle.
            FinishDeferredTxAfterDecode();
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            body();
        else
            dispatcher.BeginInvoke(body);
    }

    /// <summary>
    /// When Auto seq is on: automatically load the next TX macro if a station returns our call
    /// (CQ reply, or reply while we are working them). Returns true if FreeText/phase changed.
    /// </summary>
    bool TryAutoReplyToDecode(DecodeHit hit)
    {
        if (!hit.IsToMe) return false;

        bool midQso = _seq.Phase is QsoPhase.CallingDx or QsoPhase.SentReport
            or QsoPhase.SentRogerReport or QsoPhase.Sent73
            or QsoPhase.SendingSeventyThree;

        var qsoToken = AutoSequencer.ExtractQsoToken(hit.Message);

        // --- CQ: first grid reply only while actually calling CQ — never steal a mid-QSO. ---
        // (IsFreeTextCq alone used to let a second caller yank us off Tx2 after their R-report.)
        if ((_holdCq || _seq.Phase == QsoPhase.CallingCq) && !midQso)
        {
            if (TryAcceptCqReply(hit))
            {
                _holdCq = false;
                Report = FormatSnr(hit.SnrDb);
                _seq.Report = Report;
                _seq.StartCq();
                _seq.TryAdvanceFromDecode(hit.Message, _settings);
                ApplySeqToUi(hit, $"CQ answered by {_seq.DxCall} → {_seq.NextTxMessage(_settings)}");
                return true;
            }
        }

        // --- Working station sent R-xx / RRR while we are on Tx1/Tx2/Tx3 → RR73 (Tx4) ---
        if (IsDecodeFromWorkingStation(hit) &&
            (qsoToken is Ft8QsoToken.RogerReport or Ft8QsoToken.Rrr) &&
            (_seq.Phase is QsoPhase.CallingDx or QsoPhase.SentReport or QsoPhase.SentRogerReport))
        {
            if (!_seq.TryAdvanceFromDecode(hit.Message, _settings) ||
                _seq.Phase is not (QsoPhase.Sent73 or QsoPhase.Complete))
            {
                _seq.QueueRr73(
                    string.IsNullOrWhiteSpace(_seq.DxCall) ? DxCall : null,
                    DxGrid, Report);
            }
            ApplySeqToUi(hit, $"{_seq.DxCall} sent R-report → Tx4 {_seq.NextTxMessage(_settings)}");
            return true;
        }

        // --- Their 73 / RR73: first farewell from either side opens the log window ---
        if (IsDecodeFromWorkingStation(hit) &&
            (qsoToken is Ft8QsoToken.SeventyThree or Ft8QsoToken.Rr73))
        {
            var parts = hit.Message.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string their = parts.Length >= 2 ? parts[1] : DxCall;

            // Already queued/sent RR73 or plain 73 — reciprocal farewell ends the call.
            // Do not reopen the log window (first 73 already did).
            if (_seq.Phase is QsoPhase.Sent73 or QsoPhase.SendingSeventyThree)
            {
                if (!_seq.TryAdvanceFromDecode(hit.Message, _settings))
                    _seq.MarkComplete();
                ApplySeqToUi(hit, $"QSO complete with {_seq.DxCall}");
                return true;
            }

            // They called 73/RR73 first while we were still on Tx1–Tx3 → log once, then Tx4 RR73.
            if (qsoToken is Ft8QsoToken.SeventyThree ||
                (qsoToken is Ft8QsoToken.Rr73 &&
                 (_seq.Phase is QsoPhase.SentReport or QsoPhase.SentRogerReport or QsoPhase.CallingDx)))
            {
                if (string.IsNullOrWhiteSpace(_seq.DxCall))
                    _seq.StartWorking(
                        string.IsNullOrWhiteSpace(DxCall) ? their : DxCall,
                        DxGrid, Report, forceRestart: true);
                _seq.QueueRr73(
                    string.IsNullOrWhiteSpace(_seq.DxCall) ? their : null,
                    DxGrid, Report);
                ApplySeqToUi(hit, $"{_seq.DxCall} sent 73 → Tx4 {_seq.NextTxMessage(_settings)}");
                PromptLogQso($"{_seq.DxCall} sent 73 — review QSO");
                return true;
            }

            // Their RR73 while not mid-farewell TX → done (first farewell → one log prompt).
            if (qsoToken is Ft8QsoToken.Rr73)
            {
                _seq.MarkComplete();
                ApplySeqToUi(hit, $"QSO complete with {_seq.DxCall}");
                PromptLogQso($"{_seq.DxCall} sent RR73 — review QSO");
                return true;
            }
        }

        // --- Already working this transmitter: advance Tx2/Tx3/Tx4 from their reply ---
        if (IsDecodeFromWorkingStation(hit) && midQso)
        {
            if (!_seq.TryAdvanceFromDecode(hit.Message, _settings))
                return false;

            if (_seq.Phase is QsoPhase.SentReport or QsoPhase.SentRogerReport)
            {
                Report = FormatSnr(hit.SnrDb);
                _seq.Report = Report;
            }

            ApplySeqToUi(hit, $"{_seq.DxCall} → {_seq.Phase}: {_seq.NextTxMessage(_settings)}");
            if (_seq.Phase == QsoPhase.Complete)
                PromptLogQso("QSO complete");
            return true;
        }

        // --- New caller while idle/complete (Enable TX armed): auto-return their call ---
        // Never while mid-QSO — another station's grid must not yank us off Tx2/R-report.
        if (EnableTx && !midQso &&
            (_seq.Phase is QsoPhase.Idle or QsoPhase.Complete) &&
            LooksLikeIncomingCall(hit))
        {
            Report = FormatSnr(hit.SnrDb);
            _seq.ArmFromDecode(hit.Message, _settings, Report);
            if (string.IsNullOrWhiteSpace(_seq.DxCall))
                return false;

            // Reply in the opposite period from their transmission.
            TxEven = !IsEvenPeriod(hit.Utc);
            ApplySeqToUi(hit, $"Auto-reply {_seq.DxCall} → {_seq.NextTxMessage(_settings)}");
            return true;
        }

        return false;
    }

    static bool MessageEndsWith73(string message)
    {
        var token = AutoSequencer.ExtractQsoToken(message);
        return token is Ft8QsoToken.SeventyThree or Ft8QsoToken.Rr73;
    }

    void ApplySeqToUi(DecodeHit hit, string status)
    {
        PhaseLabel = _seq.Phase.ToString();
        if (!string.IsNullOrWhiteSpace(_seq.DxCall))
            DxCall = _seq.DxCall;
        if (!string.IsNullOrWhiteSpace(_seq.DxGrid))
            DxGrid = _seq.DxGrid;

        RxHz = hit.Hz;
        EnsureInRxActivity(hit);

        string? next = _seq.NextTxMessage(_settings);
        if (_seq.Phase == QsoPhase.Complete)
        {
            // Do not keep transmitting the previous macro (e.g. Tx3 Rxx) after the QSO ends.
            EnableTx = false;
            _deferTxUntilDecode = false;
            InvalidateTxAudioCache();
            Status = status;
            RefreshMacroPreviews();
            return;
        }

        if (!string.IsNullOrWhiteSpace(next))
            FreeText = next;
        EnableTx = true;
        InvalidateTxAudioCache();
        PrimeTxAudioCache(FreeText);
        Status = status;
        RefreshMacroPreviews();

        // Key immediately when their 73 advanced us to RR73 (Tx4) in our period —
        // do not wait for another full even/odd cycle.
        TryStartTxNowIfOurSlot();
    }

    bool IsFreeTextCq() =>
        FreeText.StartsWith("CQ ", StringComparison.OrdinalIgnoreCase);

    /// <summary>Incoming call to us with grid or report (someone initiating / returning).</summary>
    bool LooksLikeIncomingCall(DecodeHit hit)
    {
        var parts = hit.Message.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return false;
        if (!parts[0].Equals(MyCall.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;
        if (parts[1].Equals(MyCall.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;

        var token = AutoSequencer.ExtractQsoToken(hit.Message);
        return token is Ft8QsoToken.Grid or Ft8QsoToken.Report or Ft8QsoToken.RogerReport;
    }

    /// <summary>True when decode is addressed to us from the station we are currently working.</summary>
    bool IsDecodeFromWorkingStation(DecodeHit hit)
    {
        string work = !string.IsNullOrWhiteSpace(_seq.DxCall) ? _seq.DxCall : DxCall;
        var parts = hit.Message.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;

        string my = MyCall.Trim();
        if (!parts[0].Equals(my, StringComparison.OrdinalIgnoreCase))
            return false;

        string their = parts[1];
        if (!string.IsNullOrWhiteSpace(work) &&
            their.Equals(work, StringComparison.OrdinalIgnoreCase))
            return true;

        // Also match decode's transmitter call if UI DX matches.
        if (!string.IsNullOrWhiteSpace(hit.DxCall) &&
            !string.IsNullOrWhiteSpace(DxCall) &&
            hit.DxCall.Equals(DxCall, StringComparison.OrdinalIgnoreCase) &&
            their.Equals(DxCall, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>True when decode is MYCALL THEIRCALL GRID (first reply to our CQ).</summary>
    bool TryAcceptCqReply(DecodeHit hit)
    {
        // While CQ hold is armed, ignore decodes that predate this CQ session.
        if (_holdCq && hit.Utc < _cqSessionUtc.AddSeconds(-2))
            return false;

        var parts = hit.Message.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return false;

        string my = MyCall.Trim();
        if (!parts[0].Equals(my, StringComparison.OrdinalIgnoreCase))
            return false;

        string their = parts[1];
        if (their.Equals(my, StringComparison.OrdinalIgnoreCase))
            return false;

        return AutoSequencer.ExtractQsoToken(hit.Message) == Ft8QsoToken.Grid;
    }

    void MarkWorkingFlags(DecodeHit hit)
    {
        hit.IsPreviouslyWorked = _workedCalls.Contains(hit.DxCall);

        string work = !string.IsNullOrWhiteSpace(_seq.DxCall) ? _seq.DxCall : DxCall;
        work = (work ?? "").Trim();
        if (string.IsNullOrEmpty(work))
        {
            hit.IsWorking = false;
            return;
        }

        hit.IsWorking =
            (!string.IsNullOrWhiteSpace(hit.DxCall) &&
             hit.DxCall.Equals(work, StringComparison.OrdinalIgnoreCase)) ||
            hit.Message.Contains(work, StringComparison.OrdinalIgnoreCase);
    }

    void PushLocalTxDecode(string message)
    {
        var parsed = Ft8MessageBuilder.Parse(message);
        // Flag / DxCall on Band Activity = transmitter. For our own TX that is MyCall —
        // never the DX we're addressing (ViewModel.DxCall), which was painting the called
        // station's flag on outbound rows.
        string? txCall = !string.IsNullOrWhiteSpace(MyCall) ? MyCall.Trim().ToUpperInvariant() : parsed.DxCall;
        string? txGrid = MyGrid;
        if (!string.IsNullOrWhiteSpace(txGrid) && txGrid.Length > 4)
            txGrid = txGrid[..4];
        string? country = CallsignCountry.FromCall(txCall);
        // Distance still toward the DX we're working (if any).
        string? distGrid = string.IsNullOrWhiteSpace(DxGrid) ? parsed.DxGrid : DxGrid;
        var hit = new DecodeHit
        {
            Utc = DateTime.UtcNow,
            Mode = Mode,
            Hz = TxHz,
            SnrDb = 0,
            Dt = 0,
            Message = $"→ {message}",
            DxCall = txCall,
            DxGrid = txGrid,
            Country = country,
            State = UsState.Resolve(country, txGrid, txCall),
            IsNew = false,
            IsCq = parsed.IsCq,
            IsToMe = false,
            IsWorking = true, // our TX — shown green in RX (calling CQ / a station)
            IsPreviouslyWorked = _workedCalls.Contains(txCall),
            DistanceMiles = Maidenhead.DistanceMiles(MyGrid, distGrid) is double mi ? Math.Round(mi) : null
        };

        Activity.Insert(0, hit);
        if (Activity.Count > 500)
            Activity.RemoveAt(Activity.Count - 1);

        // RX pane already includes TX-frequency rows so outbound calls stay visible while split.
        UpdateRxActivityAfterNewHits([hit]);
    }

    void UpdateRxActivityAfterNewHits(IReadOnlyList<DecodeHit> hits)
    {
        if (_rxHistorySuppressed)
        {
            // Only show traffic that arrived after Clear — do not pull old Band Activity rows back in.
            foreach (var hit in hits)
            {
                if (!MatchesRxPane(hit)) continue;
                if (RxActivity.Contains(hit)) continue;
                RxActivity.Insert(0, hit);
                if (RxActivity.Count > 80)
                    RxActivity.RemoveAt(RxActivity.Count - 1);
            }
            return;
        }

        RebuildRxFilter();
    }

    /// <summary>
    /// RX pane: near Rx/Tx audio, plus anything addressed to us (CQ replies often land off our Tx Hz),
    /// plus traffic involving the station we are working.
    /// </summary>
    bool MatchesRxPane(DecodeHit hit)
    {
        if (Math.Abs(hit.Hz - RxHz) <= 50 || Math.Abs(hit.Hz - TxHz) <= 50)
            return true;
        if (hit.IsToMe)
            return true;
        if (hit.IsWorking)
            return true;

        string work = !string.IsNullOrWhiteSpace(_seq.DxCall) ? _seq.DxCall : DxCall;
        if (!string.IsNullOrWhiteSpace(work) &&
            hit.Message.Contains(work, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>Force a decode into the RX pane (CQ reply / working station), even after Clear.</summary>
    void EnsureInRxActivity(DecodeHit hit)
    {
        if (!RxActivity.Contains(hit))
        {
            RxActivity.Insert(0, hit);
            if (RxActivity.Count > 80)
                RxActivity.RemoveAt(RxActivity.Count - 1);
        }

        // Highlight quietly — do not run ApplySelectedDecode / StartWorking.
        _selectedRxDecode = hit;
        Raise(nameof(SelectedRxDecode));
    }

    void RebuildRxFilter()
    {
        if (_rxHistorySuppressed)
            return;

        // Clear selection first — clearing ItemsSource while SelectedItem is set can crash WPF DataGrid automation.
        DecodeHit? keep = _selectedRxDecode;
        SelectedRxDecode = null;
        RxActivity.Clear();
        // Near Rx/Tx Hz, calls-to-me, and working-station traffic.
        foreach (var h in Activity.Where(MatchesRxPane).Take(80))
            RxActivity.Add(h);
        if (keep is not null && RxActivity.Contains(keep))
            _selectedRxDecode = keep; // restore quietly without re-running ApplySelectedDecode
        Raise(nameof(SelectedRxDecode));
    }

    void SendMacro(MacroDisplayItem? item)
    {
        if (item is null || !EnableTx) return;
        var macro = item.Macro;
        string msg = macro.Kind == TxMacroKind.FreeText && !string.IsNullOrWhiteSpace(FreeText)
            ? FreeText
            : Ft8MessageBuilder.Build(macro.Template, _settings, DxCall, Report);

        // CQ / CQ DX / CQ EU: arm CQ-hold and clear the prior DX so the next slot
        // does not fall back to AutoSequence calling the previous station.
        if ((macro.Kind is TxMacroKind.Cq or TxMacroKind.Custom) &&
            msg.StartsWith("CQ ", StringComparison.OrdinalIgnoreCase))
        {
            _settings.CqTemplate = string.IsNullOrWhiteSpace(macro.Template)
                ? "CQ {mycall} {mygrid}"
                : macro.Template.Trim();
            SaveSettingsNow();
            ArmCqHold();
            // Never key a full cycle mid-period — that bleeds into the next sequence.
            if (TryStartTxNowIfOurSlot())
                return;
            string period = TxEven ? "even" : "odd";
            Status = $"CQ hold ON ({period}) → {FreeText}";
            return;
        }

        _holdCq = false;
        FreeText = msg;
        InvalidateTxAudioCache();
        PrimeTxAudioCache(msg);
        if (TryStartTxNowIfOurSlot())
            return;

        string waitPeriod = TxEven ? "even" : "odd";
        Status = $"TX armed ({waitPeriod}) → {msg}";
    }

    void RefreshMacroPreviews()
    {
        foreach (var item in Macros)
            item.Refresh(_settings, DxCall, Report, FreeText);
    }

    void RebuildMacroList()
    {
        Macros.Clear();
        foreach (var m in Ft8MessageBuilder.DefaultMacros)
            Macros.Add(new MacroDisplayItem(m));
        foreach (var entry in _settings.CustomMacros ?? [])
        {
            if (string.IsNullOrWhiteSpace(entry.Template)) continue;
            Macros.Add(new MacroDisplayItem(Ft8MessageBuilder.ToTxMacro(entry)));
        }
        RefreshMacroPreviews();
    }

    string BuildCqMessage() =>
        Ft8MessageBuilder.Build(
            string.IsNullOrWhiteSpace(_settings.CqTemplate) ? "CQ {mycall} {mygrid}" : _settings.CqTemplate,
            _settings, "", Report);

    void AddCustomMacro()
    {
        string label = (NewCustomMacroLabel ?? "").Trim();
        string template = (NewCustomMacroTemplate ?? "").Trim();
        if (string.IsNullOrWhiteSpace(template))
        {
            Status = "Custom macro needs a template (e.g. CQ DX {mycall} {mygrid})";
            return;
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            // First token(s) before placeholders → label
            label = template
                .Replace("{mycall}", "", StringComparison.OrdinalIgnoreCase)
                .Replace("{mygrid}", "", StringComparison.OrdinalIgnoreCase)
                .Replace("{dxcall}", "", StringComparison.OrdinalIgnoreCase)
                .Replace("{snr}", "", StringComparison.OrdinalIgnoreCase)
                .Trim();
            if (string.IsNullOrWhiteSpace(label)) label = "Custom";
        }

        _settings.CustomMacros ??= [];
        var entry = new CustomMacroEntry
        {
            Label = label.ToUpperInvariant(),
            Template = template
        };
        _settings.CustomMacros.Add(entry);
        Macros.Add(new MacroDisplayItem(Ft8MessageBuilder.ToTxMacro(entry)));
        RefreshMacroPreviews();
        SaveSettingsNow();
        Status = $"Custom macro added → {label}: {Ft8MessageBuilder.Build(template, _settings, DxCall, Report)}";
        NewCustomMacroLabel = "";
        NewCustomMacroTemplate = "";
    }

    void RemoveCustomMacro(MacroDisplayItem? item)
    {
        if (item is null || !item.IsCustom || string.IsNullOrWhiteSpace(item.CustomId)) return;
        _settings.CustomMacros?.RemoveAll(m => m.Id == item.CustomId);
        Macros.Remove(item);
        SaveSettingsNow();
        Status = $"Removed custom macro → {item.Label}";
    }

    /// <summary>Rebuild FreeText from standard WSJT-X-style templates for the current DX/phase.</summary>
    void ResetTxMacros()
    {
        string? next = _seq.NextTxMessage(_settings);
        if (!string.IsNullOrWhiteSpace(next))
        {
            FreeText = next;
            PhaseLabel = _seq.Phase.ToString();
            Status = $"TX macros reset → {FreeText}";
            return;
        }

        if (!string.IsNullOrWhiteSpace(DxCall))
        {
            FreeText = Ft8MessageBuilder.Build("{dxcall} {mycall} {mygrid}", _settings, DxCall, Report);
            Status = $"TX macros reset → {FreeText}";
            return;
        }

        FreeText = BuildCqMessage();
        Status = $"TX macros reset → {FreeText}";
    }

    /// <summary>Operator typed a callsign — load it into macros / sequencer (Tx1 grid call).</summary>
    void ApplyManualDxCall()
    {
        string call = (DxCall ?? "").Trim().ToUpperInvariant();
        string grid = (DxGrid ?? "").Trim().ToUpperInvariant();
        DxCall = call;
        DxGrid = grid;

        if (string.IsNullOrWhiteSpace(call))
        {
            Status = "Enter a DX callsign for macros";
            return;
        }

        _holdCq = false;
        _qsoLogPrompted = false;
        _qsoStartUtc = DateTime.UtcNow;
        _seq.StartWorking(call, grid, Report, forceRestart: true);
        PhaseLabel = _seq.Phase.ToString();
        FreeText = _seq.NextTxMessage(_settings) ??
                   Ft8MessageBuilder.Build("{dxcall} {mycall} {mygrid}", _settings, call, Report);
        InvalidateTxAudioCache();
        PrimeTxAudioCache(FreeText);
        RefreshMacroPreviews();
        Status = $"Manual DX → {call}  ({FreeText})";
    }

    void InvalidateTxAudioCache()
    {
        _txAudioCache = null;
        _txAudioCacheKey = null;
    }

    string TxAudioCacheKey(string message) =>
        $"{Mode}|{TxHz}|{message.Trim().ToUpperInvariant()}";

    void PrimeTxAudioCache(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        string key = TxAudioCacheKey(message);
        if (_txAudioCache is not null && _txAudioCacheKey == key) return;

        try
        {
            if (IsWsprMode)
            {
                if (!TryParseWsprMessage(message, out string call, out string grid, out int dbm))
                {
                    call = MyCall;
                    string g = (MyGrid ?? "FN42").Trim().ToUpperInvariant();
                    grid = g.Length > 4 ? g[..4] : g;
                    dbm = WsprEncoder.SnapPower(_settings.WsprPowerDbm);
                }
                _txAudioCache = WsprToneGenerator.Generate(call, grid, dbm, TxHz, drive01: 1.0);
            }
            else
            {
                double seconds = Mode.Equals("FT4", StringComparison.OrdinalIgnoreCase) ? 6.0 : 12.64;
                string? encoderExe = Mode.Equals("FT4", StringComparison.OrdinalIgnoreCase)
                    ? Ft8CodePaths.FindFt4Code()
                    : Ft8CodePaths.FindFt8Code();
                _txAudioCache = Ft8ToneGenerator.Generate(
                    message.Trim(), TxHz, seconds, drive01: 1.0, mode: Mode, codeExeHint: encoderExe);
            }
            _txAudioCacheKey = key;
        }
        catch
        {
            InvalidateTxAudioCache();
        }
    }

    async Task ToggleTuneAsync()
    {
        if (IsTuning)
        {
            await StopTuneAsync("Tune stopped.");
            return;
        }

        if (IsTransmitting) return;

        IsTuning = true;
        IsTransmitting = true;
        _tuneCts?.Cancel();
        _tuneCts?.Dispose();
        _tuneCts = new CancellationTokenSource();
        var ct = _tuneCts.Token;

        try
        {
            await PushRfPowerAsync();
            float drive = (float)Math.Clamp(TxDrivePercent / 100.0, 0.15, 1);
            // Up to 60s continuous carrier at Tx Hz for SWR / ALC.
            float[] audio = Ft8ToneGenerator.GenerateConstantTone(TxHz, seconds: 60, drive01: 1.0);

            int outIdx = OutputDevices.Count == 0 ? -1 : SelectedOutputIndex;
            string? outNamePref = string.IsNullOrWhiteSpace(_settings.OutputDeviceName)
                ? (outIdx >= 0 && outIdx < OutputDevices.Count ? OutputDevices[outIdx] : null)
                : _settings.OutputDeviceName;

            await EnsureDigiModeAsync();
            float playGain = Math.Clamp(Math.Max(drive, 0.35f), 0, 1);
            _playback.PlayMono(
                audio,
                AudioCaptureService.SampleRate,
                outIdx,
                volume01: playGain,
                preferredName: outNamePref);
            await Task.Delay(80, ct);
            if (!_playback.IsPlaying)
                throw new InvalidOperationException("Tune audio did not start — check Out device.");

            await _cat.SetPttAsync(true, CatPttEnabled);
            Status = $"TUNE {TxHz} Hz — max 60s (Tune or Halt to stop)";

            await Task.Delay(TimeSpan.FromSeconds(60), ct);
            await StopTuneAsync("Tune ended (60s).");
        }
        catch (OperationCanceledException)
        {
            // Stopped by user (Tune toggle / Halt).
        }
        catch (Exception ex)
        {
            Status = $"Tune failed: {ex.Message}";
            await StopTuneAsync(null);
        }
    }

    async Task StopTuneAsync(string? status)
    {
        try { _tuneCts?.Cancel(); } catch { /* ignore */ }
        try { _tuneCts?.Dispose(); } catch { /* ignore */ }
        _tuneCts = null;

        _playback.Stop();
        try { await _cat.SetPttAsync(false, CatPttEnabled); } catch { /* ignore */ }

        bool wasTuning = IsTuning;
        IsTuning = false;
        IsTransmitting = false;
        if (status is not null)
            Status = status;
        else if (wasTuning && string.IsNullOrWhiteSpace(Status))
            Status = "Tune stopped.";
        CommandManager.InvalidateRequerySuggested();
    }

    async Task TransmitMessageAsync(string message)
    {
        if (IsTransmitting || IsTuning) return;
        IsTransmitting = true;
        _qsoStartUtc ??= DateTime.UtcNow;
        bool promptLogAfterTx = false;
        try
        {
            message = (message ?? "").Trim();
            if (message.StartsWith('→'))
                message = message.TrimStart('→', ' ').Trim();

            // Show the outbound call in Band/RX activity at the TX frequency.
            PushLocalTxDecode(message);

            await PushRfPowerAsync();
            bool wspr = IsWsprMode;
            double seconds = wspr
                ? WsprEncoder.SignalDurationSec + WsprEncoder.LeadInSec
                : Mode.Equals("FT4", StringComparison.OrdinalIgnoreCase) ? 6.0 : 12.64;

            // Cap TX to the remainder of this period so audio/PTT never spill
            // a full cycle into the next sequence.
            _timing.Tick(DateTime.UtcNow);
            int startSlot = _timing.SlotIndex;
            double remaining = Math.Max(0, _timing.SlotSeconds - _timing.SecondsIntoSlot - 0.05);
            double holdSec = Math.Min(seconds + 0.2, remaining);
            double minHold = wspr ? WsprEncoder.SignalDurationSec * 0.95 : 1.0;
            if (holdSec < minHold)
            {
                Status = "TX skipped — too late in this period (armed for next)";
                AppendTxLog($"TX SKIP late into={_timing.SecondsIntoSlot:0.00}s remain={remaining:0.00}s");
                return;
            }

            float drive = (float)Math.Clamp(TxDrivePercent / 100.0, 0, 1);
            string? encoderExe = wspr
                ? null
                : Mode.Equals("FT4", StringComparison.OrdinalIgnoreCase)
                    ? Ft8CodePaths.FindFt4Code()
                    : Ft8CodePaths.FindFt8Code();
            string cacheKey = TxAudioCacheKey(message);
            float[] audio;
            try
            {
                if (_txAudioCache is not null && _txAudioCacheKey == cacheKey)
                {
                    audio = _txAudioCache;
                }
                else if (wspr)
                {
                    if (!TryParseWsprMessage(message, out string call, out string grid, out int dbm))
                    {
                        call = MyCall;
                        string g = (MyGrid ?? "FN42").Trim().ToUpperInvariant();
                        grid = g.Length > 4 ? g[..4] : g;
                        dbm = WsprEncoder.SnapPower(_settings.WsprPowerDbm);
                        message = WsprEncoder.FormatMessage(call, grid, dbm);
                    }
                    audio = WsprToneGenerator.Generate(call, grid, dbm, TxHz, drive01: 1.0);
                    _txAudioCache = audio;
                    _txAudioCacheKey = cacheKey;
                }
                else
                {
                    // Real FT8/FT4 channel symbols via WSJT-X ft8code/ft4code.
                    audio = Ft8ToneGenerator.Generate(
                        message, TxHz, seconds, drive01: 1.0, mode: Mode, codeExeHint: encoderExe);
                    _txAudioCache = audio;
                    _txAudioCacheKey = cacheKey;
                }
            }
            catch (Exception ex)
            {
                Status = $"TX encode failed: {ex.Message}";
                return;
            }

            int outIdx = OutputDevices.Count == 0 ? -1 : SelectedOutputIndex;
            string? outNamePref = string.IsNullOrWhiteSpace(_settings.OutputDeviceName)
                ? (outIdx >= 0 && outIdx < OutputDevices.Count ? OutputDevices[outIdx] : null)
                : _settings.OutputDeviceName;
            string encName = wspr
                ? "wspr-enc"
                : string.IsNullOrEmpty(encoderExe) ? "?" : Path.GetFileName(encoderExe);
            float peak = 0;
            for (int i = 0; i < audio.Length; i += 32)
            {
                float a = Math.Abs(audio[i]);
                if (a > peak) peak = a;
            }

            ApplyDriveLevel(_settings.TxDrivePercent);

            // USB digi radios (FT-710 etc.) need DATA-U / PKTUSB — plain USB often keys RF with no USB audio.
            string digiMode = await EnsureDigiModeAsync();

            // Start USB audio first so the codec stream is running, then key PTT.
            float playGain = Math.Clamp(Math.Max(drive, 0.35f), 0, 1);
            _playback.PlayMono(
                audio,
                AudioCaptureService.SampleRate,
                outIdx,
                volume01: playGain,
                preferredName: outNamePref);
            await Task.Delay(80);
            if (!_playback.IsPlaying)
                throw new InvalidOperationException(
                    $"Audio did not start on '{_playback.ActiveDeviceName ?? outNamePref ?? "?"}'. Check Out device.");

            await _cat.SetPttAsync(true, CatPttEnabled);

            if (!string.IsNullOrWhiteSpace(_playback.ActiveDeviceName))
            {
                _settings.OutputDeviceName = _playback.ActiveDeviceName;
                int match = OutputDevices.ToList().FindIndex(n =>
                    n.Equals(_playback.ActiveDeviceName, StringComparison.OrdinalIgnoreCase));
                if (match >= 0)
                    _settings.OutputDeviceIndex = match;
            }

            string txWarn = wspr
                ? (TxHz is < 1400 or > 1600
                    ? $"  ⚠ Tx {TxHz} Hz is outside typical WSPR audio (use ~1400–1600)"
                    : "")
                : (TxHz is < 500 or > 2500
                    ? $"  ⚠ Tx {TxHz} Hz is outside typical FT8 activity (use ~1000–2000)"
                    : "");
            Status =
                $"TX: {message} @ {TxHz} Hz → {_playback.ActiveDeviceName}  " +
                $"mode {digiMode}  drive {_settings.TxDrivePercent}% peak {peak * playGain:0.00}  [{encName}]{txWarn}";
            AppendTxLog(
                $"TX ok msg='{message}' txHz={TxHz} out='{_playback.ActiveDeviceName}' mode={digiMode} " +
                $"drive={_settings.TxDrivePercent}% peak={peak * playGain:0.00} enc={encName} hold={holdSec:0.00}s");

            await HoldTxUntilAsync(holdSec, startSlot);
            // Only the first farewell opens the log — skip if DX already sent 73/RR73.
            if (!wspr &&
                IsSeventyThreeMessage(message) &&
                !string.IsNullOrWhiteSpace(DxCall) &&
                !_qsoLogPrompted)
                promptLogAfterTx = true;
        }
        catch (Exception ex)
        {
            Status = $"TX failed: {ex.Message}";
            AppendTxLog($"TX FAIL: {ex.Message}");
        }
        finally
        {
            _playback.Stop();
            await _cat.SetPttAsync(false, CatPttEnabled);
            IsTransmitting = false;
            // Keep DRV meter showing the slider so it still feels adjustable when idle.
            ApplyDriveToAlcMeter();
            if (promptLogAfterTx)
                PromptLogQso("73 sent — review QSO");
        }
    }

    /// <summary>
    /// Keep PTT/audio for at most <paramref name="holdSec"/>, but unkey immediately if Halt
    /// is pressed or the FT8/FT4 period rolls into the next sequence.
    /// </summary>
    async Task HoldTxUntilAsync(double holdSec, int startSlotIndex)
    {
        var endUtc = DateTime.UtcNow.AddSeconds(Math.Max(0.05, holdSec));
        while (IsTransmitting && DateTime.UtcNow < endUtc)
        {
            _timing.Tick(DateTime.UtcNow);
            if (_timing.SlotIndex != startSlotIndex)
                break;
            double remainMs = (endUtc - DateTime.UtcNow).TotalMilliseconds;
            if (remainMs <= 0) break;
            await Task.Delay((int)Math.Min(40, remainMs));
        }
    }

    static bool IsSeventyThreeMessage(string message)
    {
        var parts = message.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        string last = parts[^1].Trim().ToUpperInvariant();
        return last is "73" or "RR73";
    }

    /// <summary>
    /// Keep the radio in DATA-U / PKTUSB / DIGU. Plain USB/LSB breaks USB-audio digi on many rigs
    /// and is what band/VFO changes often fall back to.
    /// </summary>
    async Task<string> EnsureDigiModeAsync()
    {
        if (!_cat.IsConnected)
            return LooksDigiMode(HamlibMode) ? HamlibMode : "DATA-U";

        _lastDigiModeForceUtc = DateTime.UtcNow;
        var digiFirst = new List<string>();
        if (LooksDigiMode(HamlibMode))
            digiFirst.Add(HamlibMode.Trim());
        digiFirst.AddRange(["DATA-U", "PKTUSB", "DIGU", "USB-D", "DATA-USB", "USB-DATA"]);

        foreach (var mode in digiFirst.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                await _cat.SetModeAsync(mode);
                HamlibMode = mode;
                return mode;
            }
            catch
            {
                // try next label — HRD/rig naming varies
            }
        }

        return LooksDigiMode(HamlibMode) ? HamlibMode : "DATA-U";
    }

    static void AppendTxLog(string line)
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SignalDx");
            Directory.CreateDirectory(dir);
            File.AppendAllText(
                Path.Combine(dir, "tx-log.txt"),
                $"{DateTime.UtcNow:HH:mm:ss.fff}Z {line}{Environment.NewLine}");
        }
        catch
        {
            // ignore
        }
    }

    async Task HaltTxAsync()
    {
        if (IsTuning)
        {
            await StopTuneAsync("Tune halted.");
            return;
        }

        _playback.Stop();
        await _cat.SetPttAsync(false, CatPttEnabled);
        IsTransmitting = false;
        Status = "TX halted.";
    }

    async Task LookupQrzAsync(string? callsign, bool openBrowser)
    {
        string call = QrzLookupClient.NormalizeCall(callsign);
        if (string.IsNullOrEmpty(call))
        {
            QrzInfoText = "";
            return;
        }

        if (openBrowser)
            OpenQrzInBrowser(call);

        int seq = ++_qrzLookupSeq;
        QrzInfoText = $"QRZ… {call}";
        var (ok, info, msg) = await _qrzLookup.LookupAsync(call, QrzUsername, QrzPassword);
        if (seq != _qrzLookupSeq) return;

        if (ok && info is not null)
        {
            QrzInfoText = info.Summary;
            if (QrzFillGrid && string.IsNullOrWhiteSpace(DxGrid) && !string.IsNullOrWhiteSpace(info.Grid))
                DxGrid = info.Grid!;
            Status = $"QRZ: {info.Summary}";
        }
        else
        {
            QrzInfoText = string.IsNullOrWhiteSpace(msg) ? $"QRZ: {call}" : msg;
            if (msg.Contains("username/password", StringComparison.OrdinalIgnoreCase))
                Status = "Set QRZ username/password in Settings → Logbooks for call lookup";
        }
    }

    static void OpenQrzInBrowser(string? callsign)
    {
        string url = QrzLookupClient.BrowserUrl(callsign ?? "");
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // ignore
        }
    }

    void PromptLogQso(string reason, bool force = false)
    {
        if (string.IsNullOrWhiteSpace(DxCall)) return;
        // Claim immediately so a reciprocal 73 cannot queue a second dialog.
        if (!force)
        {
            if (_qsoLogPrompted || _logPromptOpen) return;
            _qsoLogPrompted = true;
        }

        // Defer so an in-progress TX arm/key from the same decode can start before the modal.
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            _ = PromptLogQsoAsync(reason, force);
            return;
        }

        dispatcher.BeginInvoke(
            new Action(() => _ = PromptLogQsoAsync(reason, force)),
            DispatcherPriority.ApplicationIdle);
    }

    async Task PromptLogQsoAsync(string reason, bool force = false)
    {
        if (_logPromptOpen || string.IsNullOrWhiteSpace(DxCall)) return;
        if (!force && !_qsoLogPrompted)
            _qsoLogPrompted = true;
        _logPromptOpen = true;
        try
        {
            var entry = BuildQsoDraft();
            var owner = Application.Current.MainWindow;
            var win = new LogQsoWindow(entry)
            {
                Owner = owner,
                Title = $"Log QSO — {entry.DxCall}"
            };
            Status = reason;
            bool? ok = win.ShowDialog();
            if (ok == true)
                await CommitQsoAsync(win.Entry);
            else
            {
                Status = $"Skipped log for {entry.DxCall}";
                FinishQsoSession();
            }
        }
        finally
        {
            _logPromptOpen = false;
        }
    }

    QsoEntry BuildQsoDraft()
    {
        string dxGrid = DxGrid.Trim().ToUpperInvariant();
        string comment = "";
        if (Maidenhead.DistanceMiles(MyGrid, dxGrid) is double mi)
            comment = $"Distance: {Math.Round(mi)} mi";

        return new QsoEntry
        {
            UtcStart = _qsoStartUtc ?? DateTime.UtcNow.AddSeconds(-30),
            UtcEnd = DateTime.UtcNow,
            Mode = Mode,
            Band = Band,
            DialMhz = DialMhz,
            MyCall = MyCall,
            MyGrid = MyGrid,
            DxCall = DxCall.Trim().ToUpperInvariant(),
            DxGrid = dxGrid,
            SentRst = Report,
            RcvdRst = Report,
            Comment = comment
        };
    }

    async Task LogSelectedQsoAsync()
    {
        if (string.IsNullOrWhiteSpace(DxCall)) return;
        // Manual Log QSO button — always show the review dialog.
        PromptLogQso("Log QSO", force: true);
        await Task.CompletedTask;
    }

    async Task CommitQsoAsync(QsoEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.DxCall)) return;
        entry.DxCall = entry.DxCall.Trim().ToUpperInvariant();
        entry.DxGrid = entry.DxGrid?.Trim().ToUpperInvariant() ?? "";
        entry.MyCall = entry.MyCall?.Trim().ToUpperInvariant() ?? MyCall;
        entry.MyGrid = entry.MyGrid?.Trim().ToUpperInvariant() ?? MyGrid;
        entry.UtcEnd ??= DateTime.UtcNow;

        _logStore.Add(entry);
        Log.Insert(0, entry);
        _workedCalls.Add(entry.DxCall);
        RefreshWorkedFlagsInActivity();
        await _external.PublishAsync(entry, _settings);
        FinishQsoSession();
        Status = $"Logged {entry.DxCall}";
    }

    void RefreshWorkedFlagsInActivity()
    {
        foreach (var hit in Activity)
            hit.IsPreviouslyWorked = _workedCalls.Contains(hit.DxCall);
        foreach (var hit in RxActivity)
            hit.IsPreviouslyWorked = _workedCalls.Contains(hit.DxCall);
    }

    void FinishQsoSession()
    {
        _qsoStartUtc = null;
        // Keep _qsoLogPrompted set so a reciprocal 73 does not reopen the dialog.
        // Cleared only when a new QSO / CQ / DX selection starts.
        if (_seq.Phase is QsoPhase.Complete or QsoPhase.Sent73 or QsoPhase.SendingSeventyThree)
        {
            _seq.Reset();
            PhaseLabel = "Idle";
        }
    }

    void ExportAdif()
    {
        var dlg = new SaveFileDialog
        {
            Filter = "ADIF (*.adi)|*.adi|All files (*.*)|*.*",
            FileName = "signal-dx-export.adi",
            AddExtension = true,
            DefaultExt = "adi"
        };
        if (dlg.ShowDialog() != true) return;
        AdifExporter.WriteAll(dlg.FileName, Log);
        Status = $"Exported ADIF → {dlg.FileName}";
    }

    async Task SaveSettingsAsync()
    {
        _settingsStore.Save(_settings);
        _external.Configure(_settings);
        if (_settings.CatBackend != CatBackend.None)
            await ConnectCatAsync();
        Status = "Settings saved.";
    }

    static string FormatSnr(double snr)
    {
        int v = (int)Math.Round(snr);
        return v >= 0 ? $"+{v:00}" : $"{v:00}";
    }

    public void SaveSettingsNow()
    {
        _settingsStore.Save(_settings);
    }

    public void CaptureWindowPlacement(double left, double top, double width, double height, bool maximized)
    {
        _settings.WindowLeft = left;
        _settings.WindowTop = top;
        _settings.WindowWidth = width;
        _settings.WindowHeight = height;
        _settings.WindowMaximized = maximized;
    }

    public bool TryGetWindowPlacement(
        out double left, out double top, out double width, out double height, out bool maximized)
    {
        left = _settings.WindowLeft ?? double.NaN;
        top = _settings.WindowTop ?? double.NaN;
        width = _settings.WindowWidth;
        height = _settings.WindowHeight;
        maximized = _settings.WindowMaximized;
        return width >= 400 && height >= 300;
    }

    public void CapturePaneLayout(
        double waterfall, double activity, double log,
        double band, double rx, double macros)
    {
        _settings.LayoutWaterfallShare = waterfall;
        _settings.LayoutActivityShare = activity;
        _settings.LayoutLogShare = log;
        _settings.LayoutBandShare = band;
        _settings.LayoutRxShare = rx;
        _settings.LayoutMacroShare = macros;
    }

    public bool TryGetPaneLayout(
        out double waterfall, out double activity, out double log,
        out double band, out double rx, out double macros)
    {
        waterfall = _settings.LayoutWaterfallShare;
        activity = _settings.LayoutActivityShare;
        log = _settings.LayoutLogShare;
        band = _settings.LayoutBandShare;
        rx = _settings.LayoutRxShare;
        macros = _settings.LayoutMacroShare;

        // Log pane removed — accept old 3-share or new 2-share vertical layouts.
        bool verticalOk =
            waterfall > 0.02 && activity > 0.02 &&
            (log <= 0.001
                ? Math.Abs(waterfall + activity - 1.0) < 0.25
                : log > 0.02 && Math.Abs(waterfall + activity + log - 1.0) < 0.25);

        return verticalOk &&
               band > 0.02 && rx > 0.02 && macros > 0.02 &&
               Math.Abs(band + rx + macros - 1.0) < 0.25;
    }

    public void Dispose()
    {
        try { _tuneCts?.Cancel(); } catch { /* ignore */ }
        try { _tuneCts?.Dispose(); } catch { /* ignore */ }
        _tuneCts = null;
        _clock.Stop();
        _spectrumTimer.Stop();
        _heartbeat.Stop();
        _settingsPersistTimer.Stop();
        RememberAudioHzForMode(IsWsprMode);
        // Always flush markers / mode memory — Closing may be skipped on force-kill rebuilds.
        try { SaveSettingsNow(); } catch { /* ignore */ }
        _jtdx.Dispose();
        _capture.Dispose();
        _playback.Dispose();
        _callAlert.Dispose();
        _external.Dispose();
        _ = _cat.DisposeAsync().AsTask();
    }
}
