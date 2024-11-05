using Newtonsoft.Json;

namespace CurseforgeModpackLoader.Curseforge;

[JsonObject]
public class CurseforgeModpackManifest
{
    [JsonProperty("author")]
    public string Author { get; private set; }
    [JsonProperty("files")]
    public CurseforgeFile[] Files { get; private set; }
    [JsonProperty("manifestType")]
    public string ManifestType { get; private set; }
    [JsonProperty("manifestVersion")]
    public int ManifestVersion { get; private set; }
    [JsonProperty("minecraft")]
    public CurseforgeMinecraftConfig CurseforgeMinecraft { get; private set; }
    [JsonProperty("name")]
    public string Name { get; private set; }
    [JsonProperty("overrides")]
    public string Overrides { get; private set; }
    [JsonProperty("version")]
    public string Version { get; private set; }
    
}