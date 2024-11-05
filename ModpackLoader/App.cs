﻿using System.IO.Compression;
using System.Text.RegularExpressions;
using CommandLine;
using CurseForge.APIClient;
using CurseForge.APIClient.Models.Mods;
using CurseforgeModpackLoader.Curseforge;
using CurseforgeModpackLoader.Modrinth;
using Modrinth;
using Newtonsoft.Json;
using File = System.IO.File;

namespace CurseforgeModpackLoader;

public static partial class App
{
    [Verb("modpack", HelpText = "Load modpack")]
    private class ModpackOptions {
        [Option('k', "key", Required = true, HelpText = "Curseforge API key")]
        public string ApiKey { get; set; }
        
        [Option('i', "input", Required = true, HelpText = "Modpack configuration archive")]
        public string Input { get; set; }
        
        [Option('o', "output", Required = true, HelpText = "Output directory")]
        public string Output { get; set; }
        
        [Option('s', "skip_on_error", HelpText = "Automatically skips file if errors occur", Default = false)]
        public bool Skip { get; set; }
    }
    public static async Task<int> Main(string[] args)
    {
        return await Parser.Default.ParseArguments<ModpackOptions>(args)
            .MapResult(
                ModPackLoader,
                HandleParseError);
    }
    private static async Task<int> ModPackLoader(ModpackOptions options)
    {
        await using var stream = File.OpenRead(options.Input);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        if (Path.GetExtension(options.Input) == ".mrpack")
        {
            Console.WriteLine("Starting load of Modrinth Modpack...");
            return await LoadModrinthModpack(options, archive);
        }
        
        Console.WriteLine("Starting load of Curseforge Modpack...");
        return await LoadCurseForgeModpack(options, archive);
    }

