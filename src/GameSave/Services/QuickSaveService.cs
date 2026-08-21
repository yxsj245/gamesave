using GameSave.Models;

namespace GameSave.Services;

public interface IQuickSaveGameService
{
    bool IsGameRunning { get; }
    string? RunningGameId { get; }
    Task<SaveFile> QuickBackupAsync(Game game, DateTimeOffset backupTime);
}

public enum QuickSaveResultKind
{
    Success,
    NoRunningGame,
    GameNotFound,
    Busy,
    Failed
}

public sealed record QuickSaveResult(QuickSaveResultKind Kind, string Message, SaveFile? SaveFile = null);

/// <summary>
/// 协调全局快捷键触发的即时存档。
/// </summary>
public sealed class QuickSaveService
{
    private readonly ConfigService _configService;
    private readonly IQuickSaveGameService _gameService;
    private readonly Action<string, string> _showNotification;
    private int _isExecuting;

    public bool IsExecuting => Volatile.Read(ref _isExecuting) != 0;

    public QuickSaveService(
        ConfigService configService,
        IQuickSaveGameService gameService,
        Action<string, string> showNotification)
    {
        _configService = configService;
        _gameService = gameService;
        _showNotification = showNotification;
    }

    public async Task<QuickSaveResult> ExecuteAsync(DateTimeOffset? backupTime = null)
    {
        if (Interlocked.CompareExchange(ref _isExecuting, 1, 0) != 0)
        {
            const string busyMessage = "上一份快捷存档仍在处理中，请稍候";
            _showNotification("⏳ 快捷存档进行中", busyMessage);
            return new QuickSaveResult(QuickSaveResultKind.Busy, busyMessage);
        }

        try
        {
            if (!_gameService.IsGameRunning || string.IsNullOrWhiteSpace(_gameService.RunningGameId))
            {
                const string noGameMessage = "当前没有通过 GameSave Manager 启动并正在运行的游戏";
                _showNotification("⚠️ 暂无可快捷存档的游戏", noGameMessage);
                return new QuickSaveResult(QuickSaveResultKind.NoRunningGame, noGameMessage);
            }

            var game = _configService.GetGameById(_gameService.RunningGameId);
            if (game == null)
            {
                const string missingGameMessage = "运行中的游戏配置已不存在，本次快捷存档已取消";
                _showNotification("❌ 快捷存档失败", missingGameMessage);
                return new QuickSaveResult(QuickSaveResultKind.GameNotFound, missingGameMessage);
            }

            try
            {
                var save = await _gameService.QuickBackupAsync(game, backupTime ?? DateTimeOffset.Now);
                var successMessage = $"「{game.Name}」已保存为“{save.Name}”";
                _showNotification("✅ 快捷存档成功", successMessage);
                return new QuickSaveResult(QuickSaveResultKind.Success, successMessage, save);
            }
            catch (Exception ex)
            {
                var failureMessage = $"「{game.Name}」快捷存档失败：{ex.Message}";
                _showNotification("❌ 快捷存档失败", failureMessage);
                return new QuickSaveResult(QuickSaveResultKind.Failed, failureMessage);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _isExecuting, 0);
        }
    }
}
