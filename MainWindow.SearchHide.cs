using System.Windows;
using System.Windows.Threading;
using BijouHub.Services;
using BijouHub.Views;

namespace BijouHub;

// Apps hidden from Windows search (see SearchHideWindow). Apps write their Start menu shortcut back
// when they update, so BijouHub checks now and then and hides it again.
public partial class MainWindow
{
    private DispatcherTimer? _searchWatch;

    private void StartSearchWatch()
    {
        SearchHiding.Create().Recheck();
        _searchWatch = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _searchWatch.Tick += (_, _) => SearchHiding.Create().Recheck();
        _searchWatch.Start();
    }

    private void OpenSearchHide_Click(object sender, RoutedEventArgs e)
    {
        new SearchHideWindow { Owner = DialogOwner }.ShowDialog();
    }
}
