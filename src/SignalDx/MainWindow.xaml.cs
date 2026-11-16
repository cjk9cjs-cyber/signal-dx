using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SignalDx.Models;
using SignalDx.ViewModels;

namespace SignalDx;

public partial class MainWindow : Window
{
    readonly MainViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        Loaded += (_, _) => _vm.AttachWaterfall(Waterfall);
        Closed += (_, _) => _vm.Dispose();
    }

    void Activity_OnMouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        EnableTxFromGrid(sender);

    void RxActivity_OnMouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        EnableTxFromGrid(sender);

    void EnableTxFromGrid(object sender)
    {
        if (sender is not DataGrid grid) return;
        if (grid.SelectedItem is DecodeHit hit)
            _vm.EnableTxForStation(hit);
    }
}
