<div align="center">
    <img src="logo.png" alt="GameSave Manager" width="200" />
    <h1>GameSave Manager</h1>
    <p><strong>🎮 简洁优雅的 Windows 游戏存档备份与恢复工具</strong></p>
    <p>
        <img alt="Platform" src="https://img.shields.io/badge/平台-Windows_10%2F11-0078d4?logo=windows&logoColor=white" />
        <img alt="Framework" src="https://img.shields.io/badge/框架-WinUI_3-512BD4?logo=dotnet&logoColor=white" />
        <img alt=".NET" src="https://img.shields.io/badge/.NET-10.0-purple?logo=dotnet&logoColor=white" />
        <img alt="License" src="https://img.shields.io/github/license/yxsj245/gamesave?color=green" />
        <img alt="GitHub Release" src="https://img.shields.io/github/v/release/yxsj245/gamesave?label=最新版本" />
    </p>
</div>

---

## ✨ 项目简介

**GameSave Manager** 是一款基于 **WinUI 3** 构建的 Windows 桌面应用，专注于为玩家提供**简洁、安全、高效**的游戏存档备份与恢复体验。

无论是手动保存关键进度、通过应用启动游戏实现自动存档保护，还是将存档同步到云端实现跨设备迁移 —— GameSave Manager 都能轻松胜任。

> 🚫 不再盲目依赖游戏自带的云同步  
> ✅ 你的存档，由你掌控

---

## 🎯 核心功能

<table>
<tr>
<td width="50%">

### 🗂️ 游戏管理
- 添加、编辑、删除游戏
- 自动提取游戏 exe 图标
- 路径自动替换为环境变量，跨设备通用

</td>
<td width="50%">

### 💾 手动备份
- 随时为存档创建命名备份点
- 支持自定义存档标签名称
- 无限制的手动存档数量

</td>
</tr>
<tr>
<td>

### 🔄 自动备份
- 通过应用启动游戏，退出后自动备份
- 支持 Steam stub 进程检测
- 全流程后台运行，零干扰

</td>
<td>

### ⏱️ 定时备份
- 可配置备份间隔（最小 1 分钟）
- 自动轮转清理旧备份
- 跟随游戏生命周期自动启停

</td>
</tr>
<tr>
<td>

### ☁️ 云端同步
- 支持阿里云 OSS 云存储
- 备份完成后自动上传到云端
- 从云端一键导入恢复游戏配置和存档

</td>
<td>

### 🔍 自动导入游戏
- 支持 Steam、Epic、GOG、Ubisoft、EA、Battle.net
- 自动检测安装的游戏和启动程序
- 批量导入，快速上手

</td>
</tr>
<tr>
<td>

### 📦 数据导入导出
- 将游戏配置和存档导出为 `.zip` 文件
- 跨设备导入，路径自动适配
- 支持批量选择导出游戏

</td>
<td>

### 🖥️ 系统集成
- 系统托盘后台运行
- 游戏退出后通过系统通知告知备份结果
- 支持开机自启动（静默模式）

</td>
</tr>
</table>

---

## 🖼️ 技术架构

```
技术栈: WinUI 3 + .NET 10 + MVVM (CommunityToolkit.Mvvm)

gamesave/
├── docs/                         # 项目文档
├── src/
│   ├── GameSave/                 # WinUI 3 主项目
│   │   ├── Models/               # 数据模型 (Game, SaveFile, CloudConfig)
│   │   ├── Views/                # UI 页面 (XAML)
│   │   ├── ViewModels/           # MVVM 视图模型
│   │   ├── Services/             # 业务服务层
│   │   ├── Controls/             # 自定义控件
│   │   ├── Converters/           # 值转换器
│   │   ├── Helpers/              # 工具类
│   │   └── Assets/               # 资源文件
│   └── GameSave.Installer/       # WiX MSI 安装包项目

├── build-msi.ps1                 # MSI 构建脚本
└── logo.png                      # 项目 Logo
```

---

## 📥 安装

### 方式一：MSI 安装包（推荐）

