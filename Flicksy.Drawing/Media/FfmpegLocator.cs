
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Flicksy.Drawing.Media;

public static class FfmpegLocator
{
    public static void Initialize()
    {
        var path = LocateFfmpegBinDirectory()
            ?? throw new InvalidOperationException(
                "Could not locate FFmpeg shared libraries (avcodec-*.dll). " +
                "Install a 'shared' FFmpeg build (e.g. `winget install Gyan.FFmpeg.Shared --version 7.1.1`), " +
                "set the FFMPEG_HOME environment variable, or place the DLLs in 'lib\\ffmpeg' next to the application.");

        FFMediaToolkit.FFmpegLoader.FFmpegPath = path;
    }

    private static string? LocateFfmpegBinDirectory()
    {
        return EnumerateCandidates()
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(HasSharedLibraries);
    }

    private static IEnumerable<string?> EnumerateCandidates()
    {
        // 1. Explicit env var override.
        string? home = Environment.GetEnvironmentVariable("FFMPEG_HOME");
        if (!string.IsNullOrWhiteSpace(home))
        {
            yield return home;
            yield return Path.Combine(home, "bin");
        }

        // 2. Every directory on PATH.
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathEnv))
        {
            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    yield return dir;
                }
            }
        }

        // 3. WinGet shared FFmpeg packages (probed because winget links may not put bin/ on PATH).
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            string wingetRoot = Path.Combine(localAppData, "Microsoft", "WinGet", "Packages");
            foreach (var candidate in EnumerateWingetSharedFfmpeg(wingetRoot))
            {
                yield return candidate;
            }
        }

        // 4. Common install paths.
        yield return @"C:\ffmpeg\bin";
        yield return @"C:\ProgramData\chocolatey\lib\ffmpeg\tools\ffmpeg\bin";
        yield return @"C:\ProgramData\chocolatey\lib\ffmpeg-shared\tools\ffmpeg-shared\bin";

        // 5. App-local fallback.
        yield return Path.Combine(AppContext.BaseDirectory, "lib", "ffmpeg");
    }

    private static IEnumerable<string> EnumerateWingetSharedFfmpeg(string root)
    {
        if (!SafeDirectoryExists(root))
        {
            yield break;
        }

        string[] packageDirs;
        try
        {
            packageDirs = Directory.GetDirectories(root)
                .Where(d => Path.GetFileName(d).Contains("FFmpeg.Shared", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }
        catch
        {
            yield break;
        }

        foreach (string packageDir in packageDirs)
        {
            string[] versionDirs;
            try
            {
                versionDirs = Directory.GetDirectories(packageDir);
            }
            catch
            {
                continue;
            }

            foreach (var versionDir in versionDirs)
            {
                yield return Path.Combine(versionDir, "bin");
                yield return versionDir;
            }
        }
    }

    private static bool HasSharedLibraries(string directory)
    {
        if (!SafeDirectoryExists(directory))
        {
            return false;
        }

        try
        {
            return Directory.EnumerateFiles(directory, "avcodec-*.dll").Any();
        }
        catch
        {
            return false;
        }
    }

    private static bool SafeDirectoryExists(string directory)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory);
        }
        catch
        {
            return false;
        }
    }
}
