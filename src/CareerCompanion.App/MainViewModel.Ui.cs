using System.Windows;

namespace CareerCompanion.App;

/// <summary>
/// Presentation-only state: things the new shell shows that are not career facts.
///
/// Kept apart from MainViewModel so the career logic file stays about the career. Nothing here
/// reads or writes the database beyond the player state the dashboard already displays as text;
/// these are the same numbers, exposed so meters can draw them instead of a pipe-separated line.
/// </summary>
public sealed partial class MainViewModel
{
    private bool _isBusy;
    private string _busyMessage = "";
    private string _squadSearch = "";

    /// <summary>True while a save scan or a model call is in flight. Drives the shell's progress strip.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set { if (Set(ref _isBusy, value)) OnChanged(nameof(BusyVisibility)); }
    }

    public string BusyMessage { get => _busyMessage; private set => Set(ref _busyMessage, value); }

    public Visibility BusyVisibility => IsBusy ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Marks the app busy for the duration of an operation. Nested calls keep the last message.</summary>
    public IDisposable Busy(string message)
    {
        BusyMessage = message;
        IsBusy = true;
        return new BusyScope(this);
    }

    private sealed class BusyScope(MainViewModel owner) : IDisposable
    {
        public void Dispose() { owner.IsBusy = false; owner.BusyMessage = ""; }
    }

    /// <summary>Free-text squad filter, applied on top of the position filter.</summary>
    public string SquadSearch
    {
        get => _squadSearch;
        set { if (Set(ref _squadSearch, value)) { RebuildSquadViews(); OnChanged(nameof(SquadCountLabel)); } }
    }

    public bool HasCareer => CurrentCareer is not null;

    /// <summary>True once there is a real result to draw a form guide from.</summary>
    public bool HasForm => !string.IsNullOrWhiteSpace(CurrentForm)
        && !CurrentForm.Equals("No form", StringComparison.OrdinalIgnoreCase);

    /// <summary>Identity line for the shell header: shirt, position, club, season.</summary>
    public string CareerIdentity => CurrentCareer is null
        ? "No career open"
        : $"#{CurrentCareer.ShirtNumber}  {CurrentCareer.Position}  ·  {CurrentCareer.Club}  ·  {CurrentCareer.Season}";

    public string CareerDateLabel => CurrentCareer is null ? "" : $"In-career date {CurrentCareer.CurrentDate}";

    public string SquadCountLabel
    {
        get
        {
            var shown = SquadMembers.Count;
            var injured = SquadMembers.Count(x => x.Injured);
            var label = shown == 1 ? "1 player" : $"{shown} players";
            return injured == 0 ? label : $"{label} · {injured} injured";
        }
    }

    // The four wellbeing numbers, already shown as text in PlayerMindsetSummary. Exposed
    // individually so the dashboard can draw them as meters.
    public int Confidence => PlayerMindset.Confidence;
    public int Pressure => PlayerMindset.Pressure;
    public int Fatigue => PlayerMindset.Fatigue;
    public int Isolation => PlayerMindset.Isolation;
    public int Resilience => PlayerMindset.Resilience;
    public bool NeedsSupport => PlayerMindset.NeedsSupport;

    /// <summary>Raised from Refresh so every derived presentation value repaints together.</summary>
    private void NotifyPresentationState()
    {
        foreach (var name in new[]
        {
            nameof(HasCareer), nameof(HasForm), nameof(CareerIdentity), nameof(CareerDateLabel), nameof(SquadCountLabel),
            nameof(Confidence), nameof(Pressure), nameof(Fatigue), nameof(Isolation),
            nameof(Resilience), nameof(NeedsSupport)
        }) OnChanged(name);
    }
}
