using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Daydreamer.Core.Models;

/// <summary>
/// Base class that lets the UI know when a property changes, so bound controls
/// (and the editor's live formatting) update automatically.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>Sets the field and raises PropertyChanged only if the value actually changed.</summary>
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
