namespace BijouHub.Models;

public class WorkMode
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "New Mode";
    public List<LaunchItem> LaunchItems { get; set; } = new();
    public List<BlockItem> BlockItems { get; set; } = new();

    public override string ToString() => Name;
}
