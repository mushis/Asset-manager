using System.Diagnostics;
using System.Runtime.Versioning;

namespace QuakeAssetManager.Core;

/// <summary>Sends files and folders to the Recycle Bin (Windows) or the desktop trash (Linux).</summary>
public static class Trash
{
    public static void Send(string path)
    {
        if (OperatingSystem.IsWindows())
            SendWindows(path);
        else
            SendFreedesktop(path);
    }

    [SupportedOSPlatform("windows")]
    private static void SendWindows(string path)
    {
        if (Directory.Exists(path))
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(path,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        else
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
    }

    /// <summary>The freedesktop.org trash spec: move into ~/.local/share/Trash/files and write a .trashinfo.</summary>
    private static void SendFreedesktop(string path)
    {
        var fullPath = Path.GetFullPath(path);
        try
        {
            var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (string.IsNullOrEmpty(dataHome))
                dataHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
            var files = Path.Combine(dataHome, "Trash", "files");
            var info = Path.Combine(dataHome, "Trash", "info");
            Directory.CreateDirectory(files);
            Directory.CreateDirectory(info);

            var name = Path.GetFileName(fullPath.TrimEnd('/'));
            var target = name;
            for (var i = 2; File.Exists(Path.Combine(files, target)) || Directory.Exists(Path.Combine(files, target)) ||
                            File.Exists(Path.Combine(info, target + ".trashinfo")); i++)
                target = $"{name}.{i}";

            File.WriteAllText(Path.Combine(info, target + ".trashinfo"),
                $"[Trash Info]\nPath={Uri.EscapeDataString(fullPath).Replace("%2F", "/")}\nDeletionDate={DateTime.Now:yyyy-MM-ddTHH:mm:ss}\n");
            if (Directory.Exists(fullPath))
                Directory.Move(fullPath, Path.Combine(files, target));
            else
                File.Move(fullPath, Path.Combine(files, target));
        }
        catch (IOException)
        {
            // Different filesystem: let gio handle it (it knows about per-volume trash folders).
            using var gio = Process.Start(new ProcessStartInfo("gio", ["trash", fullPath]) { UseShellExecute = false });
            gio?.WaitForExit(10_000);
            if (gio is null || gio.ExitCode != 0 || File.Exists(fullPath) || Directory.Exists(fullPath))
                throw new IOException($"Couldn't move {fullPath} to the trash.");
        }
    }
}
