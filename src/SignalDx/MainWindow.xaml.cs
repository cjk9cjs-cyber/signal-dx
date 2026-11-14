using System.Windows;
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
}
