namespace SignalDx.Models;

public enum CatBackend
{
    None = 0,
    HamlibTcp = 1,
    SerialKenwood = 2,
    SerialYaesu = 3,
    HamRadioDeluxe = 4
}

public sealed class StationSettings
{
    public string MyCall { get; set; } = "W1ABC";
    public string MyGrid { get; set; } = "FN42";
    public string Mode { get; set; } = "FT8";
    public string Band { get; set; } = "20m";
    public double DialMhz { get; set; } = 14.074;
    public int TxHz { get; set; } = 1500;
    public int RxHz { get; set; } = 1500;
    /// <summary>FT8/FT4 Tx audio Hz remembered while in WSPR so markers restore on leave / restart.</summary>
    public int DigiTxHz { get; set; } = 1500;
    /// <summary>FT8/FT4 Rx audio Hz remembered while in WSPR so markers restore on leave / restart.</summary>
    public int DigiRxHz { get; set; } = 1500;
    /// <summary>WSPR Tx audio Hz remembered while in FT8/FT4.</summary>
    public int WsprAudioTxHz { get; set; } = 1500;
    /// <summary>WSPR Rx audio Hz remembered while in FT8/FT4.</summary>
    public int WsprAudioRxHz { get; set; } = 1500;
    /// <summary>True after Digi*/WsprAudio* were seeded so restart can restore mode-specific markers.</summary>
    public bool AudioHzMemoryInitialized { get; set; }
    public bool AutoSequence { get; set; } = true;
    public bool EnableTx { get; set; }
    /// <summary>Full path to jtdxjt9.exe (JTDX bin folder).</summary>
    public string JtdxJt9Path { get; set; } = "";
    /// <summary>jtdxjt9 -d depth (1–3).</summary>
    public int JtdxDecodeDepth { get; set; } = 3;
    public bool TxEven { get; set; } = true;
    public string TxPower { get; set; } = "50";
    /// <summary>TX audio drive 0–100% for ALC tuning (software level into the radio).</summary>
    public int TxDrivePercent { get; set; } = 40;
    public int InputDeviceIndex { get; set; } = -1;
    public int OutputDeviceIndex { get; set; } = -1;
    /// <summary>Preferred capture device friendly name (preferred over index when present).</summary>
    public string InputDeviceName { get; set; } = "";
    /// <summary>Preferred playback device friendly name (preferred over index when present).</summary>
    public string OutputDeviceName { get; set; } = "";
    /// <summary>WASAPI endpoint ID for capture — stable across reboots better than index.</summary>
    public string InputDeviceId { get; set; } = "";
    /// <summary>WASAPI endpoint ID for playback.</summary>
    public string OutputDeviceId { get; set; } = "";
    public string WaterfallPalette { get; set; } = "Classic";
    public bool WaterfallCallSigns { get; set; } = true;
    /// <summary>WSJT-X Gain. Higher packs the palette into fewer dB (more contrast).</summary>
    public int WaterfallGain { get; set; } = 20;
    /// <summary>WSJT-X Zero. Higher lifts the color floor so weaker traces disappear.</summary>
    public int WaterfallZero { get; set; }
    /// <summary>WSJT-X Flatten. Subtract a wide baseline so the noise floor is even.</summary>
    public bool WaterfallFlatten { get; set; }
    /// <summary>WSJT-X N Avg. Spectra averaged before a row is drawn.</summary>
    public int WaterfallAvg { get; set; } = 1;
    /// <summary>Waterfall scroll speed, rows per second.</summary>
    public int WaterfallRowsPerSecond { get; set; } = 25;
    /// <summary>Show the spectrum plot above the waterfall (WSJT-X Wide Graph spectrum).</summary>
    public bool ShowSpectrumPlot { get; set; } = true;
    /// <summary>Spectrum plot trace: Current, Cumulative or Linear Avg.</summary>
    public string SpectrumPlotMode { get; set; } = "Current";
    /// <summary>Spectrum plot smoothing, 1 = off.</summary>
    public int SpectrumPlotSmooth { get; set; } = 1;
    /// <summary>WSJT-X Bins/Pixel. FT8/FT4: 750 Hz per step (4 = 3000 Hz); 1 is the narrowest zoom.</summary>
    public int WaterfallBinsPerPixel { get; set; } = 4;
    /// <summary>Left edge of the FT8/FT4 waterfall, Hz.</summary>
    public int DigiWaterfallStartHz { get; set; } = 200;
    /// <summary>Left edge of the WSPR waterfall, Hz.</summary>
    public int WsprWaterfallStartHz { get; set; } = 1400;
    public bool LockTxToRx { get; set; }

    // CAT / Hamlib
    public CatBackend CatBackend { get; set; } = CatBackend.HamlibTcp;
    public string CatHost { get; set; } = "127.0.0.1";
    public int CatPort { get; set; } = 4532;
    public string CatComPort { get; set; } = "COM3";
    public int CatBaud { get; set; } = 9600;
    public bool CatPollEnabled { get; set; } = true;
    public bool CatPttEnabled { get; set; } = true;
    /// <summary>When true, Apply band / Set dial push DialMhz to the radio. Off by default so the VFO is source of truth.</summary>
    public bool CatFollowDial { get; set; }
    public int HamlibModelId { get; set; } = 2037;
    public string HamlibDevice { get; set; } = "COM3";
    public string HamlibRigctldPath { get; set; } = "";
    /// <summary>CAT mode for digi TX (Yaesu/Icom USB audio needs DATA-U / PKTUSB, not plain USB).</summary>
    public string HamlibMode { get; set; } = "DATA-U";
    public bool HamlibAutoStart { get; set; }
    public int HrdPort { get; set; } = 7809;

