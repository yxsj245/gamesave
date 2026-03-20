using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using System.Collections.Concurrent;

namespace GameSave.Services;

/// <summary>
/// 发现的疑似存档目录信息
/// </summary>
public class DetectedDirectory
{
    /// <summary>目录路径</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>写入次数</summary>
    public long WriteCount { get; set; }

    /// <summary>总写入字节数</summary>
    public long TotalBytes { get; set; }

    /// <summary>概率得分（自身写入 + 所有子目录写入之和）</summary>
    public long Score { get; set; }

    /// <summary>目录嵌套深度</summary>
    public int Depth { get; set; }

    /// <summary>该目录下被读写的文件列表</summary>
    public List<string> Files { get; set; } = new();

    /// <summary>首次发现时间</summary>
    public DateTime FirstSeen { get; set; }

    /// <summary>最后一次写入时间</summary>
    public DateTime LastSeen { get; set; }
}

/// <summary>
/// 游戏存档目录探测服务 - 基于 ETW（Event Tracing for Windows）
/// 监控指定进程的文件写入操作，自动识别用户目录下的疑似存档目录。
/// 从 exe_proc/SaveDetector.cs 迁移并改造为 WinUI 可消费的服务。
/// </summary>
public class SaveDetectorService : IDisposable
{
    private const string SessionName = "GameSaveSaveDetector";

    private TraceEventSession? _session;
    private int _targetPid;
    private bool _isRunning;
    private bool _disposed;
    private Thread? _processingThread;

    // FileKey 到文件名的映射
    private readonly ConcurrentDictionary<ulong, string> _fileObjectToName = new();

    // 已发现的目录及其写入统计
    private readonly ConcurrentDictionary<string, DirectoryStats> _discoveredDirs = new(StringComparer.OrdinalIgnoreCase);

    // 每个目录下被读写的文件集合
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, bool>> _dirFiles = new(StringComparer.OrdinalIgnoreCase);

    // 用户目录路径白名单
    private readonly List<string> _userPaths;

    /// <summary>
    /// 新目录被发现时触发的事件
    /// </summary>
    public event Action<DetectedDirectory>? DirectoryDiscovered;

    /// <summary>
    /// 目录统计更新时触发的事件（已有目录的写入次数增加时）
    /// </summary>
    public event Action? StatsUpdated;

    /// <summary>
    /// 当前探测是否正在运行
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// 目录写入统计信息（内部使用）
    /// </summary>
    private class DirectoryStats
    {
        public long WriteCount;
        public long TotalBytes;
        public DateTime FirstSeen;
        public DateTime LastSeen;
        public int Depth;
    }

    public SaveDetectorService()
    {
        _userPaths = BuildUserPaths();
    }

