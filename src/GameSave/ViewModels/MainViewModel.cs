using System.Collections.ObjectModel;
using GameSave.Models;

namespace GameSave.ViewModels;

public partial class MainViewModel : BaseViewModel
{
    public MainViewModel()
    {
        Title = "Home";
        LoadMockData();
    }

    private ObservableCollection<Game> _games = new();
    public ObservableCollection<Game> Games
    {
        get => _games;
        set => SetProperty(ref _games, value);
    }

    private Game? _selectedGame;
    public Game? SelectedGame
    {
        get => _selectedGame;
        set
        {
            if (SetProperty(ref _selectedGame, value))
            {
                OnSelectedGameChanged(value);
            }
        }
    }

    private ObservableCollection<SaveFile> _currentSaves = new();
    public ObservableCollection<SaveFile> CurrentSaves
    {
        get => _currentSaves;
        set => SetProperty(ref _currentSaves, value);
    }

    private bool _isDetailsVisible = false;
    public bool IsDetailsVisible
    {
        get => _isDetailsVisible;
        set
        {
            if (SetProperty(ref _isDetailsVisible, value))
            {
                OnPropertyChanged(nameof(DetailsVisibility));
            }
        }
    }

    public Microsoft.UI.Xaml.Visibility DetailsVisibility => IsDetailsVisible ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    private void OnSelectedGameChanged(Game? value)
    {
        CurrentSaves.Clear();

        if (value != null)
        {
            // 选中的游戏改变时，生成一些模拟存档记录
            var random = new Random();
            int saveCount = random.Next(2, 6);
            for (int i = 0; i < saveCount; i++)
            {
                CurrentSaves.Add(new SaveFile
                {
                    Name = $"Save {i + 1}",
                    BackupTime = DateTime.Now.AddDays(-i).AddHours(random.Next(-5, 5)),
                    SizeBytes = random.Next(1024 * 1024 * 5, 1024 * 1024 * 50),
                    Description = i == 0 ? "打败了女武神" : "开局存档",
                    StorageType = i % 2 == 0 ? StorageType.Local : StorageType.Cloud
                });
            }
            // 展现详情界面
            IsDetailsVisible = true;
        }
        else
        {
            // 如果清空了选择，则隐藏详情
            IsDetailsVisible = false;
        }
    }

    // 后台代码处理关闭
    public void CloseDetails()
    {
        IsDetailsVisible = false;
        SelectedGame = null;
    }

    private void LoadMockData()
    {
        Games.Add(new Game
        {
            Name = "ELDEN RING",
            SaveFolderPath = @"C:\Users\Admin\AppData\Roaming\EldenRing",
            IconPath = "\uE7FC" // 用一个默认符号占位
        });

        Games.Add(new Game
        {
            Name = "Cyberpunk 2077",
            SaveFolderPath = @"C:\Users\Admin\Saved Games\CD Projekt Red\Cyberpunk 2077",
            IconPath = "\uE7FC"
        });

        Games.Add(new Game
        {
            Name = "Baldur's Gate 3",
            SaveFolderPath = @"C:\Users\Admin\AppData\Local\Larian Studios\Baldur's Gate 3\PlayerProfiles\Public\Savegames\Story",
            IconPath = "\uE7FC"
        });

        // if (Games.Count > 0)
        // {
        //     SelectedGame = Games[0]; // 默认不选中
        // }
    }
}
