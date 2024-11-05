using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommandLine;
using CurseForge.APIClient;
using CurseForge.APIClient.Models.Mods;
using CurseforgeModpackLoader.Curseforge;
using CurseforgeModpackLoader.Modrinth;
using Newtonsoft.Json;
using Pastel;
using File = System.IO.File;

namespace CurseforgeModpackLoader;

public static partial class App
{
    [Verb("load", HelpText = "Load Modpack From File")]
    private class ModpackOptions
    {

        [Option('i', "input", Required = true, HelpText = "Modpack configuration archive")]
        public string Input { get; set; }

        [Option('o', "output", Required = true, HelpText = "Output directory")]
        public string Output { get; set; }

        [Option('k', "key", HelpText = "Curseforge API key")]
        public string? ApiKey { get; set; }

        [Option('p', "skip_option_pick", HelpText = "Automatically skips options pick", Default = false)]
        public bool Skip { get; set; }


        [Option('c', "client", HelpText = "Include client mods", Default = false)]
        public bool Client { get; set; }

        [Option('s', "server", HelpText = "Include server mods", Default = false)]
        public bool Server { get; set; }
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
        if (options.ApiKey == null)
        {
            Console.WriteLine("Cursefotge Api Key is missing!");
            return -2;
        }

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

        for (var i = 0; i <= Lines.Length; i++)
            Console.WriteLine();

        var http = new HttpClient();
        foreach (var file in manifest.Files)
        {
            var div = file.Path.IndexOf('/');
            var tag = file.Path[..(div - 1)];
            var name = file.Path[(div + 1)..];

            var client = options.Client && file.Environments["client"] == "required";
            var server = options.Server && file.Environments["server"] == "required";
            var load = client || server;
            do
            {
                if (load) break;
                if (options.Skip)
                {
                    lock (Lines)
                    {
                        Console.SetCursorPosition(0, Lines.Length);
                        Console.Write(new string(' ', Console.WindowWidth) + "\r");
                        Console.ForegroundColor = ConsoleColor.Blue;
                        Console.Write($"Skipping {tag} {name}...");
                        Console.ResetColor();
                    }
                    break;
                }

                var clientTag = client ? " client" : "";
                var serverTag = server ? " server" : "";
                lock (Lines)
                {
                    Console.Clear();
                    Console.WriteLine($"Load optional{clientTag}{serverTag} {tag} {name}? (y/n)");
                    var code = Console.ReadLine()?.ToLower();
                    if (code == "y")
                    {
                        load = true;
                        break;
                    }

                    if (code == "n") break;
                    Console.WriteLine("Invalid input. Try again.");
                }
            } while (true);

            if (!load) continue;
            while (true)
            {
                var placed = false;
                for (var i = 0; i < Lines.Length; i++)
                    if (!Lines[i].used)
                    {
                        LoadModrinthAsset(i, options.Output, file, http);
                        placed = true;
                        break;
                    }

                if (placed) break;
            }
            // await LoadModrinthAsset(0, options.Output, file, http);
        }
        while (Lines.Any(used => used.used)) { }
        Console.Clear();
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
        Console.WriteLine($"Modpack Name: {manifest.Name}");
        Console.WriteLine($"Modpack Version: {manifest.VersionId}");
        foreach (var loader in manifest.Dependencies)
            Console.WriteLine(loader.Key + ": " + loader.Value);
        return 0;
    }

    private static async Task LoadModrinthAsset(
        int index,
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
            lock (Lines)
            { 
                Console.SetCursorPosition(0, Lines.Length);
                Console.Write(new string(' ', Console.WindowWidth) + "\r");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write($"{upperTag} {name} already downloaded! Skipping...");
                Console.ResetColor();
            }
            return;
        }
        await DownloadAsset(index, tag, name, file.Downloads[0], http, path);
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

        for (var i = 0; i <= Lines.Length; i++)
            Console.WriteLine();