    /// <summary>
    /// 构建用户目录路径白名单
    /// </summary>
    private static List<string> BuildUserPaths()
    {
        var paths = new List<string>();

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrEmpty(appData)) paths.Add(appData);

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(localAppData)) paths.Add(localAppData);

        // LocalLow 不在 SpecialFolder 枚举中，需要手动构建
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(userProfile))
        {
            paths.Add(Path.Combine(userProfile, "AppData", "LocalLow"));
            paths.Add(Path.Combine(userProfile, "Saved Games"));
            paths.Add(Path.Combine(userProfile, "Desktop"));
        }

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrEmpty(documents)) paths.Add(documents);

        return paths;
    }

    /// <summary>
    /// 启动存档目录探测
    /// </summary>
    /// <param name="targetPid">要监控的目标进程 PID</param>
    public void Start(int targetPid)
    {
        if (_isRunning) return;

        _targetPid = targetPid;

        // 清理可能残留的同名 ETW 会话
        try
        {
            var existing = TraceEventSession.GetActiveSession(SessionName);
            if (existing != null) { existing.Stop(true); existing.Dispose(); }
        }
        catch { }

        _session = new TraceEventSession(SessionName);
        _session.EnableKernelProvider(
            KernelTraceEventParser.Keywords.FileIOInit |
            KernelTraceEventParser.Keywords.FileIO
        );

        RegisterCallbacks(_session.Source);
        _isRunning = true;

        _processingThread = new Thread(() =>
        {
            try { _session.Source.Process(); }
            catch (Exception ex)
            {
                if (_isRunning)
                    System.Diagnostics.Debug.WriteLine($"[存档探测] ETW事件处理异常: {ex.Message}");
            }
        })
        {
            Name = "存档探测ETW线程",
            IsBackground = true
        };
        _processingThread.Start();
    }

    /// <summary>
    /// 停止探测
    /// </summary>
    public void Stop()
    {
        if (!_isRunning) return;
        _isRunning = false;

        try { _session?.Stop(); } catch { }
        _processingThread?.Join(3000);
    }

    /// <summary>
    /// 获取当前发现的所有疑似存档目录（按概率得分降序排列）
    /// </summary>
    public List<DetectedDirectory> GetResults()
    {
        return _discoveredDirs
            .Select(kv => new DetectedDirectory
            {
                Path = kv.Key,
                WriteCount = kv.Value.WriteCount,
                TotalBytes = kv.Value.TotalBytes,
                Score = CalcScore(kv.Key),
                Depth = kv.Value.Depth,
                FirstSeen = kv.Value.FirstSeen,
                LastSeen = kv.Value.LastSeen,
                Files = _dirFiles.TryGetValue(kv.Key, out var files)
                    ? files.Keys.ToList()
                    : new List<string>()
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Depth)
            .ToList();
    }

    /// <summary>
    /// 注册 ETW 事件回调
    /// </summary>
    private void RegisterCallbacks(ETWTraceEventSource source)
    {
        var kernel = source.Kernel;

        // FileKey 到文件名的映射
        kernel.FileIOName += (FileIONameTraceData data) =>
        {
            if (data.FileName != null)
                _fileObjectToName[(ulong)data.FileKey] = data.FileName;
        };

        // 监控写入事件
        kernel.FileIOWrite += (FileIOReadWriteTraceData data) =>
        {
            if (data.ProcessID != _targetPid) return;
            string filePath = data.FileName ?? GetFileName(data.FileObject);
            ProcessWriteEvent(filePath, data.IoSize);
        };

        // 监控创建/打开事件
        kernel.FileIOCreate += (FileIOCreateTraceData data) =>
        {
            if (data.ProcessID != _targetPid) return;
            string filePath = data.FileName ?? GetFileName(data.FileObject);
            ProcessWriteEvent(filePath, 0);
        };
    }

    /// <summary>
    /// 处理写入事件：提取目录、过滤、去重、记录文件
    /// </summary>
    private void ProcessWriteEvent(string filePath, int ioSize)
    {
        if (string.IsNullOrEmpty(filePath) || filePath.StartsWith("<")) return;
        if (!IsUnderUserPath(filePath)) return;

        string? dirPath = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(dirPath)) return;
        if (ShouldExclude(dirPath)) return;

        // 记录该目录下的文件
        string fileName = Path.GetFileName(filePath);
        if (!string.IsNullOrEmpty(fileName))
        {
            var fileSet = _dirFiles.GetOrAdd(dirPath, _ => new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase));
            fileSet.TryAdd(fileName, true);
        }

        int depth = CalcDepthFromUserRoot(dirPath);

        // 过滤深度不足的目录（如 AppData、LocalLow 自身不可能是游戏存档目录）
        // 典型存档路径至少有 2 层深度，如 LocalLow\CompanyName\GameName
        if (depth < 2) return;

        bool isNew = false;
        _discoveredDirs.AddOrUpdate(
            dirPath,
            _ =>
            {
                isNew = true;
                return new DirectoryStats
                {
                    WriteCount = 1,
                    TotalBytes = ioSize,
                    FirstSeen = DateTime.Now,
                    LastSeen = DateTime.Now,
                    Depth = depth
                };
            },
            (_, existing) =>
            {
                Interlocked.Increment(ref existing.WriteCount);
                Interlocked.Add(ref existing.TotalBytes, ioSize);
                existing.LastSeen = DateTime.Now;
                return existing;
            }
        );

        if (isNew)
        {
            // 触发新目录发现事件
            var detected = new DetectedDirectory
            {
                Path = dirPath,
                WriteCount = 1,
                TotalBytes = ioSize,
                Score = 1,
                Depth = depth,
                FirstSeen = DateTime.Now,
                LastSeen = DateTime.Now,
                Files = _dirFiles.TryGetValue(dirPath, out var files)
                    ? files.Keys.ToList()
                    : new List<string>()
            };
            DirectoryDiscovered?.Invoke(detected);
        }
        else
        {
            StatsUpdated?.Invoke();
        }
    }

    /// <summary>
    /// 计算目录相对于用户根目录的嵌套深度
    /// </summary>
    private int CalcDepthFromUserRoot(string dirPath)
    {
        foreach (var root in _userPaths)
        {
            if (dirPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                string relative = dirPath[root.Length..];
                if (relative.Length == 0) return 0;
                return relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).Length;
            }
        }
        return 0;
    }

    /// <summary>
    /// 计算目录的存档概率得分（自身写入 + 所有子目录写入之和）
    /// </summary>
    private long CalcScore(string dirPath)
    {
        long score = 0;
        foreach (var kv in _discoveredDirs)
        {
            if (kv.Key.StartsWith(dirPath, StringComparison.OrdinalIgnoreCase))
            {
                score += kv.Value.WriteCount;
            }
        }

        // 如果目录名包含 "save" 关键字，极大概率是存档目录，直接提升得分
        string dirName = Path.GetFileName(dirPath) ?? "";
        if (dirName.Contains("save", StringComparison.OrdinalIgnoreCase) ||
            dirName.Contains("saves", StringComparison.OrdinalIgnoreCase) ||
            dirName.Contains("savegame", StringComparison.OrdinalIgnoreCase) ||
            dirName.Contains("savedata", StringComparison.OrdinalIgnoreCase))
        {
            score = Math.Max(score, 99);
        }

        return score;
    }

    /// <summary>
    /// 判断文件路径是否在用户目录白名单下
    /// </summary>
    private bool IsUnderUserPath(string filePath)
    {
        foreach (var userPath in _userPaths)
        {
            if (filePath.StartsWith(userPath, StringComparison.OrdinalIgnoreCase)
                && filePath.Length > userPath.Length
                && filePath[userPath.Length] == Path.DirectorySeparatorChar)
                return true;
        }
        return false;
    }

    /// <summary>
    /// 判断目录是否应被排除（缓存、日志、浏览器等无关目录）
    /// </summary>
    private static bool ShouldExclude(string dirPath)
    {
        string lower = dirPath.ToLowerInvariant();

        string[] excludePatterns =
        [
            "\\cache",
            "\\caches",
            "\\temp",
            "\\tmp",
            "\\logs",
            "\\log",
            "\\crash",
            "\\crashdumps",
            "\\shader",
            "\\shadercache",
            "\\gpucache",
            "\\webcache",
            "\\code cache",
            "\\dxcache",
            "\\d3dshadercache",
            "\\browser",
            "\\chrome",
            "\\firefox",
            "\\edge",
            "\\microsoft\\windows",
            "\\microsoft\\office",
            "\\microsoft\\teams",
            "\\microsoft\\edge",
            "\\google\\chrome",
            "\\mozilla\\firefox",
            "\\nvidia",
            "\\amd",
            "\\intel",
            "\\windows\\",
            "\\fontcache",
            "\\inetcache",
            "\\prefetch",
            "\\recent",
            "\\thumbnails",
            "\\analytics",
            "\\telemetry",
            "\\archivedevents",
            "\\diagnostics",
            "\\crashreport",
        ];

        foreach (var pattern in excludePatterns)
        {
            if (lower.Contains(pattern))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 通过 FileObject 指针获取文件名
    /// </summary>
    private string GetFileName(ulong fileObject)
    {
        return _fileObjectToName.TryGetValue(fileObject, out var name) ? name : "";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _session?.Dispose();
        _fileObjectToName.Clear();
        _discoveredDirs.Clear();
        _dirFiles.Clear();
        GC.SuppressFinalize(this);
    }
}
