using Newtonsoft.Json;

namespace CurseforgeModpackLoader.Curseforge;

[JsonObject]
public class CurseforgeMinecraftConfig
{
    [JsonProperty("modLoaders")]
    public CuseforgeMinecraftModLoader[] ModLoaders { get; private set; }
    [JsonProperty("version")]
    public string Version { get; private set; }
}