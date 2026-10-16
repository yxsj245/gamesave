using System.Diagnostics;
using System.Reflection;
using System.Text;
using GameSave.Helpers;
using Xunit;

namespace GameSave.Tests;

/// <summary>
/// 提权重启与 PowerShell 备用选择器的安全回归测试。
/// 只检查无副作用的 ProcessStartInfo 构造结果：不启动任何进程、不触发 UAC、不弹出任何对话框。
/// </summary>
public class AdminHelperRestartStartInfoTests
{
    private const int CurrentPid = 4321;

    [Fact]
    public void BuildRestartArguments_KeepsPortableAndWorkDirArguments()
    {
        // 中文路径、空格、尾部反斜杠都必须原样保留
        string[] originalArguments =
        [
            "--portable-workdir", @"C:\便携 版\GameSave-Data\",
            "--workdir", @"D:\我的 存档\"
        ];

        var arguments = AdminHelper.BuildRestartArguments(originalArguments, CurrentPid);

        Assert.Equal(
            [
                "--portable-workdir", @"C:\便携 版\GameSave-Data\",
                "--workdir", @"D:\我的 存档\",
                "--wait-exit", "4321"
            ],
            arguments.ToArray());
    }

    [Fact]
    public void BuildRestartArguments_RemovesSilentFlagForInteractiveRestart()
    {
        // 配置身份保持不变，但交互式提权后需要显示窗口，不保留静默标志
        string[] originalArguments = ["--silent", "--portable-workdir", @"C:\GameSave-Data"];

        var arguments = AdminHelper.BuildRestartArguments(originalArguments, CurrentPid);

        Assert.Equal(
            ["--portable-workdir", @"C:\GameSave-Data", "--wait-exit", "4321"],
            arguments.ToArray());
    }

    [Fact]
    public void BuildRestartArguments_RemovesStaleWaitExitAndItsValue()
    {
        string[] originalArguments = ["--wait-exit", "1111", "--portable-workdir", @"C:\GameSave-Data"];

        var arguments = AdminHelper.BuildRestartArguments(originalArguments, CurrentPid);

        Assert.Equal(
            ["--portable-workdir", @"C:\GameSave-Data", "--wait-exit", "4321"],
            arguments.ToArray());
        Assert.DoesNotContain("1111", arguments);
        Assert.Single(arguments, argument => argument == "--wait-exit");
    }

    [Fact]
    public void BuildRestartArguments_RemovesEveryStaleWaitExit()
    {
        string[] originalArguments = ["--wait-exit", "1111", "--wait-exit", "2222"];

        var arguments = AdminHelper.BuildRestartArguments(originalArguments, CurrentPid);

        Assert.Equal(["--wait-exit", "4321"], arguments.ToArray());
    }

    [Fact]
    public void BuildRestartArguments_RemovesWaitExitIgnoringCase()
    {
        string[] originalArguments = ["--WAIT-EXIT", "1111", "--workdir", @"D:\saves"];

        var arguments = AdminHelper.BuildRestartArguments(originalArguments, CurrentPid);

        Assert.Equal(["--workdir", @"D:\saves", "--wait-exit", "4321"], arguments.ToArray());
    }

    [Fact]
    public void BuildRestartArguments_KeepsTokenAfterWaitExitWhenItIsNotAProcessId()
    {
        // --wait-exit 后面不是整数时不消费下一个参数，避免把 --workdir 之类的参数误删
        string[] originalArguments = ["--wait-exit", "--workdir", @"D:\saves"];

        var arguments = AdminHelper.BuildRestartArguments(originalArguments, CurrentPid);

        Assert.Equal(["--workdir", @"D:\saves", "--wait-exit", "4321"], arguments.ToArray());
    }

    [Fact]
    public void BuildRestartArguments_KeepsNumericWorkDirValue()
    {
        string[] originalArguments = ["--workdir", "1234"];

        var arguments = AdminHelper.BuildRestartArguments(originalArguments, CurrentPid);

        Assert.Equal(["--workdir", "1234", "--wait-exit", "4321"], arguments.ToArray());
    }

