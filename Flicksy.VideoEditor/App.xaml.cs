using System;
using System.IO;
using System.Linq;
using System.Windows;
using Flicksy.Drawing.Media;
using Flicksy.VideoEditor.Services;
using Flicksy.VideoEditor.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Flicksy.VideoEditor;

public partial class App : Application
{
    private const string NewVideoProjectArgName = "--new-video-project";

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
                "Flicksy.VideoEditor",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        // Services + IEditorFactory, which builds the editor window around a runtime-chosen
        // Project. The editor is never resolved directly — the factory owns its construction so
        // empty / from-source / (future) from-saved all flow through one DI-built path; only the
        // argless Welcome window is resolved straight from the container.
        builder.Services.AddVideoEditorServices();
        builder.Services.AddTransient<WelcomeWindow>();
        _host = builder.Build();
        _host.Start();

        // Decode preference is applied once at startup (the ADR 0010 kill switch is set-once) and
        // pushed into the Drawing library, which reads no settings itself. Must be set before the
        // first preview render constructs a decoder — EditorWithSource composites during window
        // construction below. Changing it in Settings takes effect on the next launch.
        ISettingsService settings = _host.Services.GetRequiredService<ISettingsService>();
        HardwareMediaDecoder.Disabled = !settings.Current.UseHardwareDecode;

        IEditorFactory editorFactory = _host.Services.GetRequiredService<IEditorFactory>();
        StartupMode mode = ResolveStartupMode(e.Args);
        Window window = mode switch
        {
            StartupMode.EmptyEditor => editorFactory.Create(new EditorRequest.Empty()),
            StartupMode.EditorWithSource src => editorFactory.Create(new EditorRequest.FromSourceFile(src.Path)),
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
