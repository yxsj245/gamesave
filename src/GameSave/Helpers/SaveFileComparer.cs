using System.Formats.Tar;
using GameSave.Models;

namespace GameSave.Helpers;

public static class SaveFileComparer
{
    public static async Task<SaveConflictReport> CompareAsync(
        string tarPath,
        List<string> currentSavePaths,
        string gameId,
        string gameName)
    {
        var report = new SaveConflictReport
        {
            GameId = gameId,
            GameName = gameName
        };

        if (!File.Exists(tarPath))
            return report;

        var resolvedPaths = currentSavePaths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(PathEnvironmentHelper.ExpandEnvVariables)
            .ToList();

        if (resolvedPaths.Count == 0)
            return report;

        bool isMultiFormat = await IsMultiDirectoryFormatAsync(tarPath);

        var exitSaveFiles = await ReadTarEntriesAsync(tarPath, isMultiFormat, resolvedPaths);
        report.FileCountInExitSave = exitSaveFiles.Count;

        var currentSaveFiles = ScanCurrentSaveFiles(resolvedPaths);
        report.FileCountInCurrentSave = currentSaveFiles.Count;

        foreach (var exitFile in exitSaveFiles)
        {
            var found = currentSaveFiles.FirstOrDefault(c =>
                c.SourceIndex == exitFile.SourceIndex &&
                string.Equals(c.RelativePath, exitFile.RelativePath, StringComparison.OrdinalIgnoreCase));

            if (found == null)
            {
                report.Conflicts.Add(new ConflictFileInfo
                {
                    RelativePath = FormatConflictPath(exitFile.SourceIndex, exitFile.RelativePath, resolvedPaths.Count),
                    SourceDescription = exitFile.SourceDescription,
                    ExitSaveModified = exitFile.ModifiedTime,
                    CurrentSaveModified = DateTime.MinValue,
                    ExitSaveSize = exitFile.Size,
                    CurrentSaveSize = 0
                });
                report.OnlyInExitSaveCount++;
            }
            else
            {
                found.Matched = true;

                if (exitFile.ModifiedTime != found.ModifiedTime || exitFile.Size != found.Size)
                {
                    report.Conflicts.Add(new ConflictFileInfo
                    {
                        RelativePath = FormatConflictPath(exitFile.SourceIndex, exitFile.RelativePath, resolvedPaths.Count),
                        SourceDescription = exitFile.SourceDescription,
                        ExitSaveModified = exitFile.ModifiedTime,
                        CurrentSaveModified = found.ModifiedTime,
                        ExitSaveSize = exitFile.Size,
                        CurrentSaveSize = found.Size
                    });
                    report.ModifiedCount++;
                }
            }
        }

        foreach (var currentFile in currentSaveFiles.Where(c => !c.Matched))
        {
            report.Conflicts.Add(new ConflictFileInfo
            {
                RelativePath = FormatConflictPath(currentFile.SourceIndex, currentFile.RelativePath, resolvedPaths.Count),
                SourceDescription = currentFile.SourceDescription,
                ExitSaveModified = DateTime.MinValue,
                CurrentSaveModified = currentFile.ModifiedTime,
                ExitSaveSize = 0,
                CurrentSaveSize = currentFile.Size
            });
            report.OnlyInCurrentSaveCount++;
        }

        System.Diagnostics.Debug.WriteLine(
            $"[存档对比] {gameName}: 退出存档 {report.FileCountInExitSave} 文件, " +
            $"当前存档 {report.FileCountInCurrentSave} 文件, " +
            $"{report.Conflicts.Count} 个冲突 " +
            $"(仅退出:{report.OnlyInExitSaveCount}, 仅当前:{report.OnlyInCurrentSaveCount}, 修改:{report.ModifiedCount})");

        return report;
    }

    private static string FormatConflictPath(int sourceIndex, string relativePath, int totalSources)
    {
        if (totalSources <= 1)
            return relativePath;
        return $"[存档路径{sourceIndex + 1}] {relativePath}";
    }

    private static async Task<bool> IsMultiDirectoryFormatAsync(string tarPath)
    {
        try
        {
            await using var fs = new FileStream(tarPath, FileMode.Open, FileAccess.Read);
            await using var reader = new TarReader(fs);

            while (await reader.GetNextEntryAsync() is { } entry)
            {
                var name = entry.Name.Replace('\\', '/').TrimEnd('/');
                var firstSlash = name.IndexOf('/');
                var topDir = firstSlash > 0 ? name[..firstSlash] : name;

                if (int.TryParse(topDir, out _))
                    return true;

                return false;
            }
        }
        catch { }
        return false;
    }

