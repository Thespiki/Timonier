using System.Windows.Markup;

namespace Timonier.Core.Localization;

/// <summary>
/// Texte traduit dans le XAML : <c>Text="{loc:L 'Paramètres'}"</c> ou <c>{loc:L Text='Ouvrir', Context=verbe}</c>,
/// avec <c>xmlns:loc="clr-namespace:Timonier.Core.Localization"</c>. Résolu une fois, au chargement du XAML.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LExtension : MarkupExtension
{
    public LExtension() { }

    public LExtension(string text) => Text = text;

    [ConstructorArgument("text")]
    public string Text { get; set; } = "";

    public string? Context { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        Context is { Length: > 0 } ctx ? Loc.LC(ctx, Text) : Loc.L(Text);
}