    // External logging
    public bool UdpLogEnabled { get; set; } = true;
    public string UdpLogHost { get; set; } = "127.0.0.1";
    public int UdpLogPort { get; set; } = 2237;
    /// <summary>Also send WSJT-X Status and Decode packets so companion apps (JTAlert-style) can follow the band.</summary>
    public bool UdpStatusDecodeEnabled { get; set; } = true;
    /// <summary>Accept WSJT-X Reply packets (double-click from a companion app) on loopback at this port.</summary>
    public bool UdpReplyEnabled { get; set; } = true;
    public int UdpReplyPort { get; set; } = 2238;
    /// <summary>Also send Heartbeat, Status and Decode packets to a companion app on loopback, leaving the logger port unchanged.</summary>
    public bool UdpCompanionEnabled { get; set; }
    public int UdpCompanionPort { get; set; } = 2237;
    public bool AdifAppendEnabled { get; set; } = true;
    public string AdifPath { get; set; } = "";
    public bool ClubLogEnabled { get; set; }
    public string ClubLogEmail { get; set; } = "";
    public string ClubLogPassword { get; set; } = "";
    public bool QrzEnabled { get; set; }
    public string QrzApiKey { get; set; } = "";
    /// <summary>QRZ.com account for XML callsign lookup (Logbooks tab).</summary>
    public string QrzUsername { get; set; } = "";
    public string QrzPassword { get; set; } = "";
    /// <summary>Sign each logged QSO with TrustedQSL and upload it to ARRL Logbook of the World.</summary>
    public bool LotwEnabled { get; set; }
    /// <summary>Path to tqsl.exe; empty auto-detects the default TrustedQSL install.</summary>
    public string LotwTqslPath { get; set; } = "";
    /// <summary>Station Location name as configured in TrustedQSL.</summary>
    public string LotwStationLocation { get; set; } = "";
    /// <summary>Play a sound when a decode is addressed to MyCall.</summary>
    public bool CallAlertEnabled { get; set; } = true;
    /// <summary>Optional custom WAV path; empty uses Assets/call-alert.wav beside the exe.</summary>
    public string CallAlertSoundPath { get; set; } = "";

    /// <summary>Play a sound when a Wanted call/state/country is decoded (CQ by default).</summary>
    public bool WantedAlertEnabled { get; set; } = true;
    /// <summary>When true, Wanted alerts only fire on CQ (calling out), not every decode.</summary>
    public bool WantedAlertCqOnly { get; set; } = true;
    /// <summary>Optional WAV for Wanted alerts; empty uses built-in Assets/wanted-alert.wav.</summary>
    public string WantedAlertSoundPath { get; set; } = "";
    /// <summary>True after Wanted defaults were applied once (older settings.json lacked these fields → bool false).</summary>
    public bool WantedFeatureInitialized { get; set; }

    /// <summary>Blocked callsigns / wildcards (K1ABC, K1*). Their decodes are hidden and never trigger alerts or auto-replies.</summary>
    public List<string> BlockedCalls { get; set; } = [];

    /// <summary>When true, check GitHub Releases for a newer version shortly after startup.</summary>
    public bool CheckForUpdatesOnStartup { get; set; } = true;

    /// <summary>When true, selecting a decode in Band/RX activity looks up the call on QRZ.</summary>
    public bool QrzLookupOnSelect { get; set; } = true;
    /// <summary>Fill empty Dx Grid from QRZ lookup when available.</summary>
    public bool QrzFillGrid { get; set; } = true;

    /// <summary>Upload decoded callspots to PSK Reporter (report.pskreporter.info).</summary>
    public bool PskReporterEnabled { get; set; } = true;

    /// <summary>Antenna description sent to PSK Reporter with each spot batch (e.g. "Dipole 40m, 30 ft").</summary>
    public string PskReporterAntenna { get; set; } = "";

    // —— WSPR-2 (JTDX / WSJT-X style) ——
    /// <summary>Average % of 2-minute slots devoted to TX (random schedule). Typical 20–25.</summary>
    public int WsprTxPercent { get; set; } = 20;
    /// <summary>Upload decoded WSPR spots to wsprnet.org.</summary>
    public bool WsprUploadSpots { get; set; } = true;
    /// <summary>Reported TX power in dBm (snapped to WSPR valid set). Synced from / to TxPower watts.</summary>
    public int WsprPowerDbm { get; set; } = 37;

    // Main window placement (null / 0 = use XAML defaults)
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double WindowWidth { get; set; }
    public double WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }

    /// <summary>Vertical pane shares (waterfall / activity / log). 0 = XAML defaults.</summary>
    public double LayoutWaterfallShare { get; set; }
    public double LayoutActivityShare { get; set; }
    public double LayoutLogShare { get; set; }

    /// <summary>Horizontal activity pane shares (band / rx / macros). 0 = XAML defaults.</summary>
    public double LayoutBandShare { get; set; }
    public double LayoutRxShare { get; set; }
    public double LayoutMacroShare { get; set; }

    /// <summary>
    /// User-defined TX macros (label + template with {mycall}/{mygrid}/{dxcall}/{snr}).
    /// Null = first run → seed CQ DX / CQ EU. Empty list = user cleared them.
    /// </summary>
    public List<CustomMacroEntry>? CustomMacros { get; set; }

    /// <summary>CQ hold template (e.g. CQ {mycall} {mygrid} or CQ DX {mycall} {mygrid}).</summary>
    public string CqTemplate { get; set; } = "CQ {mycall} {mygrid}";
}
