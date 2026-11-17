using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ContextAll.App.ViewModels;

/// <summary>Lightweight INotifyPropertyChanged base with optional UI thread dispatch.</summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    private readonly Action<Action> _dispatch;

    protected ViewModelBase(Action<Action>? dispatchToUi = null)
    {
        _dispatch = dispatchToUi ?? (static work => work());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void DispatchToUi(Action work) => _dispatch(work);

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
