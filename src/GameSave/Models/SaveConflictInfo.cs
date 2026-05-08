namespace GameSave.Models;

public class ConflictFileInfo
{
    public string RelativePath { get; set; } = string.Empty;
    public string SourceDescription { get; set; } = string.Empty;
    public DateTime ExitSaveModified { get; set; }
    public DateTime CurrentSaveModified { get; set; }
    public long ExitSaveSize { get; set; }
    public long CurrentSaveSize { get; set; }

    public bool IsNewerInCurrent => CurrentSaveModified > ExitSaveModified;
    public bool IsNewerInExitSave => ExitSaveModified > CurrentSaveModified;
    public bool HasModificationDiff => ExitSaveModified != CurrentSaveModified;
    public bool HasSizeDiff => ExitSaveSize != CurrentSaveSize;

    public string DisplayExitSaveTime => ExitSaveModified.ToString("yyyy-MM-dd HH:mm:ss");
    public string DisplayCurrentSaveTime => CurrentSaveModified.ToString("yyyy-MM-dd HH:mm:ss");

    public string DisplayExitSaveSize
    {
        get
        {
            if (ExitSaveSize < 1024) return $"{ExitSaveSize} B";
            if (ExitSaveSize < 1024 * 1024) return $"{ExitSaveSize / 1024.0:F1} KB";
            if (ExitSaveSize < 1024 * 1024 * 1024) return $"{ExitSaveSize / (1024.0 * 1024.0):F1} MB";
            return $"{ExitSaveSize / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }
    }

    public string DisplayCurrentSaveSize
    {
        get
        {
            if (CurrentSaveSize < 1024) return $"{CurrentSaveSize} B";
            if (CurrentSaveSize < 1024 * 1024) return $"{CurrentSaveSize / 1024.0:F1} KB";
            if (CurrentSaveSize < 1024 * 1024 * 1024) return $"{CurrentSaveSize / (1024.0 * 1024.0):F1} MB";
            return $"{CurrentSaveSize / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }
    }

    public string DiffSummary
    {
        get
        {
            var parts = new List<string>();
            if (IsNewerInCurrent) parts.Add("当前存档更新");
            else if (IsNewerInExitSave) parts.Add("退出存档更新");
            if (HasSizeDiff) parts.Add("大小不同");
            return parts.Count > 0 ? string.Join("，", parts) : "一致";
        }
    }
}

public class SaveConflictReport
{
    public string GameId { get; set; } = string.Empty;
    public string GameName { get; set; } = string.Empty;
    public List<ConflictFileInfo> Conflicts { get; set; } = new();
    public bool HasConflict => Conflicts.Count > 0;

    public int FileCountInExitSave { get; set; }
    public int FileCountInCurrentSave { get; set; }
    public int OnlyInExitSaveCount { get; set; }
    public int OnlyInCurrentSaveCount { get; set; }
    public int ModifiedCount { get; set; }

    public bool HasOnlyInExitSaveFiles => OnlyInExitSaveCount > 0;
    public bool HasOnlyInCurrentSaveFiles => OnlyInCurrentSaveCount > 0;
    public bool HasModifiedFiles => ModifiedCount > 0;
}

public enum ConflictResolution
{
    Cancel,
    Overwrite,
    Rebackup
}
