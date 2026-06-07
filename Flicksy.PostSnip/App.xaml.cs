using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Flicksy.Drawing.Media;
using Flicksy.Drawing.ViewModels;
using Flicksy.PostSnip.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Flicksy.PostSnip;

public partial class App : Application
{
    private const string LaunchFilePathArgName = "--launch-file";

    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            FfmpegLocator.Initialize();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"FFmpeg initialization failed:\n{ex.Message}\n\nThe application will exit.",
                "Flicksy.PostSnip",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Services.AddTransient<IVideoPlayer, FFmpegVideoPlayer>();
        builder.Services.AddTransient<ImageEditToolsViewModel>();
        builder.Services.AddTransient<DrawingViewModel>();
        builder.Services.AddTransient<CropOverlayViewModel>();
        builder.Services.AddTransient<PostSnipViewModel>();
        builder.Services.AddTransient<PostSnipWindow>();

        _host = builder.Build();
        _host.Start();

        var window = _host.Services.GetRequiredService<PostSnipWindow>();
        var configuration = _host.Services.GetRequiredService<IConfiguration>();
        string? mediaPath = ResolveStartupMediaPath(e.Args, configuration);

        MainWindow = window;
        window.Show();

        if (!string.IsNullOrWhiteSpace(mediaPath))
        {
            QueueMediaLoad(window, mediaPath);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            _host.StopAsync().GetAwaiter().GetResult();
            _host.Dispose();
            _host = null;
        }

        base.OnExit(e);
    }

    private static void QueueMediaLoad(PostSnipWindow window, string mediaPath)
    {
        window.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                window.ViewModel.PreserveMediaFile = true;
                string extension = Path.GetExtension(mediaPath).ToLowerInvariant();
                if (IsVideoExtension(extension))
                {
                    await window.ViewModel.LoadVideoAsync(mediaPath).ConfigureAwait(true);
                }
                else
                {
                    window.ViewModel.LoadImage(mediaPath);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    window,
                    $"Failed to load media:\n{ex.Message}",
                    "Flicksy.PostSnip",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }, DispatcherPriority.Background);
    }

    private static bool IsVideoExtension(string extension)
    {
        return extension is ".mp4" or ".mov" or ".avi" or ".wmv" or ".mkv";
    }

    private static string? ResolveStartupMediaPath(string[] args, IConfiguration configuration)
    {
        string? argumentPath = GetLaunchFilePathFromArguments(args) ?? args.FirstOrDefault();
        if (TryValidatePath(argumentPath, out var validatedFromArgs))
        {
            return validatedFromArgs;
        }

        return TryValidatePath(configuration["LaunchPostSnipWithFilePath"], out var validatedFromSettings)
            ? validatedFromSettings
            : null;
    }

    private static string? GetLaunchFilePathFromArguments(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].Equals(LaunchFilePathArgName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (i + 1 >= args.Length)
            {
                return null;
            }

            return args[i + 1];
        }

        return null;
    }

    private static bool TryValidatePath(string? rawPath, out string validatedPath)
    {
        validatedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return false;
        }

        try
        {
            string fullPath = Path.GetFullPath(rawPath.Trim());
            if (!File.Exists(fullPath))
            {
                return false;
            }

            validatedPath = fullPath;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
