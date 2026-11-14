namespace SignalDx.Models;

public sealed class QsoEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTime UtcStart { get; set; } = DateTime.UtcNow;
    public DateTime? UtcEnd { get; set; }
    public string Mode { get; set; } = "FT8";
    public string Band { get; set; } = "20m";
    public double DialMhz { get; set; } = 14.074;
    public string MyCall { get; set; } = "";
    public string MyGrid { get; set; } = "";
    public string DxCall { get; set; } = "";
    public string DxGrid { get; set; } = "";
    public string SentRst { get; set; } = "";
    public string RcvdRst { get; set; } = "";
    public string Comment { get; set; } = "";
}
