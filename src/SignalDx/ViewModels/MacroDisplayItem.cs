using SignalDx.Ft8;
using SignalDx.Models;

namespace SignalDx.ViewModels;

/// <summary>TX macro button model with live expanded message preview.</summary>
public sealed class MacroDisplayItem : ObservableObject
{
    string _preview = "";

    public MacroDisplayItem(TxMacro macro) => Macro = macro;

    public TxMacro Macro { get; }
    public string Label => Macro.Label;
    public TxMacroKind Kind => Macro.Kind;
    public bool IsCustom => Macro.Kind == TxMacroKind.Custom;
    public string? CustomId => Macro.CustomId;

    public string Preview
    {
        get => _preview;
        private set => SetField(ref _preview, value);
    }

    public void Refresh(StationSettings station, string dxCall, string report, string freeText)
    {
        Preview = Macro.Kind == TxMacroKind.FreeText && !string.IsNullOrWhiteSpace(freeText)
            ? freeText.Trim()
            : Ft8MessageBuilder.Build(Macro.Template, station, dxCall, report);
    }
}
