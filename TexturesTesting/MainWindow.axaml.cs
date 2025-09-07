using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CodeWalker.GameFiles;
using CodeWalker.Utils;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using Salaros.Configuration;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TexturesTesting;

public partial class MainWindow : Window
{
    private static string? _vPath;
    private GameFileCache _gameFileCache;
    private static readonly ExtractTask _globalExtractTask = new();

    private string config = AppDomain.CurrentDomain.BaseDirectory + @"config.ini";

    public MainWindow()
    {
        InitializeComponent();
        ToggleControls(false);
        BtnLookEnts.IsEnabled = false;
    }

    private void ToggleControls(bool state)
    {
        //Terrible coding right here, I'm sorry.
        cbExtractTextures.IsEnabled = state;
        cbExtractXml.IsEnabled = state;
        CBoxExtractType.IsEnabled = state;
        BtnLookfor.IsEnabled = state;
    }

    private async void BtnGTAPath_OnClick(object? sender, RoutedEventArgs e)
    {
        var cfg = new ConfigParser(config);

        // Check if config value exists
        string? configGtaPath = cfg.GetValue("CONFIG", "GTA5Path");

        if (!string.IsNullOrEmpty(configGtaPath) && IsGtaPathValid(configGtaPath))
        {
            _vPath = configGtaPath;
        }
        else
        {
            var selectGtaPath = await GetTopLevel(this)!.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions()
            {
                Title = "Select your GTA V Path",
                AllowMultiple = false,
            });

            if (selectGtaPath.Count == 0) return;
            _vPath = selectGtaPath[0].Path.LocalPath;
            cfg.SetValue("CONFIG", "GTA5Path", _vPath);
            cfg.Save();
        }

