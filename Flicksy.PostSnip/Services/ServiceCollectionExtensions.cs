using Microsoft.Extensions.DependencyInjection;

namespace Flicksy.PostSnip.Services;

/// <summary>
/// PostSnip's composition root — mirrors the video editor's <c>AddVideoEditorServices</c> so the
/// two editors register their services the same way. Keeps <c>App.OnStartup</c> lean.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPostSnipServices(this IServiceCollection services)
    {
        // Process-wide config behind a service (mirrors the video editor) → Singleton.
        services.AddSingleton<ISettingsService, SettingsService>();
        return services;
    }
}
