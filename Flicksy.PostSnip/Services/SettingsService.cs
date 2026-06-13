using Microsoft.Extensions.Configuration;

namespace Flicksy.PostSnip.Services;

/// <summary>
/// <see cref="ISettingsService"/> backed by the host's <see cref="IConfiguration"/>
/// (appsettings.json), read once in the constructor — the repo's no-live-config convention.
/// </summary>
internal sealed class SettingsService : ISettingsService
{
    public SettingsService(IConfiguration configuration)
    {
        LaunchPostSnipWithFilePath = configuration["LaunchPostSnipWithFilePath"];
    }

    public string? LaunchPostSnipWithFilePath { get; }
}
