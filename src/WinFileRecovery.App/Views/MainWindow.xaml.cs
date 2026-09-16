using System.Windows;
using System.Windows.Controls;
using WinFileRecovery.App.ViewModels;
using WinFileRecovery.Core.Recovery;

namespace WinFileRecovery.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void ResultsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        vm.SelectedResults.Clear();
        foreach (RecoverableFile item in ResultsGrid.SelectedItems)
            vm.SelectedResults.Add(item);

        vm.RecoverSelectedCommand.RaiseCanExecuteChanged();
    }
}
