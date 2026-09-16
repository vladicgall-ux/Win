using System.ComponentModel;

namespace WinFileRecovery.App.ViewModels;

/// <summary>Wraps an immutable model with a checkbox-bindable IsSelected flag for simple list UIs.</summary>
public sealed class SelectableItem<T> : INotifyPropertyChanged
{
    public T Value { get; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
    }

    public SelectableItem(T value) => Value = value;

    public event PropertyChangedEventHandler? PropertyChanged;
}
