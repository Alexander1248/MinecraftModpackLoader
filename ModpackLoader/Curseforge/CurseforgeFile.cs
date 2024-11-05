using Newtonsoft.Json;

namespace CurseforgeModpackLoader.Curseforge;

[JsonObject]
public class CurseforgeFile
{
    [JsonProperty("fileID")]
    public int FileId { get; private set; }
    [JsonProperty("projectID")]
    public int ProjectId { get; private set; }
    [JsonProperty("required")]
    public bool Required { get; private set; }
}