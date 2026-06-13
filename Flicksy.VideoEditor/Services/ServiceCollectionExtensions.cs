using Flicksy.Drawing.Undo;
using Flicksy.VideoEditor.Composition;
using Flicksy.VideoEditor.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Flicksy.VideoEditor.Services;

/// <summary>
/// The video editor's composition root — one place that registers everything the DI
/// container provides, keeping <c>App.OnStartup</c> lean and the wiring greppable/testable.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVideoEditorServices(this IServiceCollection services)
    {
        // Process-wide config, no per-document/per-window identity → Singleton.
        services.AddSingleton<ISettingsService, SettingsService>();

        // Builds the editor window around a runtime-chosen Project. Stateless (wraps the
        // provider) → Singleton.
        services.AddSingleton<IEditorFactory, EditorFactory>();

        // --- Per-document shared collaborators ---
        // These take no Project, so a process Singleton is correct today (one document per
        // process). The runtime Project is deliberately NOT registered: it flows in through
        // IEditorFactory to the root VM, which composes the per-document sub-VMs around it. When
        // tabs/MDI land, these become Scoped (one set per document) and IEditorFactory creates +
        // disposes a scope per editor window — only the lifetimes and that scope boundary change,
        // not the call sites.
        services.AddSingleton<UndoManager>();
        services.AddSingleton<IUndoService>(sp => sp.GetRequiredService<UndoManager>());
        services.AddSingleton<OverlayHostViewModel>();
        services.AddSingleton<IOverlayService>(sp => sp.GetRequiredService<OverlayHostViewModel>());
        services.AddSingleton<ICompositor, SkiaCompositor>();
        services.AddSingleton<ProjectSettingsService>();
        services.AddSingleton<IProjectSettingsService>(sp => sp.GetRequiredService<ProjectSettingsService>());
        return services;
    }
}
