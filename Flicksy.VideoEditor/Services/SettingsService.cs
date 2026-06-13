using Microsoft.Extensions.Configuration;

namespace Flicksy.VideoEditor.Services;

/// <summary>
/// <see cref="ISettingsService"/> backed by the host's <see cref="IConfiguration"/>
/// (appsettings.json). Values are read once in the constructor, keeping the repo's
/// no-live-config convention (settings don't change after startup).
/// </summary>
internal sealed class SettingsService : ISettingsService
{
    public SettingsService(IConfiguration configuration)
    {
        DisableHardwareDecode =
            bool.TryParse(configuration["DisableHardwareDecode"], out bool disable) && disable;
    }

    public bool DisableHardwareDecode { get; }
}
