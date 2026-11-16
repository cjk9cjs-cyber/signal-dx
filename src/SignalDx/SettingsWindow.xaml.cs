using System.Windows;
using SignalDx.ViewModels;

namespace SignalDx;

public partial class SettingsWindow : Window
{
    public SettingsWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();
}
