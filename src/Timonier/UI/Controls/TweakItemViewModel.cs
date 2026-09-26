using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Timonier.Core.Engine;
using Timonier.Core.Model;
using Timonier.UI.Services;

namespace Timonier.UI.Controls;

public sealed record BadgeInfo(string Text, string Glyph, ToastKind Kind, string? Tooltip = null);

/// <summary>État affichable d'un réglage (une carte). Appliquer = changer IsOn / SelectedOption ou lancer RunCommand.</summary>
public sealed partial class TweakItemViewModel : ObservableObject
{
    private bool _suppress;

    public TweakItemViewModel(TweakDefinition definition)
    {
        Definition = definition;
        IsAvailable = true;
        Options = [.. definition.Options];
        foreach (var o in definition.Options)
        {
            var ops = o.Operations.Select(op => "• " + op.Describe()).ToList();
            if (ops.Count > 0) DetailsLines.Add(L("{0} :\n{1}", o.Label, string.Join("\n", ops)));
        }
        BuildBadges();
    }

    public TweakDefinition Definition { get; }
    public string Title => Definition.Title;
    public string Description => Definition.Description;
    public string? Warning => Definition.Warning;
    public bool IsToggle => Definition.Kind == TweakKind.Toggle;
    public bool IsChoice => Definition.Kind == TweakKind.Choice;
    public bool IsAction => Definition.Kind == TweakKind.Action;
    public ObservableCollection<TweakOption> Options { get; }
    public ObservableCollection<string> DetailsLines { get; } = [];
    public ObservableCollection<BadgeInfo> Badges { get; } = [];
    public string DetailsText => string.Join("\n\n", DetailsLines);

    [ObservableProperty] public partial bool? IsOn { get; set; }
    [ObservableProperty] public partial TweakOption? SelectedOption { get; set; }
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial bool IsAvailable { get; set; }
    [ObservableProperty] public partial string? UnavailableReason { get; set; }
    [ObservableProperty] public partial string? StateText { get; set; }
    [ObservableProperty] public partial string? RecommendationText { get; set; }
    [ObservableProperty] public partial bool IsHighlighted { get; set; }
    [ObservableProperty] public partial bool ShowDetails { get; set; }

