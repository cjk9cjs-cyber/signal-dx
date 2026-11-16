namespace SignalDx.Models;

public enum CatBackend
{
    None = 0,
    HamlibTcp = 1,
    SerialKenwood = 2,
    SerialYaesu = 3,
    HamRadioDeluxe = 4
}

public enum DecoderEngine
{
    Simulate = 0,
    Jtdx = 1
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
    public bool AutoSequence { get; set; } = true;
    public bool EnableTx { get; set; }
    public bool SimulateDecodes { get; set; } = true;
    /// <summary>Preferred decoder when Simulate is off: Simulate fallback or JTDX jtdxjt9.</summary>
    public DecoderEngine DecoderEngine { get; set; } = DecoderEngine.Simulate;
    /// <summary>Full path to jtdxjt9.exe (JTDX bin folder).</summary>
    public string JtdxJt9Path { get; set; } = "";
    /// <summary>jtdxjt9 -d depth (1–3).</summary>
    public int JtdxDecodeDepth { get; set; } = 3;
    public bool TxEven { get; set; } = true;
    public string TxPower { get; set; } = "50";
    /// <summary>TX audio drive 0–100% for ALC tuning (software level into the radio).</summary>
    public int TxDrivePercent { get; set; } = 40;
    /// <summary>When true, push RFPOWER to the radio via CAT from TxPower watts vs radio max.</summary>
    public bool CatRfPowerEnabled { get; set; } = true;
    public double RadioMaxWatts { get; set; } = 100;
    public int InputDeviceIndex { get; set; } = -1;
    public int OutputDeviceIndex { get; set; } = -1;
    public string WaterfallPalette { get; set; } = "Classic";
    public bool LockTxToRx { get; set; } = true;

    // CAT / Hamlib
    public CatBackend CatBackend { get; set; } = CatBackend.HamlibTcp;
    public string CatHost { get; set; } = "127.0.0.1";
    public int CatPort { get; set; } = 4532;
    public string CatComPort { get; set; } = "COM3";
    public int CatBaud { get; set; } = 9600;
    public bool CatPollEnabled { get; set; } = true;
    public bool CatPttEnabled { get; set; } = true;
    public bool CatFollowDial { get; set; } = true;
    public int HamlibModelId { get; set; } = 2037;
    public string HamlibDevice { get; set; } = "COM3";
    public string HamlibRigctldPath { get; set; } = "";
    public string HamlibMode { get; set; } = "USB";
    public bool HamlibAutoStart { get; set; }
    public int HrdPort { get; set; } = 7809;

    // External logging
    public bool UdpLogEnabled { get; set; } = true;
    public string UdpLogHost { get; set; } = "127.0.0.1";
    public int UdpLogPort { get; set; } = 2237;
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
    /// <summary>When true, selecting a decode in Band/RX activity looks up the call on QRZ.</summary>
    public bool QrzLookupOnSelect { get; set; } = true;
    /// <summary>Fill empty Dx Grid from QRZ lookup when available.</summary>
    public bool QrzFillGrid { get; set; } = true;
}