        if (IsGtaPathValid(_vPath))
        {
            var loadMods = false;
            var questionBox = MessageBoxManager.GetMessageBoxStandard("Information", "Do you want to enable mods?",
                ButtonEnum.YesNo,
                MsBox.Avalonia.Enums.Icon.Info);
            var result = await questionBox.ShowAsync();
            GTA5Keys.LoadFromPath(_vPath);
            labelCache.Content = "Loading...";
            if (result == ButtonResult.Yes)
            {
                loadMods = true;
            }
            _gameFileCache = new GameFileCache(int.MaxValue, 10, _vPath, "mp2024_01_g9ec", loadMods, "Installers;_CommonRedist")
            {
                LoadAudio = false,
                LoadVehicles = false,
                LoadPeds = false
            };
            await Task.Run(() => _gameFileCache.Init(UpdateStatusCache, UpdateErrorLog));
            if (!_gameFileCache.IsInited) return;
            ToggleControls(true);
            labelCache.Content = "Game Cache Loaded";
            BtnGTAPath.IsEnabled = false;
        }
        else
        {
            var box = MessageBoxManager.GetMessageBoxStandard("Error", "Invalid GTA5 directory", ButtonEnum.Ok,
                MsBox.Avalonia.Enums.Icon.Error);
            SystemSoundPlayer.PlaySystemSound(SystemSoundType.Hand);
            await box.ShowAsync();
        }
    }

    private static bool IsGtaPathValid(string? path)
    {
        return File.Exists(path + "\\GTA5.exe");
    }

    private void UpdateStatusCache(string text)
    {
        Dispatcher.UIThread.InvokeAsync(() => { labelCache.Content = text; });
    }
    
    private void UpdateErrorLog(string text)
    {
        Console.WriteLine(text);
    }

    private static async Task WriteTexturesAsync(IEnumerable<Texture> textures, string outFolder, CancellationToken ct = default)
    {
        Directory.CreateDirectory(outFolder);

        await Parallel.ForEachAsync(textures, ct, async (tex, token) =>
        {
            try
            {
                var fpath = Path.Combine(outFolder, $"{tex.Name}.dds");
                var dds = DDSIO.GetDDSFile(tex);
                await File.WriteAllBytesAsync(fpath, dds, token);
            }
            catch
            { }
        });
    }
    private async void BtnLookEnts_OnClick(object? sender, RoutedEventArgs e)
    {
        ToggleControls(false);
        if (_globalExtractTask.MapFiles.Count > 0)
        {
            var msBoxExtractPath = MessageBoxManager.GetMessageBoxStandard($"Information",
                $"Select the folder where you want to save the files", ButtonEnum.Ok,
                MsBox.Avalonia.Enums.Icon.Info, WindowStartupLocation.CenterScreen);
            var result = await msBoxExtractPath.ShowAsync();

            var selectFolder = await GetTopLevel(this)!.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions { Title = "Select the folder where you want to save the files", AllowMultiple = false });

            if (selectFolder is null || selectFolder.Count == 0) { ToggleControls(true); return; }

            string? outputPath = selectFolder[0].Path.LocalPath;
            if (string.IsNullOrWhiteSpace(outputPath)) { ToggleControls(true); return; }
            if (string.IsNullOrEmpty(outputPath)) return;
            foreach (var mapFile in _globalExtractTask.MapFiles)
            {
                var ymapFolderPath = $"{outputPath}\\{Path.GetFileNameWithoutExtension(mapFile.FileName)}";
                Directory.CreateDirectory(ymapFolderPath);

                foreach (var entity in mapFile.EntsHashes)
                {
                    ModelType mt = new();
                    if (_gameFileCache.GetYdr(entity) != null)
                    {
                        var ydr = _gameFileCache.GetYdr(entity);
                        ydr.Load(ydr.RpfFileEntry.File.ExtractFile(ydr.RpfFileEntry), ydr.RpfFileEntry);
                        mt.YdrFiles.Add(ydr);
                    }

                    if (_gameFileCache.GetYdd(GetYddFromHash(entity)) != null)
                    {
                        var ydd = _gameFileCache.GetYdd(GetYddFromHash(entity));
                        ydd.Load(ydd.RpfFileEntry.File.ExtractFile(ydd.RpfFileEntry), ydd.RpfFileEntry);
                        mt.YddFiles.Add(ydd);
                    }

                    if (_gameFileCache.GetYft(entity) != null)
                    {
                        var yft = _gameFileCache.GetYft(entity);
                        yft.Load(yft.RpfFileEntry.File.ExtractFile(yft.RpfFileEntry), yft.RpfFileEntry);
                        mt.YftFiles.Add(yft);
                    }

                    if (mt.YdrFiles.Count > 0)
                    {
                        foreach (var mYdr in mt.YdrFiles)
                        {
                            if (!(bool)cbExtractXml.IsChecked!)
                            {
                                await File.WriteAllBytesAsync($"{ymapFolderPath}\\{mYdr.Name}", mYdr.Save());
                            }
                            else
                            {
                                var ydrXml = MetaXml.GetXml(mYdr, filename: out var ydrName,
                                    $"{ymapFolderPath}\\{mYdr.Name.Split(".")[0]}");
                                await File.WriteAllTextAsync($"{ymapFolderPath}\\{mYdr.Name}.xml", ydrXml);
                            }

                            if (!(bool)cbExtractTextures.IsChecked!) continue;
                            var textures = new HashSet<Texture>();
                            var textureMissing = new HashSet<string>();
                            var extract = Directory.CreateDirectory($"{ymapFolderPath}\\alltextures\\");
                            if (mYdr.Drawable != null)
                            {
                                await Task.Run(() => CollectTextures(mYdr.Drawable, textures, textureMissing));
                            }

                            await WriteTexturesAsync(textures, extract.FullName);
                        }
                    }

                    if (mt.YddFiles.Count > 0)
                    {
                        foreach (var mYdd in mt.YddFiles)
                        {
                            if (!(bool)cbExtractXml.IsChecked!)
                            {
                                await File.WriteAllBytesAsync($"{ymapFolderPath}\\{mYdd.Name}", mYdd.Save());
                            }
                            else
                            {
                                var yddXml = MetaXml.GetXml(mYdd, filename: out var yddName,
                                    $"{ymapFolderPath}\\{mYdd.Name.Split(".")[0]}");
                                await File.WriteAllTextAsync($"{ymapFolderPath}\\{mYdd.Name}.xml", yddXml);
                            }

                            if (!(bool)cbExtractTextures.IsChecked!) continue;
                            var textures = new HashSet<Texture>();
                            var textureMissing = new HashSet<string>();
                            var extract = Directory.CreateDirectory($"{ymapFolderPath}\\alltextures\\");
                            if (mYdd.DrawableDict != null)
                            {
                                foreach (var dd in mYdd.Drawables)
                                {
                                    await Task.Run(() => CollectTextures(dd, textures, textureMissing));
                                }
                            }

                            await WriteTexturesAsync(textures, extract.FullName);
                        }
                    }

                    if (mt.YftFiles.Count > 0)
                    {
                        foreach (var mYft in mt.YftFiles)
                        {
                            if (!(bool)cbExtractXml.IsChecked!)
                            {
                                await File.WriteAllBytesAsync($"{ymapFolderPath}\\{mYft.Name}", mYft.Save());
                            }
                            else
                            {
                                var yftXml = MetaXml.GetXml(mYft, filename: out var yftName,
                                    $"{ymapFolderPath}\\{mYft.Name.Split(".")[0]}");
                                await File.WriteAllTextAsync($"{ymapFolderPath}\\{mYft.Name}.xml", yftXml);
                            }

                            if (!(bool)cbExtractTextures.IsChecked!) continue;
                            var textures = new HashSet<Texture>();
                            var textureMissing = new HashSet<string>();
                            var extract = Directory.CreateDirectory($"{ymapFolderPath}\\alltextures\\");
                            await Task.Run(() => CollectTextures(mYft.Fragment.Drawable, textures, textureMissing));

                            await WriteTexturesAsync(textures, extract.FullName);
                        }
                    }
                }
            }

            var msBoxExtract = MessageBoxManager.GetMessageBoxStandard($"Information", $"Extraction Completed",
                ButtonEnum.Ok,
                MsBox.Avalonia.Enums.Icon.Info, WindowStartupLocation.CenterScreen);
            await msBoxExtract.ShowAsync();
        }
        else
        {
            var noEntsMsg = MessageBoxManager.GetMessageBoxStandard($"Information", $"No Entities Detected",
                ButtonEnum.Ok,
                MsBox.Avalonia.Enums.Icon.Info, WindowStartupLocation.CenterScreen);
            await noEntsMsg.ShowAsync();
        }

        ToggleControls(true);
        labelCache.Content = "Ready";
    }

    private void MiExit_OnClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private static uint ToUInt(MetaHash h) => unchecked((uint)h);
    private void CollectTextures(DrawableBase d, ISet<Texture> textureSet, ISet<string> textureMissing)
    {
        var sg = d?.ShaderGroup;
        if (sg == null) return;

        var dictTextures = sg.TextureDictionary?.Textures?.data_items;
        if (dictTextures != null)
        {
            foreach (var tex in dictTextures)
            {
                if (tex != null) textureSet.Add(tex);
            }
        }

        var shaders = sg.Shaders?.data_items;
        if (shaders == null) return;

        uint archhash = 0u;
        switch (d)
        {
            case Drawable dwbl:
                {
                    var name = dwbl.Name ?? string.Empty;
                    int dot = name.IndexOf('.');
                    string raw = dot >= 0 ? name.Substring(0, dot) : name;
                    string lowered = raw.ToLowerInvariant();
                    archhash = JenkHash.GenHash(lowered);
                    break;
                }
            case FragDrawable fdbl:
                {
                    var yft = fdbl.Owner as YftFile;
                    MetaHash fraghash = yft?.RpfFileEntry?.ShortNameHash ?? 0;
                    archhash = fraghash;
                    break;
                }
        }

        Archetype arch = _gameFileCache.GetArchetype(archhash);
        if (arch == null) return;

        uint txdHash = arch.TextureDict != null ? ToUInt(arch.TextureDict.Hash) : archhash;

        var foundCache = new Dictionary<ulong, Texture>(64);
        var parentCache = new Dictionary<uint, uint>(8);

        uint GetParentTxd(uint h)
        {
            if (h == 0) return 0;
            if (parentCache.TryGetValue(h, out var p)) return p;
            p = _gameFileCache.TryGetParentYtdHash(h);
            parentCache[h] = p;
            return p;
        }

        Texture TryResolve(uint texHash, uint startTxd)
        {
            if (texHash == 0) return null;

            ulong makeKey(uint th, uint txd) => ((ulong)txd << 32) | th;

            for (uint cur = startTxd; cur != 0; cur = GetParentTxd(cur))
            {
                var key = makeKey(texHash, cur);
                if (foundCache.TryGetValue(key, out var cached)) return cached;

                var tex = TryGetTexture(texHash, cur);
                if (tex != null)
                {
                    foundCache[key] = tex;
                    return tex;
                }
                foundCache[key] = null;
            }

            {
                var key = makeKey(texHash, 0);
                if (foundCache.TryGetValue(key, out var cached)) return cached;

                var ytd = _gameFileCache.TryGetTextureDictForTexture(texHash);
                var tex = TryGetTextureFromYtd(texHash, ytd);
                foundCache[key] = tex; // may be null
                return tex;
            }
        }

        foreach (var s in shaders)
        {
            var plist = s?.ParametersList?.Parameters;
            if (plist == null) continue;

            foreach (var p in plist)
            {
                if (p?.Data is null) continue;

                if (p.Data is Texture concrete)
                {
                    textureSet.Add(concrete);
                    continue;
                }

                if (p.Data is TextureBase tb)
                {
                    var resolved = TryResolve(tb.NameHash, txdHash);
                    if (resolved != null)
                    {
                        textureSet.Add(resolved);
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(tb.Name))
                            textureMissing.Add(tb.Name);
                    }
                }
            }
        }
    }

    private Texture? TryGetTexture(uint texHash, uint txdHash)
    {
        if (txdHash == 0 || texHash == 0) return null;
        var ytd = _gameFileCache.GetYtd(txdHash);
        return TryGetTextureFromYtd(texHash, ytd);
    }

    private static Texture? TryGetTextureFromYtd(uint texHash, YtdFile? ytd)
    {
        if (ytd == null || texHash == 0) return null;
        if (ytd.TextureDict == null)
        {
            var entry = ytd.RpfFileEntry;
            if (entry?.File != null)
            {
                var data = entry.File.ExtractFile(entry);
                ytd.Load(data, entry);
            }
            else
            {
                ytd.Load(null, entry);
            }
        }

        return ytd.TextureDict?.Lookup(texHash);
    }

    private async void BtnLookfor_OnClick(object? sender, RoutedEventArgs e)
    {
        switch (CBoxExtractType.SelectedIndex)
        {
            case 0: // YMAPs

                var ymapResult = await GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(
                    new FilePickerOpenOptions()
                    {
                        Title = "Select YMAP(s) folder",
                        AllowMultiple = true,
                        FileTypeFilter = new[] { new FilePickerFileType("YMAP(s)") { Patterns = new[] { "*.ymap" }}}
                    });
                if (ymapResult.Count <= 0) return;
                var ymapMsgInfo = MessageBoxManager.GetMessageBoxStandard($"Information",
                    $"Detected {ymapResult.ToList().Count} YMAP(s)", ButtonEnum.Ok,
                    MsBox.Avalonia.Enums.Icon.Info, WindowStartupLocation.CenterScreen);
                if (ymapResult.Any(x => x.Name.Contains(".ymap")))
                {
                    await ymapMsgInfo.ShowAsync();
                    BtnLookEnts.IsEnabled = true;
                    foreach (var ymap in ymapResult)
                    {
                        _globalExtractTask.MapFiles.Add(new MapTask(ymap.Path.LocalPath,
                            GetEntityHashesFromFile(ymap.Path.LocalPath, 0)));
                    }
                }

                break;
            case 1:
            case 3:
                var ytypResult = await GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(
                    new FilePickerOpenOptions()
                    {
                        Title = "Select YTYP(s) folder",
                        AllowMultiple = true,
                        FileTypeFilter = new[] { new FilePickerFileType("YTYP(s)") { Patterns = new[] { "*.ytyp" }}}
                    });

                var ytypMsgInfo = MessageBoxManager.GetMessageBoxStandard($"Information",
                    $"Detected {ytypResult.ToList().Count} YTYP(s)", ButtonEnum.Ok,
                    MsBox.Avalonia.Enums.Icon.Info, WindowStartupLocation.CenterScreen);
                if (ytypResult.Count <= 0) return;
                if (ytypResult.Any(x => x.Name.Contains(".ytyp")))
                {
                    await ytypMsgInfo.ShowAsync();
                    BtnLookEnts.IsEnabled = true;
                    foreach (var ytyp in ytypResult)
                    {
                        try
                        {
                            var hashes = GetEntityHashesFromFile(ytyp.Path.LocalPath, 1, CBoxExtractType.SelectedIndex == 3);
                            _globalExtractTask.MapFiles.Add(new MapTask(ytyp.Path.LocalPath, hashes));
                        }
                        catch (InvalidDataException ex)
                        {
                            Debug.WriteLine($"InvalidDataException for file {ytyp.Path.LocalPath}: {ex.Message}");
                        }
                    }
                }

                break;
            case 2:
                var textFileResult = await GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(
                    new FilePickerOpenOptions()
                    {
                        Title = "Select Text File folder",
                        AllowMultiple = false,
                        FileTypeFilter = new[] { new FilePickerFileType("Text File") { Patterns = new[] { "*.txt" }}}
                    });

                var textFileMsgInfo = MessageBoxManager.GetMessageBoxStandard($"Information", $"Valid Text File",
                    ButtonEnum.Ok,
                    MsBox.Avalonia.Enums.Icon.Info, WindowStartupLocation.CenterScreen);
                if (textFileResult.Count <= 0) return;
                if (textFileResult.Any(x => x.Name.Contains(".txt")))
                {
                    await textFileMsgInfo.ShowAsync();
                    BtnLookEnts.IsEnabled = true;
                    _globalExtractTask.MapFiles.Add(new MapTask(textFileResult[0].Path.LocalPath,
                        GetEntityHashesFromFile(File.ReadAllLines(textFileResult[0].Path.LocalPath))));
                }

                break;
        }
    }

    private static List<uint> GetEntityHashesFromFile(string file, int type, bool includeMloEntities = false)
    {
        List<uint> hashes = [];
        switch (type)
        {
            case 0:
                var ymapFile = new YmapFile();
                ymapFile.Load(File.ReadAllBytes(file));
                hashes.AddRange(ymapFile.AllEntities.Select(entity => entity._CEntityDef.archetypeName.Hash));
                return hashes.Distinct().ToList();
            case 1:
                var ytypFile = new YtypFile();
                ytypFile.Load(File.ReadAllBytes(file));
                hashes.AddRange(ytypFile.AllArchetypes.Select(archetype => archetype._BaseArchetypeDef.assetName.Hash));
                if (includeMloEntities)
                {
                    foreach (var archetype in ytypFile.AllArchetypes.Where(x => x.Type == MetaName.CMloArchetypeDef))
                    {
                        var mlo = (MloArchetype)archetype;
                        if (mlo?.entitySets != null && mlo.entitySets.Length != 0)
                        {
                            hashes.AddRange(
                                mlo.entitySets
                                    .SelectMany(entitySet => entitySet.Entities.Select(x => x.Data.archetypeName.Hash))
                            );
                        }
                        hashes.AddRange(mlo.entities.Select(x => x.Data.archetypeName.Hash));
                    }
                }

                return hashes.Distinct().ToList();
        }

        return hashes;
    }

    private static List<uint> GetEntityHashesFromFile(IEnumerable<string> textLines)
    {
        List<uint> hashes = textLines.Select(line => JenkHash.GenHash(line.ToLowerInvariant().Trim())).ToList();
        return hashes.Distinct().ToList();
    }

    private uint GetYddFromHash(uint hash)
    {
        var arch = _gameFileCache.GetArchetype(hash);
        return arch != null ? arch._BaseArchetypeDef.drawableDictionary.Hash : (uint)0;
    }
}