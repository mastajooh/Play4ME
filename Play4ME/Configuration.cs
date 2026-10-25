using Dalamud.Configuration;

namespace Play4ME;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public Play4MESettings Settings { get; set; } = new();
}
