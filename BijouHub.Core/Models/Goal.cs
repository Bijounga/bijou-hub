using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BijouHub.Models;

public class Goal : INotifyPropertyChanged
{
    private string _name = "New Goal";
    private double _weight = 100;
    private bool _isComplete;

    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public double Weight
    {
        get => _weight;
        set => SetField(ref _weight, value);
    }

    public bool IsComplete
    {
        get => _isComplete;
        set
        {
            if (SetField(ref _isComplete, value))
                OnPropertyChanged(nameof(Completion));
        }
    }

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

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        if (propertyName == nameof(Weight))
            OnPropertyChanged(nameof(Completion));
        return true;
    }
}
