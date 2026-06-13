using System;
using Flicksy.VideoEditor.ViewModels;
using Flicksy.VideoEditor.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace Flicksy.VideoEditor.Services;

/// <summary>
/// <see cref="IEditorFactory"/> that picks the project per the request and builds the VM with
/// <see cref="ActivatorUtilities"/> — DI fills every constructor dependency except the runtime
/// project supplied positionally — then constructs the window around it. When per-document
/// scoping lands (tabs/MDI), this is the one place that would create a scope, seed the project,
/// and tie scope disposal to window close; the signature does not change.
/// </summary>
internal sealed class EditorFactory : IEditorFactory
{
    private readonly IServiceProvider _serviceProvider;

    public EditorFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public VideoEditorWindow Create(EditorRequest request)
    {
        Project.Project project = request switch
        {
            EditorRequest.Empty => Project.Project.CreateEmpty(),
            EditorRequest.FromSourceFile fromSource => Project.Project.CreateFromSourceFile(fromSource.Path),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request, "Unknown editor request."),
        };

        // Point the shared project-settings service at the chosen document before the VM (and its
        // title bar) resolve, so Project Settings reads this project's settings.
        _serviceProvider.GetRequiredService<ProjectSettingsService>().Current = project.Settings;

        VideoEditorViewModel viewModel = ActivatorUtilities.CreateInstance<VideoEditorViewModel>(_serviceProvider, project);
        string? sourcePath = (request as EditorRequest.FromSourceFile)?.Path;
        return new VideoEditorWindow(viewModel, sourcePath);
    }
}
