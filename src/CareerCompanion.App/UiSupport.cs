using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace CareerCompanion.App;

/// <summary>
/// Placeholder text for an empty input. The template shows it; nothing else has to know.
/// </summary>
public static class Hint
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(Hint), new PropertyMetadata(string.Empty));

    public static string GetText(DependencyObject element) => (string)element.GetValue(TextProperty);
    public static void SetText(DependencyObject element, string value) => element.SetValue(TextProperty, value);
}

/// <summary>
/// Input guards for the match form. A rating box that silently accepts "seven" produces a
/// binding failure the player never sees, so the keystroke is refused instead.
/// </summary>
public static class Numeric
{
    public static readonly DependencyProperty ModeProperty = DependencyProperty.RegisterAttached(
        "Mode", typeof(string), typeof(Numeric), new PropertyMetadata(null, OnModeChanged));

    public static string? GetMode(DependencyObject element) => (string?)element.GetValue(ModeProperty);
    public static void SetMode(DependencyObject element, string? value) => element.SetValue(ModeProperty, value);

    private static void OnModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box) return;
        box.PreviewTextInput -= OnPreviewTextInput;
        DataObject.RemovePastingHandler(box, OnPaste);
        if (string.IsNullOrEmpty(e.NewValue as string)) return;
        box.PreviewTextInput += OnPreviewTextInput;
        DataObject.AddPastingHandler(box, OnPaste);
    }

    private static void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        var box = (TextBox)sender;
        e.Handled = !IsAcceptable(box, box.Text.Remove(box.SelectionStart, box.SelectionLength)
            .Insert(box.SelectionStart, e.Text));
    }

    private static void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        var box = (TextBox)sender;
        var pasted = e.DataObject.GetData(DataFormats.UnicodeText) as string ?? "";
        if (!IsAcceptable(box, box.Text.Remove(box.SelectionStart, box.SelectionLength)
            .Insert(box.SelectionStart, pasted))) e.CancelCommand();
    }

    private static bool IsAcceptable(TextBox box, string candidate)
    {
        if (candidate.Length == 0) return true;
        return GetMode(box) == "Decimal"
            ? double.TryParse(candidate, NumberStyles.Float, CultureInfo.CurrentCulture, out _)
            : int.TryParse(candidate, NumberStyles.Integer, CultureInfo.CurrentCulture, out _);
    }
}

/// <summary>A single result in a form guide, already coloured.</summary>
public sealed record FormPill(string Letter, Brush Fill, Brush Ink);

/// <summary>
/// Turns the form string the view model already produces ("W  W  D  L") into coloured pills,
/// so the dashboard can show a form guide without the view model learning about brushes.
/// </summary>
public sealed class FormPillsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var raw = value as string ?? "";
        var pills = new List<FormPill>();
        foreach (var token in raw.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var letter = token.Trim().ToUpperInvariant();
            if (letter.Length != 1 || !"WDLU?".Contains(letter)) continue;
            pills.Add(letter switch
            {
                "W" => new FormPill("W", Ui.Brush("Win"), Ui.Brush("Shadow")),
                "D" => new FormPill("D", Ui.Brush("Draw"), Ui.Brush("Shadow")),
                "L" => new FormPill("L", Ui.Brush("Loss"), Ui.Brush("Text")),
                _ => new FormPill("?", Ui.Brush("Stroke"), Ui.Brush("Muted"))
            });
        }
        return pills;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Visibility from emptiness. Pass "Invert" to show when there IS content; the plain form shows
/// the empty state. Handles strings, collections, counts, and null alike.
/// </summary>
public sealed class EmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var empty = value switch
        {
            null => true,
            string s => string.IsNullOrWhiteSpace(s),
            int n => n == 0,
            System.Collections.ICollection c => c.Count == 0,
            System.Collections.IEnumerable e => !e.GetEnumerator().MoveNext(),
            _ => false
        };
        var invert = string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase);
        return empty != invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Visibility from a bool. Pass "Invert" to flip it.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is true;
        var invert = string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase);
        return flag != invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Colours a match rating: standout, solid, poor.</summary>
