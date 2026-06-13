using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace Flicksy.Drawing.Controls;

/// <summary>
/// Convention-based view-model → view resolver for WPF content hosts. Maps a VM instance to
/// its view by name — <c>{Name}ViewModel</c> → <c>{Name}View</c> — resolved by simple type
/// name in the configured view assemblies, and returns a <see cref="DataTemplate"/> that
/// instantiates that view (the hosting <see cref="ContentPresenter"/> sets its
/// <c>DataContext</c> to the VM). Assign to a <c>ContentTemplateSelector</c> (e.g. the shell's
/// OverlayHost) so showing a VM spawns the matching view.
///
/// Resolution is by simple name within explicit assemblies rather than by rewriting the VM's
/// namespace, because shared VMs (e.g. <c>Flicksy.Drawing.ViewModels.*</c>) have their views in
/// the consuming app's assembly — no namespace rule maps one to the other. Each app builds its
/// own locator; the parameterless default searches the app exe (where its views live) plus the
/// Drawing library (shared views). Lookups are cached per VM type, including misses.
/// </summary>
public sealed class ViewLocator : DataTemplateSelector
{
    private const string ViewModelSuffix = "ViewModel";
    private const string ViewSuffix = "View";

    private readonly Dictionary<Type, DataTemplate?> _cache = new();
    private readonly Assembly[] _viewAssemblies;

    /// <summary>
    /// Parameterless ctor for XAML instantiation (e.g. as an <c>OverlayHost</c> resource). XAML
    /// constructs resources by reflection and needs a real zero-arg constructor — a <c>params</c>
    /// ctor does not count. Defaults to the app exe + Drawing assemblies.
    /// </summary>
    public ViewLocator()
        : this(Array.Empty<Assembly>())
    {
    }

    public ViewLocator(params Assembly[] viewAssemblies)
    {
        if (viewAssemblies.Length > 0)
        {
            _viewAssemblies = viewAssemblies;
            return;
        }

        // Entry assembly = the app exe, where each app's views live; plus this (Drawing)
        // assembly for shared views. Entry can be null under the XAML designer / a test host.
        Assembly? entry = Assembly.GetEntryAssembly();
        _viewAssemblies = entry is not null
            ? new[] { entry, typeof(ViewLocator).Assembly }
            : new[] { typeof(ViewLocator).Assembly };
    }

    public override DataTemplate? SelectTemplate(object? item, DependencyObject container)
    {
        if (item is null)
            return null;

        Type viewModelType = item.GetType();
        if (_cache.TryGetValue(viewModelType, out DataTemplate? cached))
            return cached;

        DataTemplate? template = BuildTemplate(viewModelType);
        _cache[viewModelType] = template;
        return template;
    }

    private DataTemplate? BuildTemplate(Type viewModelType)
    {
        Type? viewType = ResolveViewType(viewModelType);
        if (viewType is null)
            return null;

        var template = new DataTemplate(viewModelType)
        {
            VisualTree = new FrameworkElementFactory(viewType),
        };
        template.Seal();
        return template;
    }

    private Type? ResolveViewType(Type viewModelType)
    {
        string name = viewModelType.Name;
        if (!name.EndsWith(ViewModelSuffix, StringComparison.Ordinal))
            return null;

        string viewName = string.Concat(name.AsSpan(0, name.Length - ViewModelSuffix.Length), ViewSuffix);

        foreach (Assembly assembly in _viewAssemblies)
        {
            foreach (Type type in assembly.GetTypes())
            {
                if (string.Equals(type.Name, viewName, StringComparison.Ordinal)
                    && typeof(FrameworkElement).IsAssignableFrom(type))
                {
                    return type;
                }
            }
        }

        return null;
    }
}
