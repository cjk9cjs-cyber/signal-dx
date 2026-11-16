using System.Windows;
using SignalDx.Models;

namespace SignalDx;

public partial class LogQsoWindow : Window
{
    public QsoEntry Entry { get; }

    public LogQsoWindow(QsoEntry entry)
    {
        InitializeComponent();
        Entry = entry;
        DataContext = entry;
    }

    void Log_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    void Skip_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
