using System.Windows.Controls;
using PcPilot.Core.Catalog;
using PcPilot.UI.Controls;
using PcPilot.UI.Services;

namespace PcPilot.UI.Pages;

/// <summary>Page générique : affiche tous les réglages d'une catégorie.</summary>
public sealed class CategoryPage : UserControl, INavigationAware
{
    private readonly TweakListView _list;

    public CategoryPage(CategoryInfo category)
    {
        var stack = PageScaffold.Create(this, category.Title, category.Description, category.Glyph);
        _list = new TweakListView(category.Id);
        stack.Children.Add(_list);
    }

    public void OnNavigatedTo(object? parameter)
    {
        if (parameter is string s && s.StartsWith("tweak:", StringComparison.Ordinal))
            _list.Highlight(s["tweak:".Length..]);
    }
}
