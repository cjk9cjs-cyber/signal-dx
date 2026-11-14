using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using SignalDx.Audio;
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
    readonly Ft8Timing _timing = new();
    readonly SimulatedDecoder _sim = new();
    readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(100) };
    readonly DispatcherTimer _spectrumTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };

    StationSettings _settings;
    string _dxCall = "";
    string _dxGrid = "";
    string _report = "-10";
    string _status = "Ready";
    string _freeText = "";
    string _slotLabel = "00:00";
    double _slotProgress;
    bool _txEven = true;
    bool _isTransmitting;
    DecodeHit? _selectedDecode;
    WaterfallControl? _waterfall;

    public MainViewModel()
    {
        _settings = _settingsStore.Load();
        foreach (var e in _logStore.Entries)
            Log.Add(e);

        foreach (var m in Ft8MessageBuilder.DefaultMacros)
            Macros.Add(m);

        InputDevices = new ObservableCollection<string>(
            AudioCaptureService.ListDevices().Select(d => d.Name));
        OutputDevices = new ObservableCollection<string>(
            AudioPlaybackService.ListDevices().Select(d => d.Name));

        StartMonitorCommand = new RelayCommand(StartMonitor);
        StopMonitorCommand = new RelayCommand(StopMonitor);
        EnableTxCommand = new RelayCommand(() => EnableTx = !EnableTx);
        LogQsoCommand = new RelayCommand(LogSelectedQso, () => !string.IsNullOrWhiteSpace(DxCall));
        ClearActivityCommand = new RelayCommand(() => Activity.Clear());
        SendMacroCommand = new RelayCommand(p => SendMacro(p as TxMacro), _ => EnableTx && !_isTransmitting);
        HaltTxCommand = new RelayCommand(() => _playback.Stop());
        SaveSettingsCommand = new RelayCommand(SaveSettings);

        _clock.Tick += OnClock;
        _spectrumTimer.Tick += OnSpectrumTick;
        _clock.Start();
        _spectrumTimer.Start();

        Status = InputDevices.Count == 0
            ? "No input devices found — waterfall will stay idle until one is available."
            : "Set call/grid, start monitor, enable TX when ready.";
    }

    public ObservableCollection<DecodeHit> Activity { get; } = [];
    public ObservableCollection<QsoEntry> Log { get; } = [];
    public ObservableCollection<TxMacro> Macros { get; } = [];
    public ObservableCollection<string> InputDevices { get; }
    public ObservableCollection<string> OutputDevices { get; }

    public ICommand StartMonitorCommand { get; }
    public ICommand StopMonitorCommand { get; }
    public ICommand EnableTxCommand { get; }
    public ICommand LogQsoCommand { get; }
    public ICommand ClearActivityCommand { get; }
    public ICommand SendMacroCommand { get; }
    public ICommand HaltTxCommand { get; }
    public ICommand SaveSettingsCommand { get; }

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

    public double DialMhz
    {
        get => _settings.DialMhz;
        set { _settings.DialMhz = value; Raise(); }
    }

    public int TxHz
    {
        get => _settings.TxHz;
        set
        {
            _settings.TxHz = value;
            Raise();
            if (_waterfall is not null) _waterfall.TxHz = value;
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
        }
    }

    public bool EnableTx
    {
        get => _settings.EnableTx;
        set
        {
            _settings.EnableTx = value;
            Raise();
            Status = value ? "TX armed — next macro will transmit audio." : "TX disabled.";
        }
    }

    public bool SimulateDecodes
    {
        get => _settings.SimulateDecodes;
        set { _settings.SimulateDecodes = value; Raise(); }
    }

    public bool TxEven
    {
        get => _txEven;
        set => SetField(ref _txEven, value);
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

    public string SlotLabel
    {
        get => _slotLabel;
        set => SetField(ref _slotLabel, value);
    }

    public double SlotProgress
    {
        get => _slotProgress;
        set => SetField(ref _slotProgress, value);
    }

    public DecodeHit? SelectedDecode
    {
        get => _selectedDecode;
        set
        {
            SetField(ref _selectedDecode, value);
            if (value is null) return;
            if (!string.IsNullOrWhiteSpace(value.DxCall) && value.IsCq)
                DxCall = value.DxCall;
            else if (!string.IsNullOrWhiteSpace(value.DxCall) &&
                     !value.DxCall.Equals(MyCall, StringComparison.OrdinalIgnoreCase))
                DxCall = value.DxCall;
            if (!string.IsNullOrWhiteSpace(value.DxGrid))
                DxGrid = value.DxGrid;
            RxHz = value.Hz;
            TxHz = value.Hz;
            Report = FormatSnr(value.SnrDb);
        }
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

    public void AttachWaterfall(WaterfallControl waterfall)
    {
        _waterfall = waterfall;
        _waterfall.TxHz = TxHz;
        _waterfall.RxHz = RxHz;
        _waterfall.SampleRate = AudioCaptureService.SampleRate;
        _waterfall.FftSize = _spectrum.FftSize;
    }

    void StartMonitor()
    {
        try
        {
            int idx = InputDevices.Count == 0 ? -1 : SelectedInputIndex;
            if (idx < 0 || InputDevices.Count == 0)
            {
                Status = "No capture device — starting spectrum idle / sim only.";
                return;
            }

            _capture.Start(idx);
            Status = $"Monitoring: {InputDevices[idx]}";
        }
        catch (Exception ex)
        {
            Status = $"Monitor failed: {ex.Message}";
        }
    }

    void StopMonitor()
    {
        _capture.Stop();
        Status = "Monitor stopped.";
    }

    void OnClock(object? sender, EventArgs e)
    {
        _timing.Tick(DateTime.UtcNow);
        SlotProgress = _timing.Progress;
        SlotLabel = $"{_timing.UtcNow:HH:mm:ss}  slot {_timing.SecondsIntoSlot:00.0}s  {(_timing.IsEvenSlot ? "even" : "odd")}";

        if (_timing.JustRolled && SimulateDecodes)
            InjectSimulatedDecodes();

        if (_timing.JustRolled && EnableTx && !_isTransmitting && !string.IsNullOrWhiteSpace(FreeText))
        {
            bool ourSlot = TxEven == _timing.IsEvenSlot;
            if (ourSlot)
                TransmitMessage(FreeText);
        }
    }

    void OnSpectrumTick(object? sender, EventArgs e)
    {
        if (_waterfall is null) return;

        var frame = _capture.GetLatestFrame();
        if (frame.Length == 0)
        {
            // Idle noise so the waterfall still scrolls when no device is open.
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

    void InjectSimulatedDecodes()
    {
        var hits = _sim.GenerateSlot(_timing.UtcNow, _settings);
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            foreach (var hit in hits)
            {
                Activity.Insert(0, hit);
                if (Activity.Count > 500)
                    Activity.RemoveAt(Activity.Count - 1);
            }
            Status = $"Decoded {hits.Count} (sim) @ {_timing.UtcNow:HH:mm:ss} UTC";
        });
    }

    void SendMacro(TxMacro? macro)
    {
        if (macro is null || !EnableTx) return;
        string msg = macro.Kind == TxMacroKind.FreeText && !string.IsNullOrWhiteSpace(FreeText)
            ? FreeText
            : Ft8MessageBuilder.Build(macro.Template, _settings, DxCall, Report);
        FreeText = msg;
        TransmitMessage(msg);
    }

    void TransmitMessage(string message)
    {
        if (_isTransmitting) return;
        _isTransmitting = true;
        try
        {
            var audio = Ft8ToneGenerator.Generate(message, TxHz);
            int outIdx = OutputDevices.Count == 0 ? -1 : SelectedOutputIndex;
            _playback.PlayMono(audio, AudioCaptureService.SampleRate, outIdx);
            Status = $"TX: {message} @ {TxHz} Hz";
        }
        catch (Exception ex)
        {
            Status = $"TX failed: {ex.Message}";
        }
        finally
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(13000);
                _isTransmitting = false;
            });
        }
    }

    void LogSelectedQso()
    {
        if (string.IsNullOrWhiteSpace(DxCall)) return;
        var entry = new QsoEntry
        {
            UtcStart = DateTime.UtcNow,
            UtcEnd = DateTime.UtcNow,
            Mode = "FT8",
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
        Status = $"Logged {entry.DxCall}";
    }

    void SaveSettings()
    {
        _settingsStore.Save(_settings);
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
        _capture.Dispose();
        _playback.Dispose();
        _settingsStore.Save(_settings);
    }
}
