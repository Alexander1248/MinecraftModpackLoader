using System.Collections.Generic;
using Newtonsoft.Json;

namespace CurseforgeModpackLoader.Modrinth;

[JsonObject]
public class ModrinthFile
{
    [JsonProperty("path")]
    public string Path { get; private set; }
    [JsonProperty("hashes")]
    public IDictionary<string, string> Hashes { get; private set; }
    [JsonProperty("env")]
    public IDictionary<string, string> Environments { get; private set; }
    
    [JsonProperty("downloads")]
    public IList<string> Downloads { get; private set; }
    
    [JsonProperty("fileSize")]
    public int FileSize { get; private set; }
}