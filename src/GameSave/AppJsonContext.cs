using System.Text.Json;
using System.Text.Json.Serialization;
using GameSave.Models;
using GameSave.Services;

namespace GameSave;

/// <summary>
/// JSON 序列化源生成器上下文
/// 用于替代运行时反射式序列化，支持 IL Trimming（发布裁剪）场景
/// 所有需要进行 JSON 序列化/反序列化的类型都需要在此注册
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true
)]
[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(Game))]
[JsonSerializable(typeof(SaveFile))]
[JsonSerializable(typeof(CloudConfig))]
[JsonSerializable(typeof(List<Game>))]
[JsonSerializable(typeof(List<CloudConfig>))]
[JsonSerializable(typeof(List<SaveFile>))]
[JsonSerializable(typeof(ExportManifest))]
[JsonSerializable(typeof(List<ImportGamePreview>))]
[JsonSerializable(typeof(List<string>))]
public partial class AppJsonContext : JsonSerializerContext
{
}