public sealed class RatingToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value?.ToString() ?? "";
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var rating)
            && !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out rating))
            return Ui.Brush("Faint");
        return rating >= 8 ? Ui.Brush("Win")
            : rating >= 7 ? Ui.Brush("Interactive")
            : rating >= 6 ? Ui.Brush("Muted")
            : Ui.Brush("Danger");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Colours a FIFA overall rating band.</summary>
public sealed class OverallToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = (value?.ToString() ?? "").Replace("OVR", "").Trim();
        if (!int.TryParse(text, out var overall)) return Ui.Brush("Faint");
        return overall >= 85 ? Ui.Brush("Accent")
            : overall >= 78 ? Ui.Brush("Interactive")
            : overall >= 70 ? Ui.Brush("Text")
            : Ui.Brush("Muted");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Colours an event by how much it mattered.</summary>
public sealed class ImportanceToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var importance = value is int i ? i : int.TryParse(value?.ToString(), out var parsed) ? parsed : 0;
        return importance >= 75 ? Ui.Brush("Accent")
            : importance >= 48 ? Ui.Brush("Interactive")
            : importance >= 25 ? Ui.Brush("Muted")
            : Ui.Brush("Faint");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Colours a squad availability word. Anything Touchline has not verified stays neutral, because
/// a confident green on an unconfirmed selection would be a claim the save never made.
/// </summary>
public sealed class AvailabilityToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = (value?.ToString() ?? "").ToUpperInvariant();
        if (text.Contains("INJURED") || text.Contains("SUSPENDED") || text.Contains("UNAVAILABLE"))
            return Ui.Brush("Danger");
        if (text.Contains("SELECTED TO PLAY") || text.Contains("ACTIVE")) return Ui.Brush("Win");
        if (text.Contains("NOT SELECTED") || text.Contains("FORMER")) return Ui.Brush("Faint");
        if (text.Contains("BENCH")) return Ui.Brush("Warm");
        return Ui.Brush("Muted");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Turns an internal identifier into something a person reads: MATCH_LOST becomes "Match lost",
/// PrivateMessage becomes "Private message". Display only - the stored value never changes.
/// </summary>
public sealed class HumanizeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var raw = (value?.ToString() ?? "").Replace('_', ' ').Trim();
        if (raw.Length == 0) return "";

        // Split camel and Pascal case, but keep runs of capitals (FIFA, UEFA) together.
        var text = new System.Text.StringBuilder(raw.Length + 8);
        for (var i = 0; i < raw.Length; i++)
        {
            var c = raw[i];
            if (i > 0 && char.IsUpper(c) && raw[i - 1] != ' '
                && (char.IsLower(raw[i - 1]) || (i + 1 < raw.Length && char.IsLower(raw[i + 1]))))
                text.Append(' ');
            text.Append(c);
        }

        var words = text.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < words.Length; i++)
        {
            var word = words[i];
            // An all-caps run is an acronym or a shouted constant; only the first word is titled.
            if (word.Length > 1 && word.ToUpperInvariant() == word && word.Any(char.IsLetter))
                word = i == 0
                    ? char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()
                    : word.ToLowerInvariant();
            else if (i == 0) word = char.ToUpperInvariant(word[0]) + word[1..];
            else word = char.ToLowerInvariant(word[0]) + word[1..];
            words[i] = word;
        }
        return string.Join(' ', words);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Two-letter monogram for a conversation avatar.</summary>
public sealed class InitialsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var words = (value?.ToString() ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length switch
        {
            0 => "?",
            1 => words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant(),
            _ => $"{char.ToUpperInvariant(words[0][0])}{char.ToUpperInvariant(words[^1][0])}"
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Resource lookup that never throws while the designer or a unit test has no App.</summary>
internal static class Ui
{
    public static Brush Brush(string key)
        => Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
}
