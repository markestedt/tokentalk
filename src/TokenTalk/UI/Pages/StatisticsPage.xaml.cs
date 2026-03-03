using System.Windows;
using System.Windows.Controls;
using TokenTalk.UI.ViewModels;

namespace TokenTalk.UI.Pages;

public partial class StatisticsPage : System.Windows.Controls.UserControl
{
    private readonly StatisticsViewModel _vm;

    public StatisticsPage(StatisticsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    private void FilterWeek_Click(object sender, System.Windows.RoutedEventArgs e)
        => SetRange(StatsTimeRange.Week);

    private void FilterMonth_Click(object sender, System.Windows.RoutedEventArgs e)
        => SetRange(StatsTimeRange.Month);

    private void FilterYear_Click(object sender, System.Windows.RoutedEventArgs e)
        => SetRange(StatsTimeRange.Year);

    private void FilterAllTime_Click(object sender, System.Windows.RoutedEventArgs e)
        => SetRange(StatsTimeRange.AllTime);

    private void SetRange(StatsTimeRange range)
    {
        _vm.SelectedRange = range;
        _ = _vm.LoadAsync();
    }
}
