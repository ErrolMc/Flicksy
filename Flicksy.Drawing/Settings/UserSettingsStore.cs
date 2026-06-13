using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Flicksy.Drawing.Settings;

/// <summary>
/// Reads/writes small per-app JSON preference files under <c>%LOCALAPPDATA%\Flicksy\</c> —
/// the writable counterpart to each exe's shipped <c>appsettings.json</c>. App/dev config is
/// read once from the output dir; user preferences live here where the running app rewrites
/// them. Both editor exes share this one primitive (Drawing is the only library both
/// reference), so the persistence mechanism is identical across them, not duplicated.
///
/// <para><see cref="Load{T}"/> never throws: a missing or corrupt file yields defaults so a
/// bad hand-edit can't brick startup. <see cref="Save{T}"/> writes atomically (temp file then
/// move) and swallows IO failures, mirroring the best-effort posture of the hardware-decode
/// fallback — a read-only disk shouldn't crash the app.</para>
/// </summary>
public static class UserSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Loads <typeparamref name="T"/> from <paramref name="fileName"/>, or a fresh default
    /// instance when the file is absent or unreadable.
    /// </summary>
    public static T Load<T>(string fileName)
        where T : new()
    {
        string path = ResolvePath(fileName);
        try
        {
            if (!File.Exists(path))
                return new T();

            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, Options) ?? new T();
        }
        catch (Exception exception)
        {
            // Missing/corrupt/locked — defaults beat a crash on a file the user might hand-edit.
            Debug.WriteLine($"[settings] load failed for '{path}': {exception.Message}");
            return new T();
        }
    }

    /// <summary>
    /// Persists <paramref name="value"/> to <paramref name="fileName"/>. Best-effort: IO
    /// failures are logged and swallowed.
    /// </summary>
    public static void Save<T>(string fileName, T value)
    {
        string path = ResolvePath(fileName);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string json = JsonSerializer.Serialize(value, Options);

            // Write a sibling temp file then move it over the target, so a crash mid-write can't
            // leave a half-written (corrupt) settings file.
            string tempPath = path + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, path, overwrite: true);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[settings] save failed for '{path}': {exception.Message}");
        }
    }

    /// <summary>The absolute path of a settings file under <c>%LOCALAPPDATA%\Flicksy\</c>.</summary>
    public static string ResolvePath(string fileName)
    {
        string baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Flicksy");
        return Path.Combine(baseDir, fileName);
    }
}