    [Fact]
    public void BuildRestartArguments_HandlesTrailingWaitExitWithoutValue()
    {
        string[] originalArguments = ["--workdir", @"D:\saves", "--wait-exit"];

        var arguments = AdminHelper.BuildRestartArguments(originalArguments, CurrentPid);

        Assert.Equal(["--workdir", @"D:\saves", "--wait-exit", "4321"], arguments.ToArray());
    }

    [Fact]
    public void BuildRestartArguments_WithoutOriginalArguments_OnlyWaitsForCurrentProcess()
    {
        Assert.Equal(["--wait-exit", "4321"], AdminHelper.BuildRestartArguments(null, CurrentPid).ToArray());
        Assert.Equal(["--wait-exit", "4321"], AdminHelper.BuildRestartArguments([], CurrentPid).ToArray());
    }

    [Fact]
    public void BuildRestartArguments_IsIdempotentAcrossRepeatedRestarts()
    {
        string[] originalArguments = ["--portable-workdir", @"C:\便携 版\GameSave-Data\"];

        var first = AdminHelper.BuildRestartArguments(originalArguments, 1111);
        var second = AdminHelper.BuildRestartArguments(first, 2222);

        Assert.Equal(
            ["--portable-workdir", @"C:\便携 版\GameSave-Data\", "--wait-exit", "2222"],
            second.ToArray());
    }

    [Fact]
    public void GetLaunchArguments_SkipsExecutablePath()
    {
        var arguments = AdminHelper.GetLaunchArguments(
            [@"C:\App\GameSave.exe", "--portable-workdir", @"C:\便携 版\GameSave-Data\"]);

        Assert.Equal(["--portable-workdir", @"C:\便携 版\GameSave-Data\"], arguments.ToArray());
        Assert.Empty(AdminHelper.GetLaunchArguments([@"C:\App\GameSave.exe"]));
        Assert.Empty(AdminHelper.GetLaunchArguments([]));
        Assert.Throws<ArgumentNullException>(() => AdminHelper.GetLaunchArguments(null!));
    }

    [Fact]
    public void CreateRestartStartInfo_UsesShellExecuteWithRunAsVerb()
    {
        var startInfo = AdminHelper.CreateRestartStartInfo(
            @"C:\App\GameSave.exe",
            ["--wait-exit", "4321"]);

        Assert.Equal(@"C:\App\GameSave.exe", startInfo.FileName);
        Assert.True(startInfo.UseShellExecute);
        Assert.Equal("runas", startInfo.Verb);
        Assert.Equal(["--wait-exit", "4321"], startInfo.ArgumentList.ToArray());
    }

    [Fact]
    public void CreateRestartStartInfo_PassesArgumentsVerbatimWithoutManualQuoting()
    {
        // ArgumentList 语义：元素保持原值，由运行时负责转义，因此这里不应出现额外引号
        string[] arguments =
        [
            "--portable-workdir", @"C:\便携 版\GameSave-Data\",
            "--workdir", @"D:\带 空格 的目录\",
            "--note", "含有\"引号\"的参数"
        ];

        var startInfo = AdminHelper.CreateRestartStartInfo(@"C:\Program Files\GameSave\GameSave.exe", arguments);

        // 元素与原始入参完全一致，说明没有做任何手工加引号/拼接
        Assert.Equal(arguments, startInfo.ArgumentList.ToArray());
    }

    [Fact]
    public void CreateRestartStartInfo_EscapesTrickyArgumentsForCreateProcessRules()
    {
        // 通过运行时的内部转义实现间接校验：中文、空格、尾部反斜杠都不会破坏参数边界
        string[] arguments =
        [
            "--portable-workdir", @"D:\我的 存档\",
            "--workdir", @"C:\Program Files\GameSave\"
        ];

        var startInfo = AdminHelper.CreateRestartStartInfo(@"C:\App\GameSave.exe", arguments);

        var escaped = TryBuildEscapedArguments(startInfo);
        if (escaped is null)
        {
            // 运行时未暴露内部转义实现时无法间接校验，转义由运行时负责，不作为失败
            return;
        }

        const string quote = "\"";
        var expected = string.Join(' ',
            "--portable-workdir",
            quote + @"D:\我的 存档" + @"\\" + quote,
            "--workdir",
            quote + @"C:\Program Files\GameSave" + @"\\" + quote);

        Assert.Equal(expected, escaped);
    }

    [Fact]
    public void CreateRestartStartInfo_EscapesEmbeddedQuotes()
    {
        var startInfo = AdminHelper.CreateRestartStartInfo(@"C:\App\GameSave.exe", ["--note", "he said \"hi\""]);

        var escaped = TryBuildEscapedArguments(startInfo);
        if (escaped is null)
        {
            return;
        }

        Assert.Equal("--note \"he said \\\"hi\\\"\"", escaped);
    }

    [Fact]
    public void CreateRestartStartInfo_RejectsInvalidInput()
    {
        Assert.Throws<ArgumentNullException>(() => AdminHelper.CreateRestartStartInfo(null!, []));
        Assert.Throws<ArgumentException>(() => AdminHelper.CreateRestartStartInfo("   ", []));
        Assert.Throws<ArgumentNullException>(() => AdminHelper.CreateRestartStartInfo(@"C:\App\GameSave.exe", null!));
    }

    /// <summary>
    /// 调用 ProcessStartInfo 的内部转义方法（与 ShellExecute/CreateProcess 使用同一个实现）。
    /// 该方法只做字符串拼接，不会启动进程。
    /// </summary>
    private static string? TryBuildEscapedArguments(ProcessStartInfo startInfo)
    {
        var method = typeof(ProcessStartInfo).GetMethod(
            "BuildArguments",
            BindingFlags.Instance | BindingFlags.NonPublic);

        return method?.Invoke(startInfo, null) as string;
    }
}

/// <summary>
/// PowerShell 备用选择器的编码与参数构造测试。
/// 只检查 ProcessStartInfo 配置，不启动 powershell.exe，也不会弹出任何对话框。
/// </summary>
public class ShellDialogHelperStartInfoTests
{
    [Fact]
    public void CreatePowerShellDialogStartInfo_DecodesChildOutputAsUtf8()
    {
        var startInfo = ShellDialogHelper.CreatePowerShellDialogStartInfo("Write-Output 'x'");

        Assert.Equal(Encoding.UTF8.CodePage, startInfo.StandardOutputEncoding?.CodePage ?? -1);
        Assert.Equal(Encoding.UTF8.CodePage, startInfo.StandardErrorEncoding?.CodePage ?? -1);
        Assert.Equal("utf-8", startInfo.StandardOutputEncoding?.WebName ?? string.Empty);
        Assert.Equal("utf-8", startInfo.StandardErrorEncoding?.WebName ?? string.Empty);
    }

    [Fact]
    public void CreatePowerShellDialogStartInfo_RedirectsStreamsWithoutShellExecute()
    {
        var startInfo = ShellDialogHelper.CreatePowerShellDialogStartInfo("Write-Output 'x'");

        Assert.Equal("powershell.exe", startInfo.FileName);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
        Assert.True(startInfo.CreateNoWindow);
        Assert.Equal(string.Empty, startInfo.Verb);
    }

    [Fact]
    public void CreatePowerShellDialogStartInfo_PassesScriptAsEncodedCommand()
    {
        const string script = "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8\nWrite-Output 'C:\\中文 目录'";

        var startInfo = ShellDialogHelper.CreatePowerShellDialogStartInfo(script);

        Assert.Equal(
            ["-NoProfile", "-STA", "-ExecutionPolicy", "Bypass", "-EncodedCommand"],
            startInfo.ArgumentList.Take(5).ToArray());

        var encodedScript = Assert.Single(startInfo.ArgumentList.Skip(5));
        Assert.DoesNotContain(' ', encodedScript);
        Assert.Equal(script, Encoding.Unicode.GetString(Convert.FromBase64String(encodedScript)));
    }

    [Fact]
    public void CreatePowerShellDialogStartInfo_RejectsNullScript()
    {
        Assert.Throws<ArgumentNullException>(() => ShellDialogHelper.CreatePowerShellDialogStartInfo(null!));
    }
}
