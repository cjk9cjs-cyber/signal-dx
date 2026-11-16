using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
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
using SignalDx.Logging;
using SignalDx.Models;
using SignalDx.Services;

namespace SignalDx.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    readonly SettingsStore _settingsStore = new();
    readonly QsoLogStore _logStore = new();
    readonly AudioCaptureService _capture = new();
    readonly AudioPlaybackService _playback = new();
    readonly SpectrumAnalyzer _spectrum = new(2048);
    readonly SlotTiming _timing = new();
    readonly SimulatedDecoder _sim = new();
    readonly JtdxDecoderService _jtdx = new();
    readonly AutoSequencer _seq = new();
    int _jtdxDecodeSeq;
    readonly CatService _cat = new();
    readonly ExternalLogRouter _external = new();
    readonly QrzLookupClient _qrzLookup = new();
    readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(100) };
    readonly DispatcherTimer _spectrumTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    readonly DispatcherTimer _heartbeat = new() { Interval = TimeSpan.FromSeconds(15) };

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
    string _slotLabel = "00:00";
    string _phaseLabel = "Idle";
    double _slotProgress;
    double _alcLevel;
    double _rxLevelNorm;
    bool _isTransmitting;
    bool _rigPtt;
    int _qrzLookupSeq;
    DecodeHit? _selectedActivityDecode;
    DecodeHit? _selectedRxDecode;
    WaterfallControl? _waterfall;
    DateTime? _qsoStartUtc;
    int _selectedHamlibModelIndex;
    string _hamlibModelFilter = "";
    string _hamlibModelCountText = "";
    List<HamlibModel> _hamlibModels = [];

    public MainViewModel()
    {
        _settings = _settingsStore.Load();
        if (string.IsNullOrWhiteSpace(_settings.AdifPath))
            _settings.AdifPath = AdifExporter.DefaultPath;
        if (_settings.InputDeviceIndex < 0)
            _settings.InputDeviceIndex = 0;
        PreferJtdxIfAvailable();

        foreach (var e in _logStore.Entries)
            Log.Add(e);

        foreach (var m in Ft8MessageBuilder.DefaultMacros)
            Macros.Add(m);

        InputDevices = new ObservableCollection<string>(
            AudioCaptureService.ListDevices().Select(d => d.Name));
        OutputDevices = new ObservableCollection<string>(
            AudioPlaybackService.ListDevices().Select(d => d.Name));
        ComPorts = new ObservableCollection<string>();
        CatBackends = new ObservableCollection<string>(Enum.GetNames<CatBackend>());
        HamlibModes = new ObservableCollection<string>(["USB", "LSB", "PKTUSB", "PKTLSB", "FM", "AM", "CW"]);
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
            RebuildRxFilter();
        });
        ClearRxActivityCommand = new RelayCommand(() =>
        {
            SelectedRxDecode = null;
            RxActivity.Clear();
        });
        SendMacroCommand = new RelayCommand(p => SendMacro(p as TxMacro), _ => EnableTx && !_isTransmitting);
        HaltTxCommand = new RelayCommand(() => _ = HaltTxAsync());
        SaveSettingsCommand = new RelayCommand(() => _ = SaveSettingsAsync());
        OpenSettingsCommand = new RelayCommand(OpenSettings);
        ConnectCatCommand = new RelayCommand(() => _ = ConnectCatAsync());
        DisconnectCatCommand = new RelayCommand(() => _ = DisconnectCatAsync());
        ApplyBandCommand = new RelayCommand(ApplySelectedBand);
        ExportAdifCommand = new RelayCommand(ExportAdif);
        StartCqCommand = new RelayCommand(StartCq);
        ResetSeqCommand = new RelayCommand(() => { _seq.Reset(); PhaseLabel = "Idle"; });
        RefreshPortsCommand = new RelayCommand(RefreshPorts);
        RefreshHamlibModelsCommand = new RelayCommand(() => ReloadHamlibModels(forceRefresh: true));
        StartRigctldCommand = new RelayCommand(StartRigctld);
        StopRigctldCommand = new RelayCommand(() => _cat.StopRigctld());
        SetRigFreqCommand = new RelayCommand(() => _ = SetRigFreqAsync());
        SetRigModeCommand = new RelayCommand(() => _ = SetRigModeAsync());
        TogglePttCommand = new RelayCommand(() => _ = TogglePttAsync());
        RefreshRigCommand = new RelayCommand(() => _ = _cat.RefreshAsync());
        FindRigctldCommand = new RelayCommand(FindRigctld);
        FindJtdxCommand = new RelayCommand(FindJtdx);
        LookupQrzCommand = new RelayCommand(
            () => _ = LookupQrzAsync(DxCall, openBrowser: false),
            () => !string.IsNullOrWhiteSpace(DxCall));
        OpenQrzCommand = new RelayCommand(
            () => OpenQrzInBrowser(DxCall),
            () => !string.IsNullOrWhiteSpace(DxCall));

        _cat.StatusChanged += s => Application.Current.Dispatcher.BeginInvoke(() => CatStatus = s);
        _cat.FrequencyChanged += hz => Application.Current.Dispatcher.BeginInvoke(() =>
        {
            DialMhz = hz / 1_000_000d;
            RigFreqText = $"{DialMhz:0.000000} MHz";
        });
        _cat.SnapshotChanged += snap => Application.Current.Dispatcher.BeginInvoke(() => ApplySnapshot(snap));
        _external.StatusChanged += s => Application.Current.Dispatcher.BeginInvoke(() => Status = s);

        _timing.SetMode(Mode);
        _external.Configure(_settings);

        _clock.Tick += OnClock;
        _spectrumTimer.Tick += OnSpectrumTick;
        _heartbeat.Tick += (_, _) => _external.Heartbeat(_settings);
        _clock.Start();
        _spectrumTimer.Start();
        _heartbeat.Start();

        PhaseLabel = _seq.Phase.ToString();
        UtcClockText = DateTime.UtcNow.ToString("HH:mm:ss");
        RefreshCurrentFreqText();
        Raise(nameof(TxEven));
        Raise(nameof(AutoSequence));
        Raise(nameof(Mode));

        StartMonitor();
        if (!_capture.IsRunning)
            Status = "Set call/grid, connect CAT if used, start monitor, enable TX when ready.";
    }

    public ObservableCollection<DecodeHit> Activity { get; } = [];
    public ObservableCollection<DecodeHit> RxActivity { get; } = [];
    public ObservableCollection<QsoEntry> Log { get; } = [];
    public ObservableCollection<TxMacro> Macros { get; } = [];
    public ObservableCollection<string> InputDevices { get; }
    public ObservableCollection<string> OutputDevices { get; }
    public ObservableCollection<string> ComPorts { get; }
    public ObservableCollection<string> CatBackends { get; }
    public ObservableCollection<string> BandPresets { get; } = [];
    public ObservableCollection<string> Modes { get; } = ["FT8", "FT4"];
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
    public ICommand SaveSettingsCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand ConnectCatCommand { get; }
    public ICommand DisconnectCatCommand { get; }
    public ICommand ApplyBandCommand { get; }
    public ICommand ExportAdifCommand { get; }
    public ICommand StartCqCommand { get; }
    public ICommand ResetSeqCommand { get; }
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

    public string MyCall
    {
        get => _settings.MyCall;
        set { _settings.MyCall = value; Raise(); }
    }

    public string MyGrid
    {
        get => _settings.MyGrid;
        set { _settings.MyGrid = value; Raise(); }
    }

    public string Band
    {
        get => _settings.Band;
        set { _settings.Band = value; Raise(); }
    }

    public string Mode
    {
        get => _settings.Mode;
        set
        {
            _settings.Mode = value;
            _timing.SetMode(value);
            if (_waterfall is not null)
                _waterfall.ChannelWidthHz = value.Equals("FT4", StringComparison.OrdinalIgnoreCase) ? 90 : 50;
            Raise();
            RebuildBandPresetList();
        }
    }

    public double DialMhz
    {
        get => _settings.DialMhz;
        set
        {
            _settings.DialMhz = value;
            string band = BandPlan.BandFromMhz(value);
            if (!string.Equals(_settings.Band, band, StringComparison.OrdinalIgnoreCase))
            {
                _settings.Band = band;
                Raise(nameof(Band));
            }
            Raise();
            RefreshCurrentFreqText();
        }
    }

    public int TxHz
    {
        get => _settings.TxHz;
        set
        {
            _settings.TxHz = value;
            Raise();
            if (_waterfall is not null) _waterfall.TxHz = value;
            RebuildRxFilter();
            RefreshCurrentFreqText();
        }
    }

    public int RxHz
    {
        get => _settings.RxHz;
        set
        {
            _settings.RxHz = value;
            Raise();
            if (_waterfall is not null) _waterfall.RxHz = value;
            RebuildRxFilter();
            RefreshCurrentFreqText();
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
            Raise();
            _ = PushRfPowerAsync();
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
            // Live feedback on the ALC bar while transmitting (CAT ALC may be unavailable).
            if (_isTransmitting)
                ApplyDriveToAlcMeter();
        }
    }

    public string TxDriveLabel => $"Drive {_settings.TxDrivePercent}%";

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
        set => SetField(ref _dxCall, value);
    }

    public string DxGrid
    {
        get => _dxGrid;
        set => SetField(ref _dxGrid, value);
    }

    public string Report
    {
        get => _report;
        set => SetField(ref _report, value);
    }

    public string FreeText
    {
        get => _freeText;
        set => SetField(ref _freeText, value);
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
        if (!string.IsNullOrWhiteSpace(value.DxCall) && value.IsCq)
            DxCall = value.DxCall!;
        else if (!string.IsNullOrWhiteSpace(value.DxCall) &&
                 !value.DxCall!.Equals(MyCall, StringComparison.OrdinalIgnoreCase))
            DxCall = value.DxCall;
        if (!string.IsNullOrWhiteSpace(value.DxGrid))
            DxGrid = value.DxGrid!;
        RxHz = value.Hz;
        TxHz = value.Hz;
        Report = FormatSnr(value.SnrDb);
        _qsoStartUtc = DateTime.UtcNow;
        if (AutoSequence && !string.IsNullOrWhiteSpace(DxCall))
        {
            _seq.StartWorking(DxCall, DxGrid, Report);
            PhaseLabel = _seq.Phase.ToString();
            FreeText = _seq.NextTxMessage(_settings) ?? FreeText;
        }
        if (QrzLookupOnSelect && !string.IsNullOrWhiteSpace(DxCall))
            _ = LookupQrzAsync(DxCall, openBrowser: false);
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

        // Ensure we have a TX message even if auto-seq was off.
        if (string.IsNullOrWhiteSpace(FreeText) || AutoSequence)
        {
            _seq.StartWorking(DxCall, DxGrid, Report);
            PhaseLabel = _seq.Phase.ToString();
            FreeText = _seq.NextTxMessage(_settings) ?? FreeText;
        }

        EnableTx = true;
        Status = $"TX armed → {DxCall} @ {TxHz} Hz  ({FreeText})";
    }

    public int SelectedInputIndex
    {
        get => Math.Max(0, _settings.InputDeviceIndex);
        set { _settings.InputDeviceIndex = value; Raise(); }
    }

    public int SelectedOutputIndex
    {
        get => Math.Max(0, _settings.OutputDeviceIndex);
        set { _settings.OutputDeviceIndex = value; Raise(); }
    }

    int _selectedBandPresetIndex;
    List<BandPreset> _bandPresetItems = [];

    public int SelectedBandPresetIndex
    {
        get => _selectedBandPresetIndex;
        set
        {
            if (_selectedBandPresetIndex == value) return;
            _selectedBandPresetIndex = value;
            Raise();
            if (value >= 0)
                ApplySelectedBand();
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
        set { _settings.HamlibMode = value; Raise(); }
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
        _waterfall.ChannelWidthHz = Mode.Equals("FT4", StringComparison.OrdinalIgnoreCase) ? 90 : 50;
        _waterfall.SampleRate = AudioCaptureService.SampleRate;
        _waterfall.FftSize = _spectrum.FftSize;
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
            // Refresh device list — WASAPI indices can change after sleep / cable changes.
            var devices = AudioCaptureService.ListDevices();
            InputDevices.Clear();
            foreach (var d in devices)
                InputDevices.Add(d.Name);

            int idx = InputDevices.Count == 0 ? -1 : Math.Min(SelectedInputIndex, InputDevices.Count - 1);
            if (idx < 0 || InputDevices.Count == 0)
            {
                Status = "No capture device — spectrum idle / sim only.";
                return;
            }

            SelectedInputIndex = idx;
            _capture.Start(idx);
            Status = $"Monitoring: {InputDevices[idx]}";
            AppendDecodeLog($"Monitor start idx={idx} device={InputDevices[idx]}");
        }
        catch (Exception ex)
        {
            Status = $"Monitor failed: {ex.Message}";
            AppendDecodeLog($"Monitor failed: {ex}");
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
            await _cat.RefreshAsync();
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
            Status = $"Set rig {DialMhz:0.000000} MHz";
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
            await _cat.SetModeAsync(HamlibMode);
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

    void ApplySnapshot(RigSnapshot snap)
    {
        if (snap.FrequencyHz is long hz)
        {
            DialMhz = hz / 1_000_000d;
            RigFreqText = $"{DialMhz:0.000000} MHz";
            RefreshCurrentFreqText();
        }
        if (!string.IsNullOrWhiteSpace(snap.Mode))
        {
            RigModeText = snap.PassbandHz is > 0 ? $"{snap.Mode} {snap.PassbandHz}Hz" : snap.Mode;
            if (HamlibModes.Contains(snap.Mode))
                HamlibMode = snap.Mode;
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
        else if (_isTransmitting)
        {
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
                p.Primary && p.Mode.Equals(mode, StringComparison.OrdinalIgnoreCase) &&
                p.Band.Equals(Band, StringComparison.OrdinalIgnoreCase));
        _selectedBandPresetIndex = match >= 0 ? match : 0;
        Raise(nameof(SelectedBandPresetIndex));
    }

    void ApplySelectedBand()
    {
        if (_selectedBandPresetIndex < 0 || _selectedBandPresetIndex >= _bandPresetItems.Count)
            return;
        var p = _bandPresetItems[_selectedBandPresetIndex];
        // Set fields without recursive RebuildBandPresetList from Mode setter mid-apply.
        _settings.Mode = p.Mode;
        _timing.SetMode(p.Mode);
        if (_waterfall is not null)
            _waterfall.ChannelWidthHz = p.Mode.Equals("FT4", StringComparison.OrdinalIgnoreCase) ? 90 : 50;
        Raise(nameof(Mode));
        Band = p.Band;
        DialMhz = p.DialMhz;
        if (_settings.CatFollowDial && _cat.IsConnected)
            _ = _cat.SetDialMhzAsync(DialMhz);
        Status = $"Band {p.Band} {p.Mode} @ {p.DialMhz:0.0000} MHz";
    }

    void StartCq()
    {
        _seq.StartCq();
        PhaseLabel = _seq.Phase.ToString();
        FreeText = _seq.NextTxMessage(_settings) ?? FreeText;
        DxCall = "";
        if (EnableTx)
            _ = TransmitMessageAsync(FreeText);
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
            TriggerSlotDecode();

        if (_timing.JustRolled && EnableTx && !_isTransmitting)
        {
            bool ourSlot = TxEven == _timing.IsEvenSlot;
            if (!ourSlot) return;

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
    }

    void OnSpectrumTick(object? sender, EventArgs e)
    {
        var frame = _capture.GetLatestFrame();
        UpdateRxAudioLevel(frame);

        if (_waterfall is null) return;

        if (frame.Length == 0)
        {
            var noise = new float[2048];
            var rng = Random.Shared;
            for (int i = 0; i < noise.Length; i++)
                noise[i] = (float)((rng.NextDouble() - 0.5) * 0.002);
            _spectrum.Analyze(noise);
        }
        else
        {
            _spectrum.Analyze(frame.Length >= 2048 ? frame.AsSpan(0, 2048) : frame);
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
        if (string.IsNullOrWhiteSpace(_settings.JtdxJt9Path))
        {
            string? found = JtdxPaths.FindJt9();
            if (!string.IsNullOrWhiteSpace(found))
            {
                _settings.JtdxJt9Path = found;
                _settings.DecoderEngine = DecoderEngine.Jtdx;
                _settings.SimulateDecodes = false;
            }
        }
        else if (_settings.DecoderEngine == DecoderEngine.Simulate &&
                 _jtdx.IsAvailable(_settings.JtdxJt9Path) &&
                 _settings.SimulateDecodes)
        {
            // Path already saved from a prior find — leave Simulate as the user's last choice.
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
                return;
            }
            _ = DecodeWithJtdxAsync();
            return;
        }

        if (SimulateDecodes || _settings.DecoderEngine == DecoderEngine.Simulate ||
            !_jtdx.IsAvailable(_settings.JtdxJt9Path))
            InjectSimulatedDecodes();
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
        }
        catch (OperationCanceledException)
        {
            // disposed / shutdown
        }
        catch (Exception ex)
        {
            if (seq != _jtdxDecodeSeq) return;
            AppendDecodeLog($"error: {ex}");
            Status = $"JTDX decode failed: {ex.Message}";
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
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            foreach (var hit in hits)
            {
                Activity.Insert(0, hit);
                if (Activity.Count > 500)
                    Activity.RemoveAt(Activity.Count - 1);

                if (AutoSequence && _seq.TryAdvanceFromDecode(hit.Message, _settings))
                {
                    PhaseLabel = _seq.Phase.ToString();
                    if (!string.IsNullOrWhiteSpace(_seq.DxCall))
                        DxCall = _seq.DxCall;
                    if (!string.IsNullOrWhiteSpace(_seq.DxGrid))
                        DxGrid = _seq.DxGrid;
                    FreeText = _seq.NextTxMessage(_settings) ?? FreeText;
                    if (_seq.Phase == QsoPhase.Complete)
                        _ = LogSelectedQsoAsync();
                }
            }

            RebuildRxFilter();
            Status = $"Decoded {hits.Count} ({engineLabel}) @ {utc:HH:mm:ss} UTC";
        });
    }

    void RebuildRxFilter()
    {
        // Clear selection first — clearing ItemsSource while SelectedItem is set can crash WPF DataGrid automation.
        DecodeHit? keep = _selectedRxDecode;
        SelectedRxDecode = null;
        RxActivity.Clear();
        foreach (var h in Activity.Where(a => Math.Abs(a.Hz - RxHz) <= 50).Take(80))
            RxActivity.Add(h);
        if (keep is not null && RxActivity.Contains(keep))
            _selectedRxDecode = keep; // restore quietly without re-running ApplySelectedDecode
        Raise(nameof(SelectedRxDecode));
    }

    void SendMacro(TxMacro? macro)
    {
        if (macro is null || !EnableTx) return;
        string msg = macro.Kind == TxMacroKind.FreeText && !string.IsNullOrWhiteSpace(FreeText)
            ? FreeText
            : Ft8MessageBuilder.Build(macro.Template, _settings, DxCall, Report);
        FreeText = msg;
        _ = TransmitMessageAsync(msg);
    }

    async Task TransmitMessageAsync(string message)
    {
        if (_isTransmitting) return;
        _isTransmitting = true;
        _qsoStartUtc ??= DateTime.UtcNow;
        try
        {
            await PushRfPowerAsync();
            await _cat.SetPttAsync(true, CatPttEnabled);
            double seconds = Mode.Equals("FT4", StringComparison.OrdinalIgnoreCase) ? 6.0 : 12.64;
            // Generate near full-scale; PlayMono applies Drive as the ALC audio level.
            float drive = (float)Math.Clamp(TxDrivePercent / 100.0, 0, 1);
            var audio = Ft8ToneGenerator.Generate(message, TxHz, seconds, drive01: 1.0);
            int outIdx = OutputDevices.Count == 0 ? -1 : SelectedOutputIndex;
            ApplyDriveToAlcMeter();
            _playback.PlayMono(audio, AudioCaptureService.SampleRate, outIdx, volume01: drive);
            Status = $"TX: {message} @ {TxHz} Hz  drive {_settings.TxDrivePercent}%";
            int holdMs = (int)(seconds * 1000) + 400;
            await Task.Delay(holdMs);
        }
        catch (Exception ex)
        {
            Status = $"TX failed: {ex.Message}";
        }
        finally
        {
            _playback.Stop();
            await _cat.SetPttAsync(false, CatPttEnabled);
            _isTransmitting = false;
            if (AlcText.StartsWith("DRV", StringComparison.Ordinal))
            {
                AlcText = "ALC —";
                AlcLevel = 0;
            }
        }
    }

    async Task HaltTxAsync()
    {
        _playback.Stop();
        await _cat.SetPttAsync(false, CatPttEnabled);
        _isTransmitting = false;
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

    async Task LogSelectedQsoAsync()
    {
        if (string.IsNullOrWhiteSpace(DxCall)) return;
        var entry = new QsoEntry
        {
            UtcStart = _qsoStartUtc ?? DateTime.UtcNow.AddSeconds(-30),
            UtcEnd = DateTime.UtcNow,
            Mode = Mode,
            Band = Band,
            DialMhz = DialMhz,
            MyCall = MyCall,
            MyGrid = MyGrid,
            DxCall = DxCall.Trim().ToUpperInvariant(),
            DxGrid = DxGrid.Trim().ToUpperInvariant(),
            SentRst = Report,
            RcvdRst = Report,
            Comment = FreeText
        };
        _logStore.Add(entry);
        Log.Insert(0, entry);
        await _external.PublishAsync(entry, _settings);
        _qsoStartUtc = null;
        if (_seq.Phase == QsoPhase.Complete)
        {
            _seq.Reset();
            PhaseLabel = "Idle";
        }
        Status = $"Logged {entry.DxCall}";
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

    public void Dispose()
    {
        _clock.Stop();
        _spectrumTimer.Stop();
        _heartbeat.Stop();
        _jtdx.Dispose();
        _capture.Dispose();
        _playback.Dispose();
        _external.Dispose();
        _ = _cat.DisposeAsync().AsTask();
        _settingsStore.Save(_settings);
    }
}
