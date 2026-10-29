using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using GanttIt.Core.Dates;
using GanttIt.Revit.UI.Mvvm;

namespace GanttIt.Revit.UI.Editor;

/// <summary>
/// The editor's Start/End field: shows <see cref="SelectedDate"/> in <see cref="DateFormat"/>
/// (the chart's date format from Settings) and reads typed dates in the same pattern, so
/// the grid matches the chart in either Revit year and on any Windows locale. Day/month
/// order always follows the pattern (<see cref="DateFormats.TryParse"/>); text that
/// doesn't match is kept, marked with the validation style and explained in the tooltip,
/// and never applied. The calendar drop-down is WPF's own <see cref="Calendar"/>.
/// </summary>
public partial class DateField : UserControl
{
    public static readonly DependencyProperty SelectedDateProperty = DependencyProperty.Register(
        nameof(SelectedDate), typeof(DateTime?), typeof(DateField),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((DateField)d).ShowDate()));

    /// <summary>The project's date pattern; an unusable one falls back to ISO (<see cref="DateFormats.EntryPattern"/>).</summary>
    public static readonly DependencyProperty DateFormatProperty = DependencyProperty.Register(
        nameof(DateFormat), typeof(string), typeof(DateField),
        new PropertyMetadata(DateFormats.Iso, (d, _) => ((DateField)d).ShowDate()));

    public static readonly DependencyProperty DisplayDateStartProperty = DependencyProperty.Register(
        nameof(DisplayDateStart), typeof(DateTime?), typeof(DateField), new PropertyMetadata(null));

    public static readonly DependencyProperty DisplayDateEndProperty = DependencyProperty.Register(
        nameof(DisplayDateEnd), typeof(DateTime?), typeof(DateField), new PropertyMetadata(null));

    /// <summary>A validation problem from the view-model (e.g. End before Start); drawn like a typing error.</summary>
    public static readonly DependencyProperty HasErrorProperty = DependencyProperty.Register(
        nameof(HasError), typeof(bool), typeof(DateField), new PropertyMetadata(false, (d, _) => ((DateField)d).ShowState()));

    /// <summary>AutomationId of the text box ("Section2.Start"); the calendar button gets "<c>&lt;id&gt;.Calendar</c>".</summary>
    public static readonly DependencyProperty FieldIdProperty = DependencyProperty.Register(
        nameof(FieldId), typeof(string), typeof(DateField), new PropertyMetadata(null, (d, _) => ((DateField)d).ApplyAutomation()));

    /// <summary>Automation name of the text box ("Start").</summary>
    public static readonly DependencyProperty FieldNameProperty = DependencyProperty.Register(
        nameof(FieldName), typeof(string), typeof(DateField), new PropertyMetadata(null, (d, _) => ((DateField)d).ApplyAutomation()));

    /// <summary>
    /// The pending typing error (null when the text shows <see cref="SelectedDate"/>), two-way:
    /// the row reports it to the editor, which lists it and blocks Generate/Update, like a
    /// length that doesn't parse. When the row clears it (a refresh), the field drops the
    /// typed text and shows the date again.
    /// </summary>
    public static readonly DependencyProperty TypingErrorProperty = DependencyProperty.Register(
        nameof(TypingError), typeof(string), typeof(DateField),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnTypingErrorChanged));

    private static readonly DateOnly Sample = new(2026, 12, 31);

    private string? _typingError;
    private bool _settingCalendar;

    public DateField()
    {
        InitializeComponent();
        Box.KeyDown += OnBoxKeyDown;
        Box.LostKeyboardFocus += (_, _) => Commit();
        Box.TextChanged += (_, _) =>
        {
            if (_typingError is not null && Box.Text == Formatted()) ClearTypingError();
        };
        CalendarButton.Click += (_, _) => OpenCalendar();
        Picker.PreviewMouseLeftButtonUp += OnCalendarMouseUp;
        Picker.KeyDown += OnCalendarKeyDown;
        CalendarPopup.Opened += (_, _) => CalendarButton.IsHitTestVisible = false;
        CalendarPopup.Closed += (_, _) => CalendarButton.IsHitTestVisible = true;
        ShowDate();
    }

    public DateTime? SelectedDate
    {
        get => (DateTime?)GetValue(SelectedDateProperty);
        set => SetValue(SelectedDateProperty, value);
    }

    public string? DateFormat
    {
        get => (string?)GetValue(DateFormatProperty);
        set => SetValue(DateFormatProperty, value);
    }

    public DateTime? DisplayDateStart
    {
        get => (DateTime?)GetValue(DisplayDateStartProperty);
        set => SetValue(DisplayDateStartProperty, value);
    }

    public DateTime? DisplayDateEnd
    {
        get => (DateTime?)GetValue(DisplayDateEndProperty);
        set => SetValue(DisplayDateEndProperty, value);
    }

    public bool HasError
    {
        get => (bool)GetValue(HasErrorProperty);
        set => SetValue(HasErrorProperty, value);
    }

    public string? FieldId
    {
        get => (string?)GetValue(FieldIdProperty);
        set => SetValue(FieldIdProperty, value);
    }

    public string? FieldName
    {
        get => (string?)GetValue(FieldNameProperty);
        set => SetValue(FieldNameProperty, value);
    }

    public string? TypingError
    {
        get => (string?)GetValue(TypingErrorProperty);
        set => SetValue(TypingErrorProperty, value);
    }

    /// <summary>The pattern the field shows and reads.</summary>
    public string Pattern => DateFormats.EntryPattern(DateFormat);

    private string Formatted() =>
        SelectedDate is { } d ? DateFormats.Format(DateOnly.FromDateTime(d), Pattern) : "";

    // ---- text -----------------------------------------------------------------------------

    /// <summary>Shows the current date in the pattern; drops any pending typing error.</summary>
    private void ShowDate()
    {
        SetTypingError(null);
        Box.Text = Formatted();
        ShowState();
    }

    /// <summary>Applies typed text (Enter or leaving the field).</summary>
    private void Commit()
    {
        var text = Box.Text;
        if (text == Formatted())
        {
            ClearTypingError();
            return;
        }
        if (string.IsNullOrWhiteSpace(text))
        {
            ShowDate(); // a cleared field snaps back, as the date picker did
            return;
        }
        if (!DateFormats.TryParse(text, Pattern, out var date))
        {
            SetTypingError($"\"{text.Trim()}\" is not a date in this chart's format. " +
                           $"Type it as {Pattern}, for example {DateFormats.Format(Sample, Pattern)}.");
            return;
        }

        var value = date.ToDateTime(TimeOnly.MinValue);
        if (SelectedDate != value) SetCurrentValue(SelectedDateProperty, value); // the view-model may refuse it and push its own value back
        ShowDate();
    }

    private void ClearTypingError() => SetTypingError(null);

    private void SetTypingError(string? error)
    {
        if (_typingError == error) return;
        _typingError = error; // before SetCurrentValue, so OnTypingErrorChanged sees it as ours
        SetCurrentValue(TypingErrorProperty, error);
        ShowState();
    }

    /// <summary>The row cleared the error (row refresh): drop the typed text and show the date.</summary>
    private static void OnTypingErrorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var field = (DateField)d;
        if (e.NewValue is null && field._typingError is not null) field.ShowDate();
    }

    private void ShowState()
    {
        Behaviors.SetHasError(Box, HasError || _typingError is not null);
        Box.ToolTip = _typingError ?? $"Type the date as {Pattern}, or press Alt+Down for the calendar.";
    }

    private void OnBoxKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                Commit();
                Box.SelectAll();
                e.Handled = true;
                break;
            case Key.Escape when Box.Text != Formatted():
                ShowDate(); // first Esc undoes the typing; the next one reaches the window
                Box.SelectAll();
                e.Handled = true;
                break;
            case Key.System when e.SystemKey == Key.Down && Keyboard.Modifiers == ModifierKeys.Alt:
            case Key.F4:
                OpenCalendar();
                e.Handled = true;
                break;
        }
    }

    // ---- calendar -------------------------------------------------------------------------

    private void OpenCalendar()
    {
        Commit();
        _settingCalendar = true;
        try
        {
            Picker.DisplayDateStart = DisplayDateStart;
            Picker.DisplayDateEnd = DisplayDateEnd;
            // The calendar throws for a date outside its range.
            var inRange = SelectedDate is { } d
                          && (DisplayDateStart is not { } min || d >= min)
                          && (DisplayDateEnd is not { } max || d <= max);
            Picker.SelectedDate = inRange ? SelectedDate : null;
            var shown = inRange ? SelectedDate!.Value : DateTime.Today;
            if (DisplayDateStart is { } lo && shown < lo) shown = lo;
            if (DisplayDateEnd is { } hi && shown > hi) shown = hi;
            Picker.DisplayDate = shown;
        }
        finally
        {
            _settingCalendar = false;
        }
        CalendarPopup.IsOpen = true;
        Dispatcher.BeginInvoke(() =>
        {
            // Keyboard navigation starts on the selected day.
            if (CalendarPopup.IsOpen) Picker.Focus();
        });
    }

    private void CloseCalendar(bool apply)
    {
        if (!CalendarPopup.IsOpen) return;
        CalendarPopup.IsOpen = false;
        if (apply && !_settingCalendar && Picker.SelectedDate is { } picked && picked.Date != SelectedDate)
            SetCurrentValue(SelectedDateProperty, picked.Date);
        ShowDate();
        Box.Focus();
    }

    /// <summary>A click on a day picks it. The calendar keeps the mouse captured after a click, so release it.</summary>
    private void OnCalendarMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (Mouse.Captured is CalendarItem or CalendarDayButton) Mouse.Capture(null);
        if (IsInside<CalendarDayButton>(e.OriginalSource as DependencyObject)) CloseCalendar(apply: true);
    }

    private void OnCalendarKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
            case Key.Space:
                CloseCalendar(apply: true);
                e.Handled = true;
                break;
            case Key.Escape:
                CloseCalendar(apply: false);
                e.Handled = true;
                break;
        }
    }

    private static bool IsInside<T>(DependencyObject? element) where T : DependencyObject
    {
        for (var e = element; e is not null; e = e is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(e) : LogicalTreeHelper.GetParent(e))
            if (e is T) return true;
        return false;
    }

    private void ApplyAutomation()
    {
        AutomationProperties.SetAutomationId(Box, FieldId ?? "");
        AutomationProperties.SetName(Box, FieldName ?? "");
        AutomationProperties.SetAutomationId(CalendarButton, FieldId is null ? "" : FieldId + ".Calendar");
        AutomationProperties.SetAutomationId(Picker, FieldId is null ? "" : FieldId + ".CalendarPopup");
        AutomationProperties.SetName(CalendarButton, FieldName is null ? "Calendar" : $"{FieldName} calendar");
    }
}
