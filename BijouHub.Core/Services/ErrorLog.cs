using System.IO;

namespace BijouHub.Services;

// One line in errors.log (next to settings.json) for something that failed quietly but that a
// person looking for the cause will want to find.
public static class ErrorLog
{
    public static void Write(string message)
    {
        try
        {
            File.AppendAllText(Path.Combine(DataPaths.LocalDir, "errors.log"), $"{DateTime.Now:s}  {message}\n\n");
        }
        catch
        {
            // nowhere to write it
        }
    }
}
