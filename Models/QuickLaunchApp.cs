using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;

namespace BijouHub.Models;

public class QuickLaunchApp
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";

    [JsonIgnore]
    public BitmapSource? Icon { get; set; }
}
