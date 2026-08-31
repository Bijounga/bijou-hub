using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Win32;
using BijouHub.Models;
using BijouHub.Services;

namespace BijouHub.Views;

public partial class ModeEditorWindow : Window
{
    private readonly ObservableCollection<LaunchItem> _launchItems;
    private readonly ObservableCollection<BlockItem> _blockItems;
    public WorkMode Mode { get; }

    public ModeEditorWindow(WorkMode mode)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        Mode = mode;

        NameBox.Text = mode.Name;
        _launchItems = new ObservableCollection<LaunchItem>(mode.LaunchItems);
        _blockItems = new ObservableCollection<BlockItem>(mode.BlockItems);
        LaunchItemsList.ItemsSource = _launchItems;
        BlockItemsList.ItemsSource = _blockItems;

        ListReorderBehavior.Enable(LaunchItemsList, _launchItems);
        ListReorderBehavior.Enable(BlockItemsList, _blockItems);
    }

    private void AddApp_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Executable (*.exe)|*.exe|All files (*.*)|*.*" };
        if (dlg.ShowDialog() == true)
            _launchItems.Add(new LaunchItem { Type = LaunchItemType.Application, Path = dlg.FileName });
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog();
        if (dlg.ShowDialog() == true)
            _launchItems.Add(new LaunchItem { Type = LaunchItemType.Folder, Path = dlg.FolderName });
    }

    private void AddFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "All files (*.*)|*.*" };
        if (dlg.ShowDialog() == true)
            _launchItems.Add(new LaunchItem { Type = LaunchItemType.File, Path = dlg.FileName });
    }

    private void AddLink_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new AddLinkWindow { Owner = this };
        if (dlg.ShowDialog() == true && dlg.Result != null)
            _launchItems.Add(dlg.Result);
    }

    private void SetOpenWith_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: LaunchItem item }) return;

        var dlg = new OpenFileDialog { Filter = "Executable (*.exe)|*.exe|All files (*.*)|*.*" };
        if (dlg.ShowDialog() == true)
        {
            item.OpenWithAppPath = dlg.FileName;
            RefreshLaunchItems();
        }
    }

    private void RefreshLaunchItems()
    {
        LaunchItemsList.ItemsSource = null;
        LaunchItemsList.ItemsSource = _launchItems;
    }

    private void RemoveLaunchItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: LaunchItem item })
            _launchItems.Remove(item);
    }

    private void BrowseBlock_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Executable (*.exe)|*.exe|All files (*.*)|*.*" };
        if (dlg.ShowDialog() != true) return;

        var name = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);
        _blockItems.Add(new BlockItem { ProcessName = name });
    }

    private void BlockNameBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
            AddBlock_Click(sender, e);
    }

    private void AddBlock_Click(object sender, RoutedEventArgs e)
    {
        var name = BlockNameBox.Text.Trim();
        if (string.IsNullOrEmpty(name)) return;
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];

        _blockItems.Add(new BlockItem { ProcessName = name });
        BlockNameBox.Clear();
    }

    private void RemoveBlockItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: BlockItem item })
            _blockItems.Remove(item);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show("Give this mode a name.", "Edit Mode", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Mode.Name = name;
        Mode.LaunchItems = _launchItems.ToList();
        Mode.BlockItems = _blockItems.ToList();

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
