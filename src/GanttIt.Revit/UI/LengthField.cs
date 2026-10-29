using GanttIt.Revit.Services;
using GanttIt.Revit.UI.Mvvm;

namespace GanttIt.Revit.UI;

/// <summary>
/// A length the user types in the project's units (D11, A12). Text is parsed
/// with <see cref="IGanttItBackend.TryParseLength"/> when the field commits
/// (focus leaves or Enter), and re-displayed with
/// <see cref="IGanttItBackend.FormatLength"/>, so a typed value round-trips.
/// A value that doesn't parse keeps the user's text and reports an error;
/// the last valid length stays in effect.
/// </summary>
public sealed class LengthField : ObservableObject
{
    private readonly IGanttItBackend _backend;
    private readonly Action<double> _apply;
    private string _text;
    private string? _error;

    public LengthField(IGanttItBackend backend, double feet, Action<double> apply)
    {
        _backend = backend;
        _apply = apply;
        Value = feet;
        _text = Format(feet);
    }

    /// <summary>The last valid length, in feet.</summary>
    public double Value { get; private set; }

    public string Text
    {
        get => _text;
        set
        {
            if (TryParse(value, out var feet))
            {
                Value = feet;
                Error = null;
                _text = Format(feet);
                OnPropertyChanged();
                _apply(feet);
            }
            else
            {
                _text = value;
                OnPropertyChanged();
                Error = Resources.Strings.Length_Invalid(value.Trim(), Format(Value));
            }
        }
    }

    public string? Error
    {
        get => _error;
        private set
        {
            if (SetField(ref _error, value)) OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => Error is not null;

    private bool TryParse(string text, out double feet)
    {
        feet = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        try
        {
            return _backend.TryParseLength(text, out feet) && double.IsFinite(feet);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private string Format(double feet)
    {
        try
        {
            return _backend.FormatLength(feet);
        }
        catch (Exception)
        {
            return feet.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + "'";
        }
    }
}
