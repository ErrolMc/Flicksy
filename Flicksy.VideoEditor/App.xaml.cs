using System;
using System.IO;
using System.Linq;
using System.Windows;
using Flicksy.Drawing.Media;
using Flicksy.VideoEditor.ViewModels;
using Flicksy.VideoEditor.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Flicksy.VideoEditor;

public partial class App : Application
{
    private const string NewVideoProjectArgName = "--new-video-project";

    private IHost? _host;

    public static IServiceProvider Services =>
        ((App)Current)._host?.Services
            ?? throw new InvalidOperationException("Host has not been initialized.");

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
                "Flicksy.VideoEditor",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        // Factory registrations: a fresh empty Project per resolve, propagated up
        // through the VM into the window. EditorWithSource bypasses DI and uses
        // Project.CreateFromSourceFile() directly so the source path can flow in.
        builder.Services.AddTransient(_ => Project.Project.CreateEmpty());
        builder.Services.AddTransient<VideoEditorViewModel>();
        builder.Services.AddTransient<VideoEditorWindow>();
        builder.Services.AddTransient<WelcomeWindow>();
        _host = builder.Build();
        _host.Start();

        // Read-once kill switch (ADR 0010), pushed into the Drawing library because the library
        // itself reads no configuration. Must be set before the first preview render constructs
        // a decoder — EditorWithSource composites during window construction below.
        IConfiguration configuration = _host.Services.GetRequiredService<IConfiguration>();
        HardwareMediaDecoder.Disabled =
            bool.TryParse(configuration["DisableHardwareDecode"], out bool disableHwDecode) && disableHwDecode;

        StartupMode mode = ResolveStartupMode(e.Args);
        Window window = mode switch
        {
            StartupMode.EmptyEditor => _host.Services.GetRequiredService<VideoEditorWindow>(),
            StartupMode.EditorWithSource src => new VideoEditorWindow(
                new VideoEditorViewModel(Project.Project.CreateFromSourceFile(src.Path)), src.Path),
            _ => _host.Services.GetRequiredService<WelcomeWindow>(),
        };

        MainWindow = window;
        window.Show();
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

    public static StartupMode ResolveStartupMode(string[] args)
    {
        if (args.Length == 0)
        {
            return new StartupMode.Welcome();
        }

        if (args.Any(a => a.Equals(NewVideoProjectArgName, StringComparison.OrdinalIgnoreCase)))
        {
            return new StartupMode.EmptyEditor();
        }

        if (TryValidatePath(args[0], out var validatedPath))
        {
            return new StartupMode.EditorWithSource(validatedPath);
        }

        return new StartupMode.Welcome();
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