        List<string> manualUrls = [];
        var http = new HttpClient();
        var client = new ApiClient(options.ApiKey);
        foreach (var file in manifest.Files)
        {
            var mod = await client.GetModAsync(file.ProjectId);
            if (mod.Error != null)
            {
                lock (Lines)
                {
                    Console.SetCursorPosition(0, Lines.Length);
                    Console.Write(new string(' ', Console.WindowWidth) + "\r");
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write($"Data loading error:  {mod.Error.ErrorCode} - {mod.Error.ErrorMessage}");
                    Console.ResetColor();
                }

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
                do
                {
                    lock (Lines)
                    {
                        Console.Clear();
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
                    }
                } while (true);

                if (!load) continue;
            }

            var modFile = await client.GetModFileAsync(file.ProjectId, file.FileId);
            if (mod.Error != null)
            {
                lock (Lines)
                {
                    Console.SetCursorPosition(0, Lines.Length);
                    Console.Write(new string(' ', Console.WindowWidth) + "\r");
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write($"File data loading error:  {mod.Error.ErrorCode} - {mod.Error.ErrorMessage}");
                    Console.ResetColor();
                }

                continue;
            }

            while (true)
            {

                var placed = false;
                for (var i = 0; i < Lines.Length; i++)
                    if (!Lines[i].used)
                    {
                        LoadCurseforgeAsset(i, options.Output, mod.Data, modFile.Data, tag, client, http, manualUrls,
                            options.Skip);
                        placed = true;
                        break;
                    }

                if (placed) break;
            }
            // await LoadCurseforgeAsset(options.Output, mod.Data, modFile.Data, tag, client, http, manualUrls, options.Skip);
        }
        while (Lines.Any(used => used.used)) { }
        Console.Clear();
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
        Console.WriteLine($"Modpack Name: {manifest.Name}");
        Console.WriteLine($"Modpack Version: {manifest.Version}");
        if (manifest.CurseforgeMinecraft.ModLoaders.Length != 0)
            foreach (var loader in manifest.CurseforgeMinecraft.ModLoaders)
                Console.WriteLine(loader);
        Console.WriteLine($"Minecraft Version: {manifest.CurseforgeMinecraft.Version}");
        return 0;
    }

    private static async Task LoadCurseforgeAsset(
        int index,
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
        var mods = Path.Combine(assets, mod.ClassId switch
        {
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
            lock (Lines)
            {
                Console.SetCursorPosition(0, Lines.Length);
                Console.Write(new string(' ', Console.WindowWidth) + "\r");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write($"{upperTag} {mod.Name} already downloaded! Skipping...");
                Console.ResetColor();
            }
            return;
        }

        if (modFile.DownloadUrl != null)
            await DownloadAsset(index, tag, mod.Name, modFile.DownloadUrl, http, path);
        else {
            File.Delete(path);
            lock (Lines)
            {
                Console.SetCursorPosition(0, Lines.Length);
                Console.Write(new string(' ', Console.WindowWidth) + "\r");
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write($"Error on loading {tag} {mod.Name}. \t FileName: {modFile.FileName}");
                Console.ResetColor();
            }

            if (skip)
            {
                manualUrls.Add($"{mod.Links.WebsiteUrl}/files/{modFile.Id}");
                return;
            }

            do
            {
                lock (Lines)
                {
                    Console.Clear();
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
                }
            } while (true);

            ModLoaderType? type = null;
            string version = null;
            foreach (var gameVersion in modFile.GameVersions)
                if (Enum.TryParse(gameVersion, out ModLoaderType t)) type = t;
                else if (VersionRegex().IsMatch(gameVersion)) version = gameVersion;
            var variants = await client.GetModFilesAsync(mod.Id, version, type);
            if (variants.Error != null)
            {
                lock (Lines)
                { 
                    Console.SetCursorPosition(0, Lines.Length);
                    Console.Write(new string(' ', Console.WindowWidth) + "\r");
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write($"File data loading error:  {variants.Error.ErrorCode} - {variants.Error.ErrorMessage}");
                    Console.ResetColor();
                }

                return;
            }

            var files = variants.Data.Where(file => file.DownloadUrl != null).ToList();
            if (files.Count == 0)
            {
                var url = $"{mod.Links.WebsiteUrl}/files/{modFile.Id}";
                manualUrls.Add(url);
                lock (Lines)
                {
                    Console.SetCursorPosition(0, Lines.Length);
                    Console.Write(new string(' ', Console.WindowWidth) + "\r");
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write($"Another variants not found! You can load manually: {url}");
                    Console.ResetColor();
                }

                return;
            }

            CurseForge.APIClient.Models.Files.File file;
            do
            {
                lock (Lines)
                {
                    Console.Clear();
                    Console.WriteLine($"Variants (0-{files.Count}):");
                    for (var i = 0; i < files.Count; i++)
                    {
                        Console.WriteLine($"{i} - {files[i].FileName}");
                    }

                    if (int.TryParse(Console.ReadLine()?.ToLower(), out var code) && code >= 0 && code < files.Count)
                    {
                        file = files[code];
                        break;
                    }

                    Console.WriteLine("Invalid input. Try again.");
                }
            } while (true);

            await LoadCurseforgeAsset(index, assets, mod, file, tag, client, http, manualUrls, skip);
        }
    }
    
    private static readonly (bool used, float speed)[] Lines = new (bool, float)[8];

    private static async Task DownloadAsset(int index, string tag, string name, string url, HttpClient http,
        string path)
    {
        Lines[index] = (true, 0);
        var progress = new Progress<HttpClientProgressExtensions.FileLoadingProgress>();
        var pr = new HttpClientProgressExtensions.FileLoadingProgress();
        progress.ProgressChanged += (_, p) =>
        {
            lock (Lines)
            {
                var speed = Lines[index].speed;
                Console.SetCursorPosition(0, index);
                Console.Write($"{GetText(tag, name, p, ref speed)}\r");
                Lines[index] = (Lines[index].used, speed);
                pr = p;
            }
        };
        var tempPath = $"{path}.onload";
        await using (var fileStream = new FileStream(tempPath, FileMode.Create,
                         FileAccess.Write, FileShare.None))
            await http.DownloadDataAsync(url, fileStream, progress);

        File.Move(tempPath, path);
        lock (Lines)
        {
            var speed = Lines[index].speed;
            Console.SetCursorPosition(0, index);
            Console.Write(new string(' ', Console.WindowWidth));
            Lines[index] = (false, speed);
        }
    }


    private const string Header = "Loading {0} {1} ";
    private const string Footer = " {0:F2} %  {1:F3} {2}  {3:F3}/{4:F3} {5}";
    private static readonly string[] Speeds = [
        "bit/s",
        "Kbit/s",
        "Mbit/s",
        "Gbit/s",
        "Tbit/s"
    ];
    private static readonly string[] Sizes = [
        "b",
        "Kb",
        "Mb",
        "Gb",
        "Tb"
    ];
    private static string GetText(string tag, string name, HttpClientProgressExtensions.FileLoadingProgress progress, ref float avgSpeed)
    {
        avgSpeed *= 0.99f;
        avgSpeed += 0.01f * float.Max(0, progress.Speed);
        var speed = avgSpeed;
        var spi = 0;
        while (speed > 1024)
        {
            speed /= 1024;
            spi++;
            if (spi == Speeds.Length - 1) break;
        }
        
        var total = (float) progress.TotalBytes;
        var loaded = (float) progress.LoadedBytes;
        var sii = 0;
        while (total > 1024)
        {
            total /= 1024;
            loaded /= 1024;
            sii++;
            if (sii == Sizes.Length - 1) break;
        }

        var headerLen = string.Format(Header, tag, name).Length;
        var footerLen = string.Format(Footer, progress.Percentage * 100, speed, Speeds[spi], loaded, total, Sizes[sii]).Length;
        
        var len = Console.WindowWidth - int.Max(70, headerLen) - int.Max(35, footerLen) - 10;
        var f = progress.Percentage * len;
        var c = (int)f;
        var bar = new string(' ', int.Max(1, 71 - headerLen)) + "|";
        bar += new string('\u25a0', c);
        bar += new string(' ', len - c) + "|";
        bar += new string(' ',int.Max(1, 36 - footerLen));
        
        
        return $"{string.Format(Header, tag, name)}{bar.Pastel(Color.Green)}{string.Format(
            Footer, progress.Percentage * 100, speed, Speeds[spi], loaded, total, Sizes[sii])}";
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