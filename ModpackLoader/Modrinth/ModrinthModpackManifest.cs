using Newtonsoft.Json;

namespace CurseforgeModpackLoader.Modrinth;

[JsonObject]
public class ModrinthModpackManifest
{
    [JsonProperty("game")]
    public string Game { get; private set; }
    [JsonProperty("formatVersion")]
    public int FormatVersion { get; private set; }
    [JsonProperty("versionId")]
    public string VersionId { get; private set; }
    [JsonProperty("name")]
    public string Name { get; private set; }
    [JsonProperty("summary")]
    public string Summary { get; private set; }
    
    [JsonProperty("files")]
    public ModrinthFile[] Files { get; private set; }
    
    [JsonProperty("dependencies")]
    public IDictionary<string, string> Dependencies { get; private set; }
    
}