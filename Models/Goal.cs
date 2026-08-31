using System.Collections.ObjectModel;

namespace BijouHub.Models;

public class Goal
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "New Goal";
    public double Weight { get; set; } = 100;
    public bool IsComplete { get; set; }
    public ObservableCollection<Goal> SubGoals { get; set; } = new();

    public double Completion
    {
        get
        {
            if (SubGoals.Count == 0)
                return IsComplete ? 1.0 : 0.0;

            double total = 0;
            foreach (var sub in SubGoals)
                total += (sub.Weight / 100.0) * sub.Completion;
            return total;
        }
    }

    public Goal Clone()
    {
        var clone = new Goal { Id = Id, Name = Name, Weight = Weight, IsComplete = IsComplete };
        foreach (var sub in SubGoals)
            clone.SubGoals.Add(sub.Clone());
        return clone;
    }
}
