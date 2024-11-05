using Newtonsoft.Json;

namespace CurseforgeModpackLoader.Curseforge;

[JsonObject]
public class CuseforgeMinecraftModLoader
{
    [JsonProperty("id")]
    public string Id { get; private set; }
    [JsonProperty("primary")]
    public bool Primary { get; private set; }

    public override string ToString()
    {
        var primary = Primary ? " *" : "";
        return $"Mod Loader: {Id}{primary}";
    }
}