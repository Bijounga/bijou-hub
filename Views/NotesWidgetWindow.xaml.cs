using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using BijouHub.Services;

namespace BijouHub.Views;

// A notes pad to keep on a second monitor: type, and it's saved. One plain text, shared between your
// computers through the sync folder.
public partial class NotesWidgetWindow : Window
{
    private readonly NotesStore _store;
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private bool _loading;
    private bool _dirty;
    private DateTime _seen;

    public NotesWidgetWindow(NotesStore store)
    {
        InitializeComponent();
        _store = store;
        _saveTimer.Tick += (_, _) => Flush();
        Load();

        // Back in front after being away (or written to from the other computer): pick up newer text,
        // but never over something typed here that isn't saved yet.
        Activated += (_, _) => { if (!_dirty && _store.LastWriteUtc > _seen) Load(); };
        Closing += (_, _) => Flush();

        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 16;
        Top = workArea.Top + 200;
    }

    private void Load()
    {
        _loading = true;
        var caret = NotesBox.CaretIndex;
        NotesBox.Text = _store.Load();
        NotesBox.CaretIndex = Math.Min(caret, NotesBox.Text.Length);
        _seen = _store.LastWriteUtc;
        _loading = false;
        SavedText.Text = "";
    }

    private void NotesBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_loading) return;
        _dirty = true;
        SavedText.Text = "…";
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void Flush()
    {
        _saveTimer.Stop();
        if (!_dirty) return;
        try
        {
            _store.Save(NotesBox.Text);
            _seen = _store.LastWriteUtc;
            _dirty = false;
            SavedText.Text = "saved";
        }
        catch
        {
            SavedText.Text = "not saved"; // the sync folder was busy: the next change tries again
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
