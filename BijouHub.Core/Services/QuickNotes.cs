namespace BijouHub.Services;

// Things jotted down in passing (on the focus screen) go into the day's notes, one bullet a line.
public static class QuickNotes
{
    public static string Append(string? notes, string text)
    {
        var line = "\u2022 " + text.Trim();
        return string.IsNullOrWhiteSpace(notes) ? line : notes.TrimEnd() + "\n" + line;
    }
}
