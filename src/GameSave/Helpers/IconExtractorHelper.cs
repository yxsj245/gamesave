using System.Runtime.InteropServices;
using GameSave;
using Microsoft.UI.Xaml.Media.Imaging;

namespace GameSave.Helpers;

/// <summary>
/// 从 EXE 文件中提取图标的辅助类。
/// 使用 Win32 API 提取图标，转换为 PNG 缓存文件后供 WinUI 3 显示。
/// </summary>
public static class IconExtractorHelper
{
    // Win32 API: 从文件中提取图标
    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern uint ExtractIconEx(string szFileName, int nIconIndex,
        IntPtr[] phiconLarge, IntPtr[] phiconSmall, uint nIcons);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    // 图标缓存目录
    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GameSave", "IconCache");

    // 内存缓存，避免重复读取磁盘
    private static readonly Dictionary<string, BitmapImage> _memoryCache = new();

    /// <summary>
    /// 从 EXE 文件提取图标并返回 BitmapImage。
    /// 结果会被缓存到磁盘和内存中。
    /// </summary>
    /// <param name="exePath">EXE 文件的完整路径</param>
    /// <returns>提取到的图标 BitmapImage，失败返回 null</returns>
    public static BitmapImage? GetIconFromExe(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
            return null;

        // 展开环境变量
        var resolvedPath = PathEnvironmentHelper.ExpandEnvVariables(exePath);

        // 生成缓存 key（基于文件路径的哈希）
        var cacheKey = GetCacheKey(resolvedPath);

        // 1. 检查内存缓存
        if (_memoryCache.TryGetValue(cacheKey, out var cached))
            return cached;

        // 2. 检查磁盘缓存
        var cachePath = GetCachePath(cacheKey);
        if (File.Exists(cachePath))
        {
            var bitmap = LoadBitmapFromFile(cachePath);
            if (bitmap != null)
            {
                _memoryCache[cacheKey] = bitmap;
                return bitmap;
            }
        }

        // 3. 从 EXE 提取图标
        if (!File.Exists(resolvedPath))
            return null;

        try
        {
            var pngBytes = ExtractIconToPng(resolvedPath);
            if (pngBytes == null || pngBytes.Length == 0)
                return null;

            // 保存到磁盘缓存
            Directory.CreateDirectory(CacheDir);
            File.WriteAllBytes(cachePath, pngBytes);

            // 加载为 BitmapImage
            var bitmap = LoadBitmapFromBytes(pngBytes);
            if (bitmap != null)
            {
                _memoryCache[cacheKey] = bitmap;
            }
            return bitmap;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[IconExtractor] 提取图标失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 从普通图片文件提取图标并返回 BitmapImage。
    /// 支持 PNG、JPG、BMP、GIF、ICO 等常见格式。
    /// </summary>
    /// <param name="imagePath">图片文件的完整路径</param>
    /// <returns>图片位图，失败返回 null</returns>
    public static BitmapImage? GetIconFromImageFile(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            return null;

        var resolvedPath = Path.GetFullPath(imagePath);
        var cacheKey = GetCacheKey(resolvedPath);

        if (_memoryCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var cachePath = GetCachePath(cacheKey);
        if (File.Exists(cachePath))
        {
            var bitmap = LoadBitmapFromFile(cachePath);
            if (bitmap != null)
            {
                _memoryCache[cacheKey] = bitmap;
                return bitmap;
            }
        }

        if (!File.Exists(resolvedPath))
            return null;

        try
        {
            var bytes = File.ReadAllBytes(resolvedPath);
            if (bytes.Length == 0)
                return null;

            Directory.CreateDirectory(CacheDir);
            File.WriteAllBytes(cachePath, bytes);

            var bitmap = LoadBitmapFromBytes(bytes);
            if (bitmap != null)
            {
                _memoryCache[cacheKey] = bitmap;
            }
            return bitmap;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[IconExtractor] 加载自定义图标失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 获取游戏最终应该显示的图标。
    /// 优先级：自定义图标 > 启动进程图标 > 空。
    /// </summary>
    public static BitmapImage? GetGameIcon(string gameId, string? iconPath, string? processPath)
    {
        var customIconPath = ResolveGameIconPath(gameId, iconPath);
        if (!string.IsNullOrWhiteSpace(customIconPath) && File.Exists(customIconPath))
        {
            var customIcon = GetIconFromImageFile(customIconPath);
            if (customIcon != null)
                return customIcon;
        }

        return GetIconFromExe(processPath);
    }

    /// <summary>
    /// 解析游戏图标路径。
    /// 如果存储的是相对路径，则按游戏工作目录进行拼接。
    /// </summary>
    public static string? ResolveGameIconPath(string gameId, string? iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath))
            return null;

        if (Path.IsPathRooted(iconPath))
            return iconPath;

        if (string.IsNullOrWhiteSpace(gameId))
            return null;

        try
        {
            var workDir = App.ConfigService.WorkDirectory;
            if (string.IsNullOrWhiteSpace(workDir))
                return null;

            return Path.Combine(workDir, gameId, iconPath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[IconExtractor] 解析游戏图标路径失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 将用户选择的图片复制为游戏自定义图标，并返回保存到配置中的相对路径。
    /// </summary>
    /// <param name="gameId">游戏 ID</param>
    /// <param name="sourceImagePath">用户选择的图片路径</param>
    /// <param name="oldIconPath">旧图标路径，用于清理旧文件</param>
    /// <returns>保存到配置中的相对路径</returns>
    public static async Task<string?> SaveCustomIconAsync(string gameId, string? sourceImagePath, string? oldIconPath = null)
    {
        if (string.IsNullOrWhiteSpace(sourceImagePath))
            return null;

        var resolvedSourcePath = Path.GetFullPath(sourceImagePath);
        if (!File.Exists(resolvedSourcePath))
            throw new FileNotFoundException("自定义图标文件不存在", resolvedSourcePath);

        var extension = Path.GetExtension(sourceImagePath);
        if (string.IsNullOrWhiteSpace(extension))
            extension = ".png";

        var iconDir = Path.Combine(App.ConfigService.GetGameWorkDirectory(gameId), "icon");
        Directory.CreateDirectory(iconDir);

        var targetFileName = $"custom-icon{extension.ToLowerInvariant()}";
        var targetPath = Path.Combine(iconDir, targetFileName);
        var storedRelativePath = Path.Combine("icon", targetFileName);

        if (string.Equals(resolvedSourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
            return storedRelativePath;

        // 清理旧缓存，避免更换图标后仍然显示旧图片
        var oldResolvedPath = ResolveGameIconPath(gameId, oldIconPath);
        ClearCache(oldResolvedPath);
        ClearCache(targetPath);

        await Task.Run(() => File.Copy(resolvedSourcePath, targetPath, overwrite: true));

        if (!string.IsNullOrWhiteSpace(oldResolvedPath) &&
            !string.Equals(oldResolvedPath, targetPath, StringComparison.OrdinalIgnoreCase) &&
            File.Exists(oldResolvedPath))
        {
            try
            {
                File.Delete(oldResolvedPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[IconExtractor] 删除旧自定义图标失败: {ex.Message}");
            }
        }

        return storedRelativePath;
    }

    /// <summary>
    /// 删除游戏自定义图标文件。
    /// </summary>
    public static void RemoveCustomIcon(string gameId, string? iconPath)
    {
        var resolvedPath = ResolveGameIconPath(gameId, iconPath);
        ClearCache(resolvedPath);

        if (!string.IsNullOrWhiteSpace(resolvedPath) && File.Exists(resolvedPath))
        {
            try
            {
                File.Delete(resolvedPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[IconExtractor] 删除自定义图标失败: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 清除指定路径的图标缓存（用于路径变更后刷新）
    /// </summary>
    public static void ClearCache(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var resolvedPath = PathEnvironmentHelper.ExpandEnvVariables(path);
        var cacheKey = GetCacheKey(resolvedPath);

        _memoryCache.Remove(cacheKey);

        var cachePath = GetCachePath(cacheKey);
        if (File.Exists(cachePath))
        {
            try { File.Delete(cachePath); }
            catch { /* 忽略删除失败 */ }
        }
    }

    /// <summary>
    /// 从 EXE 文件提取大图标并转换为 PNG 字节数组
    /// </summary>
    private static byte[]? ExtractIconToPng(string exePath)
    {
        IntPtr[] largeIcons = new IntPtr[1];
        IntPtr[] smallIcons = new IntPtr[1];

        try
        {
            uint count = ExtractIconEx(exePath, 0, largeIcons, smallIcons, 1);
            if (count == 0 || largeIcons[0] == IntPtr.Zero)
                return null;

            // 使用 System.Drawing 互操作将 HICON 转换为 PNG
            // WinUI 3 不支持 System.Drawing，我们使用 GDI+ 手动处理
            return ConvertHIconToPng(largeIcons[0]);
        }
        finally
        {
            // 释放图标句柄
            if (largeIcons[0] != IntPtr.Zero)
                DestroyIcon(largeIcons[0]);
            if (smallIcons[0] != IntPtr.Zero)
                DestroyIcon(smallIcons[0]);
        }
    }

    #region GDI+ 图标转 PNG

    // GDI+ API 声明
    [DllImport("gdiplus.dll")]
    private static extern int GdiplusStartup(out IntPtr token, ref GdiplusStartupInput input, IntPtr output);

    [DllImport("gdiplus.dll")]
    private static extern void GdiplusShutdown(IntPtr token);

    [DllImport("gdiplus.dll")]
    private static extern int GdipCreateBitmapFromHICON(IntPtr hIcon, out IntPtr bitmap);

    [DllImport("gdiplus.dll")]
    private static extern int GdipDisposeImage(IntPtr image);

    [DllImport("gdiplus.dll")]
    private static extern int GdipSaveImageToStream(IntPtr image, IntPtr stream, ref Guid clsidEncoder, IntPtr encoderParams);

    [DllImport("gdiplus.dll")]
    private static extern int GdipGetImageEncodersSize(out int numEncoders, out int size);

    [DllImport("gdiplus.dll")]
    private static extern int GdipGetImageEncoders(int numEncoders, int size, IntPtr encoders);

    [DllImport("gdiplus.dll")]
    private static extern int GdipGetImageWidth(IntPtr image, out int width);

    [DllImport("gdiplus.dll")]
    private static extern int GdipGetImageHeight(IntPtr image, out int height);

    [DllImport("gdiplus.dll")]
    private static extern int GdipCreateBitmapFromScan0(int width, int height, int stride, int format, IntPtr scan0, out IntPtr bitmap);

    [DllImport("gdiplus.dll")]
    private static extern int GdipGetImageGraphicsContext(IntPtr image, out IntPtr graphics);

    [DllImport("gdiplus.dll")]
    private static extern int GdipSetInterpolationMode(IntPtr graphics, int mode);

    [DllImport("gdiplus.dll")]
    private static extern int GdipDrawImageRectI(IntPtr graphics, IntPtr image, int x, int y, int width, int height);

    [DllImport("gdiplus.dll")]
    private static extern int GdipDeleteGraphics(IntPtr graphics);

    [DllImport("ole32.dll")]
    private static extern int CreateStreamOnHGlobal(IntPtr hGlobal, bool fDeleteOnRelease, out IntPtr ppstm);

    [DllImport("ole32.dll")]
    private static extern int GetHGlobalFromStream(IntPtr pstm, out IntPtr phglobal);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern UIntPtr GlobalSize(IntPtr hMem);

    [StructLayout(LayoutKind.Sequential)]
    private struct GdiplusStartupInput
    {
        public uint GdiplusVersion;
        public IntPtr DebugEventCallback;
        public int SuppressBackgroundThread;
        public int SuppressExternalCodecs;
    }

    // PNG 编码器 CLSID
    private static readonly Guid PngEncoderClsid = new Guid("557cf406-1a04-11d3-9a73-0000f81ef32e");

    /// <summary>
    /// 使用 GDI+ 将 HICON 转换为 PNG 字节数组，目标大小 48x48
    /// </summary>
    private static byte[]? ConvertHIconToPng(IntPtr hIcon)
    {
        var startupInput = new GdiplusStartupInput { GdiplusVersion = 1 };
        int status = GdiplusStartup(out IntPtr gdipToken, ref startupInput, IntPtr.Zero);
        if (status != 0) return null;

        IntPtr gdipBitmap = IntPtr.Zero;
        IntPtr resizedBitmap = IntPtr.Zero;
        IntPtr graphics = IntPtr.Zero;
        IntPtr stream = IntPtr.Zero;

        try
        {
            // 从 HICON 创建 GDI+ Bitmap
            status = GdipCreateBitmapFromHICON(hIcon, out gdipBitmap);
            if (status != 0 || gdipBitmap == IntPtr.Zero) return null;

            // 获取原始尺寸
            GdipGetImageWidth(gdipBitmap, out int origWidth);
            GdipGetImageHeight(gdipBitmap, out int origHeight);

            // 目标尺寸 48x48（匹配 UI 显示大小）
            const int targetSize = 48;
            IntPtr bitmapToSave;

            if (origWidth != targetSize || origHeight != targetSize)
            {
                // 创建目标大小的位图（PixelFormat32bppARGB = 0x26200A）
                status = GdipCreateBitmapFromScan0(targetSize, targetSize, 0, 0x26200A, IntPtr.Zero, out resizedBitmap);
                if (status != 0 || resizedBitmap == IntPtr.Zero) return null;

                // 获取 Graphics 上下文
                status = GdipGetImageGraphicsContext(resizedBitmap, out graphics);
                if (status != 0 || graphics == IntPtr.Zero) return null;

                // 设置高质量插值模式（HighQualityBicubic = 7）
                GdipSetInterpolationMode(graphics, 7);

                // 绘制缩放后的图标
                GdipDrawImageRectI(graphics, gdipBitmap, 0, 0, targetSize, targetSize);
                GdipDeleteGraphics(graphics);
                graphics = IntPtr.Zero;

                bitmapToSave = resizedBitmap;
            }
            else
            {
                bitmapToSave = gdipBitmap;
            }

            // 创建 IStream
            int hr = CreateStreamOnHGlobal(IntPtr.Zero, true, out stream);
            if (hr != 0 || stream == IntPtr.Zero) return null;

            // 保存为 PNG
            Guid pngClsid = PngEncoderClsid;
            status = GdipSaveImageToStream(bitmapToSave, stream, ref pngClsid, IntPtr.Zero);
            if (status != 0) return null;

            // 从 IStream 读取 PNG 数据
            hr = GetHGlobalFromStream(stream, out IntPtr hGlobal);
            if (hr != 0) return null;

            UIntPtr size = GlobalSize(hGlobal);
            int dataSize = (int)size.ToUInt64();
            if (dataSize == 0) return null;

            IntPtr pData = GlobalLock(hGlobal);
            if (pData == IntPtr.Zero) return null;

            try
            {
                byte[] pngBytes = new byte[dataSize];
                Marshal.Copy(pData, pngBytes, 0, dataSize);
                return pngBytes;
            }
            finally
            {
                GlobalUnlock(hGlobal);
            }
        }
        finally
        {
            if (graphics != IntPtr.Zero) GdipDeleteGraphics(graphics);
            if (resizedBitmap != IntPtr.Zero) GdipDisposeImage(resizedBitmap);
            if (gdipBitmap != IntPtr.Zero) GdipDisposeImage(gdipBitmap);
            // stream 由 CreateStreamOnHGlobal(fDeleteOnRelease=true) 自动释放
            GdiplusShutdown(gdipToken);
        }
    }

    #endregion

    /// <summary>
    /// 从文件路径加载 BitmapImage
    /// </summary>
    private static BitmapImage? LoadBitmapFromFile(string filePath)
    {
        try
        {
            var bytes = File.ReadAllBytes(filePath);
            return LoadBitmapFromBytes(bytes);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 从字节数组加载 BitmapImage
    /// </summary>
    private static BitmapImage? LoadBitmapFromBytes(byte[] data)
    {
        try
        {
            var bitmap = new BitmapImage();
            using var memStream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            // 同步写入（WinUI 3 要求在 UI 线程上设置 BitmapImage）
            var writer = new Windows.Storage.Streams.DataWriter(memStream.GetOutputStreamAt(0));
            writer.WriteBytes(data);
            writer.StoreAsync().AsTask().GetAwaiter().GetResult();
            writer.FlushAsync().AsTask().GetAwaiter().GetResult();
            writer.DetachStream();

            memStream.Seek(0);
            bitmap.SetSource(memStream);
            return bitmap;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[IconExtractor] 加载位图失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 基于文件路径生成缓存 key
    /// </summary>
    private static string GetCacheKey(string filePath)
    {
        // 使用简单的哈希作为文件名
        var hash = filePath.ToLowerInvariant().GetHashCode();
        return $"icon_{hash:X8}";
    }

    /// <summary>
    /// 获取缓存文件完整路径
    /// </summary>
    private static string GetCachePath(string cacheKey)
    {
        return Path.Combine(CacheDir, $"{cacheKey}.png");
    }
}