1. 前往 [Releases](https://github.com/yxsj245/gamesave/releases) 页面
2. 下载最新的 `GameSave-x64-Setup.msi`（或对应平台版本）
3. 双击运行安装程序，按照向导完成安装
4. 启动 GameSave Manager，开始保护你的游戏进度！

### 方式二：便携版（免安装）

1. 前往 [Releases](https://github.com/yxsj245/gamesave/releases) 页面
2. 下载最新的 `GameSave-x64-Portable-vX.X.X.zip`（或对应平台版本）
3. 解压到任意目录
4. 双击 `GameSave.exe` 即可运行，无需安装！

### 方式三：从源码构建

```powershell
# 克隆项目
git clone https://github.com/yxsj245/gamesave.git
cd gamesave

# 还原 NuGet 包
dotnet restore src/GameSave/GameSave.csproj

# 编译运行（Debug 模式）
dotnet run --project src/GameSave/GameSave.csproj

# 或构建 MSI 安装包
.\build-msi.ps1 -Platform x64 -Version 1.0.0

# 或构建便携版 ZIP
.\build-portable.ps1 -Platform x64 -Version 1.0.0
```

---

## 🚀 快速开始

### 1. 添加游戏

点击工具栏上的 **「添加游戏」** 按钮，填写游戏名称和存档目录即可。

> 如果指定了游戏启动进程（`.exe`），列表中会自动提取并显示游戏图标。

### 2. 手动备份

在游戏详情面板中点击 **「备份」** 按钮，即可创建一个命名的备份点。

### 3. 启动游戏 & 自动备份

通过 GameSave Manager 启动游戏后，程序会自动在后台监测游戏进程。当游戏退出后，自动创建一份「退出存档」备份，并通过系统通知告知你备份结果。

### 4. 恢复存档

在游戏详情面板中找到需要恢复的手动存档，点击 **「还原」** 即可将存档恢复到游戏目录。

---

## 📋 存档类型说明

| 类型 | 标签 | 创建方式 | 数量限制 | 可恢复 | 可删除 |
|------|------|----------|----------|:------:|:------:|
| 退出存档 | 🔴 自动 | 游戏退出后自动创建 | 仅保留最新 1 份 | ❌ | ❌ |
| 手动存档 | 🟢 手动 | 用户手动点击备份 | 无限制 | ✅ | ✅ |
| 定时备份 | 🔵 定时 | 按设定间隔自动创建 | 可配置上限 | ✅ | ✅ |

---

## ☁️ 云端同步

GameSave Manager 支持将存档同步到云端存储，当前已支持 **阿里云 OSS**。

1. 在 **设置** 页面添加云端存储配置
2. 添加/编辑游戏时关联云端服务商
3. 备份完成后自动上传到云端
4. 通过 **云端** 页面查看、下载、管理云端存档
5. 换新设备时从云端一键导入恢复

> 详细配置指南请参阅 [云端存档文档](docs/云端存档.md)

---

## 🛠️ 开发指南

### 环境要求

| 要求 | 版本 |
|------|------|
| Windows | 10 (1809+) / 11 |
| .NET SDK | 10.0+ |
| Visual Studio | 2026（可选，用于 XAML 设计器调试） |

### 常用命令

```powershell
# 还原 NuGet 包
dotnet restore src/GameSave/GameSave.csproj

# 编译（Debug）
dotnet build src/GameSave/GameSave.csproj

# 运行应用
dotnet run --project src/GameSave/GameSave.csproj

# 构建 MSI 安装包
.\build-msi.ps1 -Platform x64 -Version 1.0.0
```

### 开发工具推荐

- **Visual Studio 2026** — 完整调试 + XAML 设计器
- **VSCode + C# Dev Kit** — 轻量开发

---

## 📖 文档

| 文档 | 说明 |
|------|------|
| [使用文档](docs/使用文档.md) | 完整的用户使用指南 |
| [开发文档](docs/开发文档.md) | 架构设计、数据模型、业务流程 |
| [环境搭建说明](docs/环境搭建说明.md) | 开发环境配置指南 |
| [云端存档](docs/云端存档.md) | 阿里云 OSS 云端同步使用说明 |
| [导入游戏](docs/导入游戏.md) | 多平台游戏自动导入功能 |
| [定时备份](docs/定时备份.md) | 定时备份功能说明 |
| [开机自启动](docs/开机自启动.md) | 开机自启动配置与原理 |
| [数据导入导出](docs/数据导入导出.md) | 游戏配置与存档的导入导出 |
| [MSI 打包指南](docs/MSI打包指南.md) | MSI 安装包构建流程 |
| [便携版打包指南](docs/便携版打包指南.md) | 便携版 ZIP 构建流程 |

---

## 🤝 贡献

欢迎提交 Issue 和 Pull Request！

1. Fork 本仓库
2. 创建你的特性分支 (`git checkout -b feature/amazing-feature`)
3. 提交你的修改 (`git commit -m '添加某个很棒的功能'`)
4. 推送到分支 (`git push origin feature/amazing-feature`)
5. 提交 Pull Request

---

## 💬 交流群

如果你有任何问题、建议或想法，欢迎加入 QQ 群交流！

🐧 **QQ 群**：`1053482216`

---

## 💖 赞助

如果你觉得这个项目对你有帮助，欢迎通过爱发电赞助支持开发者持续维护和更新！

👉 [**爱发电 - 赞助本项目**](https://ifdian.net/a/xiaozhuhouses)

---

## 📄 许可证

本项目采用 [MIT](LICENSE) 许可证开源。

---

## 🌟 致谢

- [WinUI 3](https://github.com/microsoft/microsoft-ui-xaml) — 现代 Windows 原生 UI 框架
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) — 轻量级 MVVM 框架
- [WiX Toolset](https://wixtoolset.org/) — Windows Installer 打包工具
- [Aliyun OSS SDK](https://github.com/aliyun/aliyun-oss-csharp-sdk) — 阿里云对象存储 SDK
- [Game Save Manager (旧版)](https://github.com/dyang886/Game-Save-Manager) — 项目灵感来源
