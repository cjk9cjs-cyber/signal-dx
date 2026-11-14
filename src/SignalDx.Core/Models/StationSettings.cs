namespace SignalDx.Models;

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
    public int InputDeviceIndex { get; set; } = -1;
    public int OutputDeviceIndex { get; set; } = -1;
}