    private static async Task<int> LoadModrinthModpack(ModpackOptions options, ZipArchive archive)
    {
        var manifestEntry = archive.GetEntry("modrinth.index.json");
        if (manifestEntry == null)
        {
            Console.WriteLine("Could not find modpack manifest!");
            return -1;
        }

        ModrinthModpackManifest? manifest;
        await using (var manifestStream = manifestEntry.Open())
        using (var manifestReader = new StreamReader(manifestStream))
            manifest = JsonConvert.DeserializeObject<ModrinthModpackManifest>(manifestReader.ReadToEnd());
        if (manifest == null)
        {
            Console.WriteLine("Could not parse manifest.json");
            return -1;
        }
        var http = new HttpClient();
        foreach (var file in manifest.Files)
        {
            var div = file.Path.IndexOf('/');
            var tag = file.Path[..(div - 1)];
            var name = file.Path[(div + 1)..];
            if (file.Environments["client"] != "required" && file.Environments["server"] != "required")
            {
                bool load;
                do {
                    Console.WriteLine($"Load optional {tag} {name}? (y/n)");
                    var code = Console.ReadLine()?.ToLower();
                    if (code == "y")
                    {
                        load = true;
                        break;
                    }
                    if (code == "n")
                    {
                        load = false;
                        break;
                    }
                    Console.WriteLine("Invalid input. Try again.");
                } while (true);
                if (!load) continue;
            }
            await LoadModrinthAsset(options.Output, file, http);
        }
        Console.WriteLine("Asset loading complete! Applying overrides...");
        
        foreach (var entry in archive.Entries)
            if (entry.FullName.StartsWith("overrides"))
            {
                var path = Path.Combine(options.Output, entry.FullName[10..]);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if (entry.ExternalAttributes != (int)FileAttributes.Directory) 
                    entry.ExtractToFile(path, true);
                Console.WriteLine($"Extracted override: {path}");
            }
        
        Console.WriteLine("Overrides applied!");
        Console.WriteLine("Version Info:");
        foreach (var loader in manifest.Dependencies)
            Console.WriteLine(loader.Key + ": " + loader.Value);
        return 0;
    }
    private static async Task LoadModrinthAsset(
        string assets, 
        ModrinthFile file, 
        HttpClient http)
    {
        var div = file.Path.IndexOf('/');
        var dir = file.Path[..div];
        var name = file.Path[(div + 1)..];
        var tag = dir[..^1];
        var upperTag = $"{char.ToUpper(tag[0])}{tag[1..]}";
        var mods = Path.Combine(assets, dir);
        Directory.CreateDirectory(mods);
        var path = Path.Combine(mods, name);
        if (File.Exists(path))
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"{upperTag} {name} already downloaded! Skipping...");
            Console.ResetColor();
            return;
        }
        try
        { 
            var progress = new Progress<float>();
            progress.ProgressChanged += (_, p) => {
                Console.Write($"Loading {tag} {name} {p * 100:F3} %\r");
                if (p == 1) Console.WriteLine();
            };
            await using var fileStream = new FileStream(path, FileMode.Create,
                FileAccess.Write, FileShare.None);
            await http.DownloadDataAsync(file.Downloads[0], fileStream, progress);
        }
        catch
        {
            File.Delete(path);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error on loading {tag} {name}. {upperTag}: {name}");
            Console.ResetColor();
        }
    }

    private static async Task<int> LoadCurseForgeModpack(ModpackOptions options, ZipArchive archive)
    {
        var manifestEntry = archive.GetEntry("manifest.json");
        if (manifestEntry == null)
        {
            Console.WriteLine("Could not find modpack manifest!");
            return -1;
        }

        CurseforgeModpackManifest? manifest;
        await using (var manifestStream = manifestEntry.Open())
        using (var manifestReader = new StreamReader(manifestStream))
            manifest = JsonConvert.DeserializeObject<CurseforgeModpackManifest>(manifestReader.ReadToEnd());
        if (manifest == null)
        {
            Console.WriteLine("Could not parse manifest.json");
            return -1;
        }

        List<string> manualUrls = [];
        var http = new HttpClient();
        var client = new ApiClient(options.ApiKey);
        foreach (var file in manifest.Files)
        {
            var mod = await client.GetModAsync(file.ProjectId);
            if (mod.Error != null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Data loading error:  {mod.Error.ErrorCode} - {mod.Error.ErrorMessage}");
                Console.ResetColor();
                continue;
            }
            var tag = mod.Data.ClassId switch
            {
                6 => "mod",
                12 => "resourcepack",
                4471 => "modpack",
                6945 => "datapack",
                17 => "world",
                6552 => "shaderpack",
                _ => throw new ArgumentOutOfRangeException()
            };
            if (!file.Required)
            {
                bool load;
                do {
                    Console.WriteLine($"Load optional {tag} {mod.Data.Name}? (y/n)");
                    var code = Console.ReadLine()?.ToLower();
                    if (code == "y")
                    {
                        load = true;
                        break;
                    }
                    if (code == "n")
                    {
                        load = false;
                        break;
                    }
                    Console.WriteLine("Invalid input. Try again.");
                } while (true);
                if (!load) continue;
            }
            var modFile = await client.GetModFileAsync(file.ProjectId, file.FileId);
            if (mod.Error != null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"File data loading error:  {mod.Error.ErrorCode} - {mod.Error.ErrorMessage}");
                Console.ResetColor();
                continue;
            }
            await LoadCurseforgeAsset(options.Output, mod.Data, modFile.Data, tag, client, http, manualUrls, options.Skip);
        }
        Console.WriteLine("Asset loading complete! Applying overrides...");
        
        foreach (var entry in archive.Entries)
            if (entry.FullName.StartsWith(manifest.Overrides))
            {
                var path = Path.Combine(options.Output, entry.FullName[10..]);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if (entry.ExternalAttributes != (int)FileAttributes.Directory) 
                    entry.ExtractToFile(path, true);
                Console.WriteLine($"Extracted override: {path}");
            }
        
        Console.WriteLine("Overrides applied!");

        if (manualUrls.Count != 0)
        {
            Console.WriteLine("Manual Loading:");
            manualUrls.ForEach(Console.WriteLine);
        }
        Console.WriteLine("Version Info:");
        if (manifest.CurseforgeMinecraft.ModLoaders.Length != 0)
            foreach (var loader in manifest.CurseforgeMinecraft.ModLoaders)
                Console.WriteLine(loader);
        Console.WriteLine($"Minecraft Version: {manifest.CurseforgeMinecraft.Version}");
        return 0;
    }

    private static async Task LoadCurseforgeAsset(
        string assets, 
        Mod mod, 
        CurseForge.APIClient.Models.Files.File modFile, 
        string tag,
        ApiClient client,
        HttpClient http,
        List<string> manualUrls,
        bool skip)
    {
        var upperTag = $"{char.ToUpper(tag[0])}{tag[1..]}";
        var mods = Path.Combine(assets, mod.ClassId switch {
            6 => "mods",
            12 => "resourcepacks",
            4471 => "modpacks",
            6945 => "datapacks",
            17 => "saves",
            6552 => "shaderpacks",
            _ => throw new ArgumentOutOfRangeException()
        });
        Directory.CreateDirectory(mods);
        var path = Path.Combine(mods, modFile.FileName);
        if (File.Exists(path))
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"{upperTag} {mod.Name} already downloaded! Skipping...");
            Console.ResetColor();
            return;
        }
        try
        { 
            var progress = new Progress<float>();
            progress.ProgressChanged += (_, p) => {
                Console.Write($"Loading {tag} {mod.Name} {p * 100:F3} %\r");
                if (p == 1) Console.WriteLine();
            };
            await using var fileStream = new FileStream(path, FileMode.Create,
                FileAccess.Write, FileShare.None);
            await http.DownloadDataAsync(modFile.DownloadUrl, fileStream, progress);
        }
        catch
        {
            File.Delete(path);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error on loading {tag} {mod.Name}. {upperTag}: {mod.Name} FileName: {modFile.FileName}");
            Console.ResetColor();
            if (skip)
            {
                manualUrls.Add($"{mod.Links.WebsiteUrl}/files/{modFile.Id}");
                return;
            }
            do {
                Console.WriteLine($"Load another version of {tag} {mod.Name}? (y/n)");
                var code = Console.ReadLine()?.ToLower();
                if (code == "y")
                    break;
                if (code == "n")
                {
                    var url = $"{mod.Links.WebsiteUrl}/files/{modFile.Id}";
                    manualUrls.Add(url);
                    
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"You can load manually: {url}");
                    Console.ResetColor();
                    return;
                }
                Console.WriteLine("Invalid input. Try again.");
            } while (true);
            ModLoaderType? type = null;
            string version = null;
            foreach (var gameVersion in modFile.GameVersions)
                if (Enum.TryParse(gameVersion, out ModLoaderType t)) type = t;
                else if (VersionRegex().IsMatch(gameVersion)) version = gameVersion;
            var variants = await client.GetModFilesAsync(mod.Id, version, type);
            if (variants.Error != null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"File data loading error:  {variants.Error.ErrorCode} - {variants.Error.ErrorMessage}");
                Console.ResetColor();
                return;
            }
            var files = variants.Data.Where(file => file.DownloadUrl != null).ToList();
            if (files.Count == 0)
            {
                var url = $"{mod.Links.WebsiteUrl}/files/{modFile.Id}";
                manualUrls.Add(url);
                
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Another variants not found! You can load manually: {url}");
                Console.ResetColor();
                return;
            }
            do {
                Console.WriteLine($"Variants (0-{files.Count}):");
                for (var i = 0; i < files.Count; i++)
                    Console.WriteLine($"{i} - {files[i].FileName}");
                if (int.TryParse(Console.ReadLine()?.ToLower(), out var code) && code >= 0 && code < files.Count)
                {
                    await LoadCurseforgeAsset(assets, mod, files[code], tag, client, http, manualUrls, skip);
                    break;
                }
                Console.WriteLine("Invalid input. Try again.");
            } while (true);
        }
    }
    
    private static Task<int> HandleParseError(IEnumerable<Error> errs)
    {
        foreach (var error in errs)
            Console.WriteLine(error.ToString());
        return Task.FromResult(-1);
    }

    [GeneratedRegex(@"^[\d]+.[\d]+.[\d]+")]
    private static partial Regex VersionRegex();
}