    private static async Task<List<ScannedFileEntry>> ReadTarEntriesAsync(
        string tarPath,
        bool isMultiFormat,
        List<string> resolvedPaths)
    {
        var entries = new List<ScannedFileEntry>();

        await using var fs = new FileStream(tarPath, FileMode.Open, FileAccess.Read);
        await using var reader = new TarReader(fs);

        while (await reader.GetNextEntryAsync() is { } entry)
        {
            if (entry.EntryType != TarEntryType.RegularFile)
                continue;

            var entryName = entry.Name.Replace('\\', '/').TrimStart('/');

            int sourceIndex = 0;
            string relativePath;

            if (isMultiFormat)
            {
                var firstSlash = entryName.IndexOf('/');
                if (firstSlash < 0) continue;
                var prefix = entryName[..firstSlash];
                if (!int.TryParse(prefix, out sourceIndex) || sourceIndex < 0 || sourceIndex >= resolvedPaths.Count)
                    continue;
                relativePath = entryName[(firstSlash + 1)..];
            }
            else
            {
                relativePath = entryName;
            }

            if (string.IsNullOrEmpty(relativePath))
                continue;

            var savePath = resolvedPaths[sourceIndex];
            var sourceDesc = GetSourceDescription(sourceIndex, savePath, resolvedPaths.Count);

            entries.Add(new ScannedFileEntry
            {
                SourceIndex = sourceIndex,
                RelativePath = relativePath,
                ModifiedTime = entry.ModificationTime.LocalDateTime,
                Size = entry.Length,
                SourceDescription = sourceDesc
            });
        }

        return entries;
    }

    private static List<ScannedFileEntry> ScanCurrentSaveFiles(List<string> resolvedPaths)
    {
        var entries = new List<ScannedFileEntry>();

        for (int i = 0; i < resolvedPaths.Count; i++)
        {
            var path = resolvedPaths[i];
            var sourceDesc = GetSourceDescription(i, path, resolvedPaths.Count);

            if (File.Exists(path) && !Directory.Exists(path))
            {
                var fileInfo = new FileInfo(path);
                entries.Add(new ScannedFileEntry
                {
                    SourceIndex = i,
                    RelativePath = Path.GetFileName(path),
                    ModifiedTime = fileInfo.LastWriteTime,
                    Size = fileInfo.Length,
                    SourceDescription = sourceDesc
                });
            }
            else if (Directory.Exists(path))
            {
                ScanDirectoryRecursive(path, "", i, sourceDesc, entries);
            }
        }

        return entries;
    }

    private static void ScanDirectoryRecursive(
        string basePath,
        string relativePath,
        int sourceIndex,
        string sourceDesc,
        List<ScannedFileEntry> entries)
    {
        var currentDir = string.IsNullOrEmpty(relativePath)
            ? basePath
            : Path.Combine(basePath, relativePath);

        if (!Directory.Exists(currentDir))
            return;

        foreach (var filePath in Directory.GetFiles(currentDir))
        {
            var fileName = Path.GetFileName(filePath);
            var fileRelativePath = string.IsNullOrEmpty(relativePath)
                ? fileName
                : Path.Combine(relativePath, fileName).Replace('\\', '/');

            var fileInfo = new FileInfo(filePath);
            entries.Add(new ScannedFileEntry
            {
                SourceIndex = sourceIndex,
                RelativePath = fileRelativePath,
                ModifiedTime = fileInfo.LastWriteTime,
                Size = fileInfo.Length,
                SourceDescription = sourceDesc
            });
        }

        foreach (var dirPath in Directory.GetDirectories(currentDir))
        {
            var dirName = Path.GetFileName(dirPath);
            var newRelativePath = string.IsNullOrEmpty(relativePath)
                ? dirName
                : Path.Combine(relativePath, dirName);

            ScanDirectoryRecursive(basePath, newRelativePath, sourceIndex, sourceDesc, entries);
        }
    }

    private static string GetSourceDescription(int index, string path, int totalSources)
    {
        if (totalSources > 1)
            return $"存档路径 {index + 1}";
        return Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, '/'));
    }

    private class ScannedFileEntry
    {
        public int SourceIndex { get; set; }
        public string RelativePath { get; set; } = string.Empty;
        public DateTime ModifiedTime { get; set; }
        public long Size { get; set; }
        public string SourceDescription { get; set; } = string.Empty;
        public bool Matched { get; set; }
    }
}