    public bool CanInteract => IsAvailable && !IsBusy;

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanInteract));
    partial void OnIsAvailableChanged(bool value) => OnPropertyChanged(nameof(CanInteract));

    private void BuildBadges()
    {
        if (Definition.RequiresAdmin) Badges.Add(new BadgeInfo(LC("badge", "Admin"), "", ToastKind.Info, L("Nécessite une autorisation administrateur (UAC).")));
        if (Definition.Effect.HasFlag(ApplyEffect.Reboot)) Badges.Add(new BadgeInfo(LC("badge", "Redémarrage"), "", ToastKind.Warning, L("Pris en compte après redémarrage du PC.")));
        else if (Definition.Effect.HasFlag(ApplyEffect.SignOut)) Badges.Add(new BadgeInfo(LC("badge", "Déconnexion"), "", ToastKind.Warning, L("Pris en compte après fermeture de session.")));
        else if (Definition.Effect.HasFlag(ApplyEffect.RestartExplorer)) Badges.Add(new BadgeInfo(LC("badge", "Explorateur"), "", ToastKind.Info, L("L'Explorateur Windows doit être relancé (proposé automatiquement).")));
        if (Definition.Risk == RiskLevel.Moderate) Badges.Add(new BadgeInfo(LC("badge", "Modéré"), "", ToastKind.Warning, L("Peut désactiver une fonctionnalité : lisez la description.")));
        if (Definition.Risk == RiskLevel.Advanced) Badges.Add(new BadgeInfo(LC("badge", "Avancé"), "", ToastKind.Error, L("Réservé aux utilisateurs avertis.")));
        if (!Definition.IsReversible) Badges.Add(new BadgeInfo(LC("badge", "Non annulable"), "", ToastKind.Warning, L("Cette action ne peut pas être annulée automatiquement.")));
    }

    /// <summary>Relit l'état réel du système (thread de fond) et met à jour la carte sans rien appliquer.</summary>
    public async Task RefreshAsync()
    {
        var reason = AppHost.Engine.Unavailability(Definition);
        TweakState state = TweakState.UnknownState;
        if (!IsAction) state = await AppHost.Engine.DetectAsync(Definition);

        _suppress = true;
        try
        {
            IsAvailable = reason is null;
            UnavailableReason = reason;
            if (IsToggle)
            {
                IsOn = state.OptionKey switch { TweakDefinition.On => true, TweakDefinition.Off => false, _ => null };
                StateText = state.Partial ? L("Partiellement appliqué") : state.Unknown ? L("État inconnu") : null;
            }
            else if (IsChoice)
            {
                SelectedOption = state.OptionKey is null ? null : Definition.GetOption(state.OptionKey);
                StateText = state.Partial ? L("Partiellement appliqué") : state.OptionKey is null ? L("Configuration personnalisée") : null;
            }
            var recommended = Definition.RecommendationFor(AppHost.Profile);
            RecommendationText = recommended is not null && recommended != state.OptionKey && !IsAction && Definition.GetOption(recommended) is { } ro
                ? L("Recommandé pour ce PC : {0}", ro.Label)
                : null;
        }
        finally { _suppress = false; }
    }

    /// <summary>Libellé affiché à gauche de l'interrupteur (« Activé » / « Désactivé »…).</summary>
    public string ToggleLabel => IsOn switch
    {
        true => Definition.GetOption(TweakDefinition.On)?.Label ?? L("Activé"),
        false => Definition.GetOption(TweakDefinition.Off)?.Label ?? L("Désactivé"),
        _ => "—",
    };

    public string ActionLabel => Definition.Options.FirstOrDefault()?.Label ?? L("Exécuter");

    partial void OnIsOnChanged(bool? oldValue, bool? newValue)
    {
        OnPropertyChanged(nameof(ToggleLabel));
        if (_suppress || newValue is null || oldValue == newValue) return;
        _ = ApplyAsync(newValue.Value ? TweakDefinition.On : TweakDefinition.Off);
    }

    partial void OnSelectedOptionChanged(TweakOption? oldValue, TweakOption? newValue)
    {
        if (_suppress || newValue is null || ReferenceEquals(oldValue, newValue)) return;
        _ = ApplyAsync(newValue.Key);
    }

    [RelayCommand]
    private Task RunAsync() => ApplyAsync(TweakDefinition.Run);

    [RelayCommand]
    private void ToggleDetails() => ShowDetails = !ShowDetails;

    [RelayCommand]
    private async Task ApplyRecommendedAsync()
    {
        if (Definition.RecommendationFor(AppHost.Profile) is { } key) await ApplyAsync(key);
    }

    /// <summary>
    /// Confirmation exigée avant d'appliquer un réglage à risque, avec avertissement ou non annulable. Partagée par
    /// toutes les façons d'appliquer un réglage isolé (carte, bouton direct de la recherche). Vrai = on peut appliquer.
    /// </summary>
    public static Task<bool> ConfirmIfNeededAsync(TweakDefinition definition, TweakOption option)
    {
        if (definition.Risk == RiskLevel.Safe && definition.Warning is null && definition.IsReversible) return Task.FromResult(true);
        var text = definition.Warning is null
            ? L("{0}\n\nChoix : {1}", definition.Description, option.Label)
            : L("{0}\n\n⚠ {1}\n\nChoix : {2}", definition.Description, definition.Warning, option.Label);
        return AppHost.Dialogs.ConfirmAsync(definition.Title, text, L("Appliquer"), L("Annuler"), definition.Risk == RiskLevel.Advanced);
    }

    public async Task ApplyAsync(string optionKey)
    {
        if (IsBusy) return;
        var option = Definition.GetOption(optionKey);
        if (option is null) return;

        if (!await ConfirmIfNeededAsync(Definition, option))
        {
            await RefreshAsync();
            return;
        }

        IsBusy = true;
        try
        {
            var outcome = await AppHost.Engine.ApplyAsync(Definition, optionKey);
            AppHost.Toasts.ShowOutcome(outcome);
        }
        finally
        {
            IsBusy = false;
            await RefreshAsync();
        }
    }
}
