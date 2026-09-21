using System.Windows;
using AutoFan.App.ViewModels;

namespace AutoFan.App;

public partial class OptimizeWalkWindow : Window
{
    private readonly OptimizeWalkViewModel _viewModel;
    private bool _allowClose;

    public OptimizeWalkWindow(OptimizeWalkViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Title = OptimizeWalkViewModel.WindowTitle;
        Closing += OnClosing;
    }

    public void AllowClose()
    {
        _allowClose = true;
    }

    private void OnPrimary(object sender, RoutedEventArgs e)
    {
        _viewModel.RequestPrimary();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        _viewModel.RequestCancel();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        _viewModel.RequestCancel();
    }
}
