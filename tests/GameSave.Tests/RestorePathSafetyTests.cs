using System.Reflection;
using System.Runtime.InteropServices;
using GameSave.Helpers;
using GameSave.Models;
using GameSave.Services;
using Xunit;

namespace GameSave.Tests;

/// <summary>
/// 还原存档（清空目标后解压）路径安全预检的隔离测试。
/// 所有文件读写都限制在本测试创建的唯一临时目录内：不初始化真实用户配置、不启动应用，
/// 也不创建或修改真实的文档、桌面、用户目录。
/// </summary>
public sealed class RestorePathSafetyTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "gamesave-restore-safety-test-" + Guid.NewGuid().ToString("N"));

    private readonly List<string> _createdLinks = new();

    private string Work => Path.Combine(_root, "work");
    private string Saves => Path.Combine(_root, "saves");
    private string Archive => Path.Combine(Work, "game-1", "1700000000_手动存档.tar");
    private string Settings => Path.Combine(_root, "settings");
    private string Config => Path.Combine(Settings, "config.json");
    private string Outside => Path.Combine(_root, "outside");
    private string Payload => Path.Combine(_root, "payload");

    public RestorePathSafetyTests()
    {
        Directory.CreateDirectory(Work);
        Directory.CreateDirectory(Saves);
        Directory.CreateDirectory(Outside);
        Directory.CreateDirectory(Path.GetDirectoryName(Archive)!);
    }

    [Fact]
    public void Resolve_ReturnsNormalizedTargetsInOrderAndAllowsSingleFile()
    {
        var other = Saves + "-other";
        var singleFile = Path.Combine(Saves, "slot1.sav");
        File.WriteAllText(singleFile, "单文件存档");

        var targets = RestorePathSafety.ResolveRestoreTargets(
            new[] { Saves + Path.DirectorySeparatorChar, other }, Work, Archive, Config);

        Assert.Equal(new[] { Saves, other }, targets);

        var fileTargets = RestorePathSafety.ResolveRestoreTargets(new[] { singleFile }, Work, Archive, Config);
        Assert.Equal(new[] { singleFile }, fileTargets);
    }

    [Fact]
    public void Resolve_IgnoresBlankEntries()
    {
        var targets = RestorePathSafety.ResolveRestoreTargets(new[] { "", "   " }, Work, Archive, Config);
        Assert.Empty(targets);
    }

    [Theory]
    [InlineData("saves")]
    [InlineData("saves\\sub")]
    [InlineData("C:temp")]
    [InlineData(@"\\?\C:\saves")]
    [InlineData(@"\\.\C:")]
    public void Resolve_RejectsRelativeAndDevicePaths(string path)
    {
        Assert.Throws<ArgumentException>(() =>
            RestorePathSafety.ResolveRestoreTargets(new[] { path }, Work, Archive, Config));
    }

    /// <summary>
    /// 用户数据目录：目录本身与其上级都不能作为还原目标。
    /// 只通过 Environment.GetFolderPath 取字符串做判定，不读写这些真实目录。
    /// </summary>
    public static IEnumerable<object[]> ProtectedRoots()
    {
        var roots = new List<string?> { Path.GetPathRoot(Path.GetTempPath()) };
        foreach (var folder in new[]
        {
            Environment.SpecialFolder.UserProfile,
            Environment.SpecialFolder.DesktopDirectory,
            Environment.SpecialFolder.MyDocuments,
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolder.CommonApplicationData,
            Environment.SpecialFolder.ProgramFiles,
            Environment.SpecialFolder.ProgramFilesX86
        })
        {
            roots.Add(Environment.GetFolderPath(folder));
        }

        // 用户目录的上级（例如 C:\Users）同样不能作为还原目标
        roots.Add(Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
        roots.Add(Environment.GetEnvironmentVariable("OneDrive"));

        return roots.Where(root => !string.IsNullOrWhiteSpace(root)).Select(root => new object[] { root! });
    }

    [Theory]
    [MemberData(nameof(ProtectedRoots))]
    public void Resolve_RejectsProtectedRoots(string protectedRoot)
    {
        Assert.Throws<InvalidOperationException>(() =>
            RestorePathSafety.ResolveRestoreTargets(new[] { protectedRoot }, Work, Archive, Config));
    }

    /// <summary>
    /// 系统目录：目录本身、其上级及其子目录都不能作为还原目标（例如 Windows\System32）。
    /// 只取字符串做判定，不读写这些真实目录。
    /// </summary>
    public static IEnumerable<object[]> SystemDirectoryTargets()
    {
        var candidates = new List<string?>();
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        if (!string.IsNullOrWhiteSpace(windows))
        {
            candidates.Add(windows);
            candidates.Add(Path.Combine(windows, "System32"));
        }

        candidates.Add(Environment.GetEnvironmentVariable("SystemRoot"));

        return candidates.Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(candidate => new object[] { candidate! });
    }

    [Theory]
    [MemberData(nameof(SystemDirectoryTargets))]
    public void Resolve_RejectsSystemDirectoriesAndTheirChildren(string systemDirectory)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            RestorePathSafety.ResolveRestoreTargets(new[] { systemDirectory }, Work, Archive, Config));

        Assert.Contains("系统目录", error.Message);
    }

    [Fact]
    public void NormalizeTargetPath_AllowsDedicatedSteamSaveFolderWithoutAccessingIt()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (string.IsNullOrWhiteSpace(programFiles)) return;
        var path = Path.Combine(programFiles, "Steam", "userdata", "123", "456");
        // 仅调用纯路径策略，不读取属性、不枚举或创建真实 Steam 目录。
        Assert.Equal(path, RestorePathSafety.NormalizeTargetPath(path));
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Resolve_RejectsDuplicateAndNestedTargets(bool parentFirst)
    {
        var nested = Path.Combine(Saves, "sub");
        var paths = parentFirst ? new[] { Saves, nested } : new[] { nested, Saves };

        var error = Assert.Throws<InvalidOperationException>(() =>
            RestorePathSafety.ResolveRestoreTargets(paths, Work, Archive, Config));

        Assert.Contains("不能重复或互相包含", error.Message);
    }

    [Fact]
    public void Resolve_RejectsDuplicateTargetAfterNormalization()
    {
        Assert.Throws<InvalidOperationException>(() => RestorePathSafety.ResolveRestoreTargets(
            new[] { Saves, Saves + Path.DirectorySeparatorChar }, Work, Archive, Config));
    }

    [Theory]
    [InlineData("work")]
    [InlineData("work-child")]
    [InlineData("game-dir")]
    [InlineData("root")]
    public void Resolve_RejectsTargetOverlappingWorkDirectoryOrArchive(string kind)
    {
        File.WriteAllText(Archive, "占位的存档文件");

        var target = kind switch
        {
            "work" => Work,
            "work-child" => Path.Combine(Work, "saves"),
            "game-dir" => Path.Combine(Work, "game-1"),
            _ => _root
        };

        var error = Assert.Throws<InvalidOperationException>(() =>
            RestorePathSafety.ResolveRestoreTargets(new[] { target }, Work, Archive, Config));

        Assert.False(string.IsNullOrWhiteSpace(error.Message));
        // 预检不删除任何文件：源存档必须还在
        Assert.True(File.Exists(Archive));
    }

    [Fact]
    public void Resolve_RejectsTargetContainingConfigFile()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            RestorePathSafety.ResolveRestoreTargets(new[] { Settings }, Work, Archive, Config));

        Assert.Contains("配置文件", error.Message);
    }

    [Fact]
    public void Resolve_RejectsEmptyArchivePath()
    {
        Assert.Throws<InvalidOperationException>(() =>
            RestorePathSafety.ResolveRestoreTargets(new[] { Saves }, Work, "  ", Config));
    }

    [Fact]
    public async Task Restore_ClearsOnlyItsOwnTargetAndExtractsArchive()
    {
        await CreateArchiveAsync();
        var storage = CreateStorage(new Game { Id = "game-1", Name = "隔离测试游戏", SaveFolderPaths = [Saves] });
        var oldSave = Path.Combine(Saves, "旧存档.dat");
        await File.WriteAllTextAsync(oldSave, "旧内容");
        var keep = Path.Combine(Outside, "保留.dat");
        await File.WriteAllTextAsync(keep, "保留内容");

        await storage.RestoreSaveAsync(CreateSaveFile("game-1"));

        Assert.Equal("还原内容", await File.ReadAllTextAsync(Path.Combine(Saves, "还原后的存档.dat")));
        Assert.False(File.Exists(oldSave));
        Assert.Equal("保留内容", await File.ReadAllTextAsync(keep));
        Assert.True(File.Exists(Archive));
    }

    [Fact]
    public async Task Restore_RejectsEveryUnsafeTargetBeforeDeletingAnything()
    {
        await CreateArchiveAsync();
        // 第一个目标本身安全，第二个目标包含正在还原的存档（工作目录）：
        // 只有在删除前完成全部目标的预检，第一个目标里的文件才会被保留。
        var storage = CreateStorage(new Game
        {
            Id = "game-2",
            Name = "隔离测试游戏",
            SaveFolderPaths = [Saves, Work]
        });
        var oldSave = Path.Combine(Saves, "旧存档.dat");
        await File.WriteAllTextAsync(oldSave, "旧内容");

        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.RestoreSaveAsync(CreateSaveFile("game-2")));

        Assert.Equal("旧内容", await File.ReadAllTextAsync(oldSave));
        Assert.True(File.Exists(Archive));
    }

    [Fact]
    public void Resolve_RejectsLinkInsideTargetTree()
    {
        var link = Path.Combine(Saves, "link");
        if (!TryCreateDirectoryLink(link, Outside)) return;

        var keep = Path.Combine(Outside, "保留.dat");
        File.WriteAllText(keep, "保留内容");

        var error = Assert.Throws<InvalidOperationException>(() =>
            RestorePathSafety.ResolveRestoreTargets(new[] { Saves }, Work, Archive, Config));

        Assert.Contains(link, error.Message);
        Assert.Equal("保留内容", File.ReadAllText(keep));
    }

    [Fact]
    public async Task Restore_RejectsLinkInsideTargetTreeWithoutDeletingOutsideFiles()
    {
        await CreateArchiveAsync();
        var link = Path.Combine(Saves, "link");
        if (!TryCreateDirectoryLink(link, Outside)) return;

        var keep = Path.Combine(Outside, "保留.dat");
        await File.WriteAllTextAsync(keep, "保留内容");
        var storage = CreateStorage(new Game { Id = "game-3", Name = "隔离测试游戏", SaveFolderPaths = [Saves] });

        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.RestoreSaveAsync(CreateSaveFile("game-3")));

        Assert.Equal("保留内容", await File.ReadAllTextAsync(keep));
        Assert.True(File.Exists(Archive));
    }

    [Fact]
    public void Resolve_RejectsTargetThatIsItselfALink()
    {
        var link = Path.Combine(_root, "link-target");
        if (!TryCreateDirectoryLink(link, Outside)) return;

        var error = Assert.Throws<InvalidOperationException>(() =>
            RestorePathSafety.ResolveRestoreTargets(new[] { link }, Work, Archive, Config));

        Assert.Contains("符号链接或目录联接", error.Message);
    }

    private async Task CreateArchiveAsync()
    {
        Directory.CreateDirectory(Payload);
        await File.WriteAllTextAsync(Path.Combine(Payload, "还原后的存档.dat"), "还原内容");
        await TarHelper.CreateTarAsync(Payload, Archive);
    }

    private SaveFile CreateSaveFile(string gameId) => new()
    {
        GameId = gameId,
        Name = "手动存档",
        Path = Archive,
        BackupTime = DateTime.Now,
        Tag = SaveTag.ManualSave
    };

    private LocalStorageService CreateStorage(Game game)
    {
        var service = new ConfigService();
        // 只注入内存配置与临时配置文件路径，不初始化也不读写真实用户配置。
        typeof(ConfigService).GetField("_config", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(service, new AppConfig { WorkDirectory = Work, Games = [game] });
        typeof(ConfigService).GetField("_configFilePath", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(service, Config);
        return new LocalStorageService(service);
    }

    /// <summary>
    /// 在临时目录内创建一个指向目录的链接：优先符号链接，环境不允许时退回目录联接。
    /// 两者都不可用时返回 false，调用方据此跳过该用例。
    /// </summary>
    private bool TryCreateDirectoryLink(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            _createdLinks.Add(linkPath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // 没有创建符号链接的权限时改用目录联接（标准用户即可创建）。
        }

        var created = TryCreateJunction(linkPath, targetPath);
        if (created) _createdLinks.Add(linkPath);
        return created;
    }

    private static bool TryCreateJunction(string junctionPath, string targetPath)
    {
        const uint GenericWrite = 0x40000000;
        const uint ShareAll = 0x00000007;
        const uint OpenExisting = 3;
        const uint FileFlagBackupSemantics = 0x02000000;
        const uint FileFlagOpenReparsePoint = 0x00200000;
        const uint FsctlSetReparsePoint = 0x000900A4;
        const uint IOReparseTagMountPoint = 0xA0000003;

        var invalidHandle = new IntPtr(-1);
        var handle = IntPtr.Zero;
        var pinned = default(GCHandle);
        try
        {
            Directory.CreateDirectory(junctionPath);

            var target = Path.GetFullPath(targetPath);
            var substituteBytes = System.Text.Encoding.Unicode.GetBytes(@"\??\" + target);
            var printBytes = System.Text.Encoding.Unicode.GetBytes(target);
            var pathBufferLength = substituteBytes.Length + 2 + printBytes.Length + 2;

            // REPARSE_DATA_BUFFER：8 字节头 + 挂载点字段(8 字节) + 路径缓冲区
            var buffer = new byte[8 + 8 + pathBufferLength];
            BitConverter.GetBytes(IOReparseTagMountPoint).CopyTo(buffer, 0);
            BitConverter.GetBytes((ushort)(8 + pathBufferLength)).CopyTo(buffer, 4);
            BitConverter.GetBytes((ushort)0).CopyTo(buffer, 6);
            BitConverter.GetBytes((ushort)0).CopyTo(buffer, 8);                              // SubstituteNameOffset
            BitConverter.GetBytes((ushort)substituteBytes.Length).CopyTo(buffer, 10);        // SubstituteNameLength
            BitConverter.GetBytes((ushort)(substituteBytes.Length + 2)).CopyTo(buffer, 12);  // PrintNameOffset
            BitConverter.GetBytes((ushort)printBytes.Length).CopyTo(buffer, 14);             // PrintNameLength
            substituteBytes.CopyTo(buffer, 16);
            printBytes.CopyTo(buffer, 16 + substituteBytes.Length + 2);

            handle = CreateFileW(junctionPath, GenericWrite, ShareAll, IntPtr.Zero, OpenExisting,
                FileFlagBackupSemantics | FileFlagOpenReparsePoint, IntPtr.Zero);
            if (handle == invalidHandle) return false;

            pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            return DeviceIoControl(handle, FsctlSetReparsePoint, pinned.AddrOfPinnedObject(),
                (uint)buffer.Length, IntPtr.Zero, 0, out _, IntPtr.Zero);
        }
        catch
        {
            return false;
        }
        finally
        {
            if (pinned.IsAllocated) pinned.Free();
            if (handle != IntPtr.Zero && handle != invalidHandle) CloseHandle(handle);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateFileW")]
    private static extern IntPtr CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode, IntPtr lpInBuffer,
        uint nInBufferSize, IntPtr lpOutBuffer, uint nOutBufferSize, out uint lpBytesReturned, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    public void Dispose()
    {
        // 只清理构造时生成、位于临时目录中的这个唯一测试根；不接受外部输入路径。
        var fullRoot = Path.GetFullPath(_root);
        var temporaryRoot = Path.GetFullPath(Path.GetTempPath());
        if (!fullRoot.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(fullRoot).StartsWith("gamesave-restore-safety-test-", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("测试清理路径不安全。");
        }
        // 先解除本用例创建的联接，再清理临时树，避免依赖递归删除重解析点的顺序。
        foreach (var link in _createdLinks)
        {
            var fullLink = Path.GetFullPath(link);
            if (!fullLink.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("测试链接清理路径不安全。");
            try { Directory.Delete(fullLink, recursive: false); }
            catch (DirectoryNotFoundException) { }
        }
        if (Directory.Exists(fullRoot)) Directory.Delete(fullRoot, recursive: true);
    }
}
