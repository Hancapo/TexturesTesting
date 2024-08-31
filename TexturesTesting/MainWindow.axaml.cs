using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CodeWalker.GameFiles;
using CodeWalker.Utils;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace TexturesTesting;

public partial class MainWindow : Window
{
    private static string? vPath;
    private GameFileCache _gameFileCache;
    private static ExtractTask _globalExtractTask = new();

    public MainWindow()
    {
        InitializeComponent();
        ToggleControls(false);
        BtnLookEnts.IsEnabled = false;
    }

    private void ToggleControls(bool state)
    {
        cbExtractTextures.IsEnabled = state;
        cbExtractXml.IsEnabled = state;
        CBoxExtractType.IsEnabled = state;
        BtnLookfor.IsEnabled = state;
    }

    private async void BtnGTAPath_OnClick(object? sender, RoutedEventArgs e)
    {
        var selectGtaPath = await GetTopLevel(this)!.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions()
        {
            Title = "Select your GTA V Path",
            AllowMultiple = false,
        });

        if (selectGtaPath.Count == 0) return;
        vPath = selectGtaPath[0].Path.LocalPath;

        if (IsGtaPathValid(vPath))
        {
            var loadMods = false;
            var questionBox = MessageBoxManager.GetMessageBoxStandard("Information", "Do you want to enable mods?",
                ButtonEnum.YesNo,
                MsBox.Avalonia.Enums.Icon.Info);
            var result = await questionBox.ShowAsync();
            GTA5Keys.LoadFromPath(vPath);
            labelCache.Content = "Loading...";
            if (result == ButtonResult.Yes)
            {
                loadMods = true;
            }
            _gameFileCache = new GameFileCache(int.MaxValue, 10, vPath, "mp2024_01_g9ec", loadMods,
                "Installers;_CommonRedist");
            labelCache.Foreground = Brushes.GreenYellow;
            labelCache.FontFamily = FontFamily.Parse("Consolas"); 
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
                new FolderPickerOpenOptions()
                {
                    Title = "Select the folder where you want to save the files",
                    AllowMultiple = false,
                });

            string? outputPath = selectFolder[0].Path.LocalPath;
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

                    if (_gameFileCache.GetYdd(GetYDDFromHash(entity)) != null)
                    {
                        var ydd = _gameFileCache.GetYdd(GetYDDFromHash(entity));
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

                            Parallel.ForEach(textures, async (tex) =>
                            {
                                try
                                {
                                    var fpath = $"{extract.FullName}\\{tex.Name}.dds";
                                    var dds = DDSIO.GetDDSFile(tex);
                                    await File.WriteAllBytesAsync(fpath, dds);
                                }
                                catch
                                {
                                    // ignored
                                }
                            });
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

                            Parallel.ForEach(textures, async (tex) =>
                            {
                                try
                                {
                                    var fpath = $"{extract.FullName}\\{tex.Name}.dds";
                                    var dds = DDSIO.GetDDSFile(tex);
                                    await File.WriteAllBytesAsync(fpath, dds);
                                }
                                catch
                                {
                                    // ignored
                                }
                            });
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

                            Parallel.ForEach(textures, async (tex) =>
                            {
                                try
                                {
                                    var fpath = $"{extract.FullName}\\{tex.Name}.dds";
                                    var dds = DDSIO.GetDDSFile(tex);
                                    await File.WriteAllBytesAsync(fpath, dds);
                                }
                                catch
                                {
                                    // ignored
                                }
                            });
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

    private void CollectTextures(DrawableBase d, HashSet<Texture> textureSet, HashSet<string> textureMissing)
    {
        if (d?.ShaderGroup?.TextureDictionary?.Textures?.data_items != null)
        {
            foreach (var tex in d.ShaderGroup.TextureDictionary.Textures.data_items)
            {
                textureSet.Add(tex);
            }
        }

        if (d?.ShaderGroup?.Shaders?.data_items == null) return;

        uint archhash = 0u;
        switch (d)
        {
            case Drawable dwbl:
            {
                string dname = dwbl.Name.ToLowerInvariant();
                dname = dname.Split(".")[0];
                archhash = JenkHash.GenHash(dname);
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
        uint txdHash = (arch != null) ? arch.TextureDict.Hash : archhash;
        if ((txdHash == 0) && (archhash == 0))
        {
        }

        foreach (ShaderFX s in d.ShaderGroup.Shaders.data_items)
        {
            if (s?.ParametersList?.Parameters == null) continue;
            foreach (ShaderParameter p in s.ParametersList.Parameters)
            {
                if (p.Data is not TextureBase t) continue;
                if (t is Texture tex)
                {
                    textureSet.Add(tex);
                }
                else
                {
                    uint texhash = t.NameHash;
                    tex = TryGetTexture(texhash, txdHash)!;
                    if (tex == null)
                    {
                        var ptxdhash = _gameFileCache.TryGetParentYtdHash(txdHash);
                        while ((ptxdhash != 0) && (tex == null))
                        {
                            tex = TryGetTexture(texhash, ptxdhash);
                            if (tex == null)
                            {
                                ptxdhash = _gameFileCache.TryGetParentYtdHash(ptxdhash);
                            }
                        }

                        if (tex == null)
                        {
                            var ytd = _gameFileCache.TryGetTextureDictForTexture(texhash);
                            tex = TryGetTextureFromYtd(texhash, ytd);
                        }

                        if (tex == null)
                        {
                            textureMissing.Add(t.Name);
                        }
                    }

                    if (tex != null)
                    {
                        textureSet.Add(tex);
                    }
                }
            }
        }
    }

    private Texture? TryGetTexture(uint texHash, uint txdHash)
    {
        if (txdHash == 0) return null;
        var ytd = _gameFileCache.GetYtd(txdHash);
        var tex = TryGetTextureFromYtd(texHash, ytd);
        return tex;
    }

    private static Texture? TryGetTextureFromYtd(uint texHash, YtdFile? ytd)
    {
        if (ytd == null) return null;
        ytd.Load(ytd.RpfFileEntry.File.ExtractFile(ytd.RpfFileEntry), ytd.RpfFileEntry);
        return ytd.TextureDict?.Lookup(texHash);
    }

    private async void BtnLookfor_OnClick(object? sender, RoutedEventArgs e)
    {
        switch (CBoxExtractType.SelectedIndex)
        {
            case 0:

                var ymapResult = await GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(
                    new FilePickerOpenOptions()
                    {
                        Title = "Select YMAP(s) folder",
                        AllowMultiple = true,
                        FileTypeFilter = new[] { new FilePickerFileType("YMAP(s)") { Patterns = new[] { "*.ymap" } } }
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
                        FileTypeFilter = new[] { new FilePickerFileType("YTYP(s)") { Patterns = new[] { "*.ytyp" } } }
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
                        _globalExtractTask.MapFiles.Add(new MapTask(ytyp.Path.LocalPath,
                            GetEntityHashesFromFile(ytyp.Path.LocalPath, 1, CBoxExtractType.SelectedIndex == 3)));
                    }
                }

                break;
            case 2:
                var textFileResult = await GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(
                    new FilePickerOpenOptions()
                    {
                        Title = "Select Text File folder",
                        AllowMultiple = false,
                        FileTypeFilter = new[] { new FilePickerFileType("Text File") { Patterns = new[] { "*.txt" } } }
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

    private List<uint> GetEntityHashesFromFile(string file, int type, bool includeMloEntities = false)
    {
        List<uint> hashes = new();
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
                        if (mlo!.entitySets.Length != 0)
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

    private List<uint> GetEntityHashesFromFile(IEnumerable<string> textLines)
    {
        List<uint> hashes = textLines.Select(line => JenkHash.GenHash(line.ToLowerInvariant().Trim())).ToList();
        return hashes.Distinct().ToList();
    }

    private void ExtractAssetsXML(List<uint> hashes, string path)
    {
    }

    private uint GetYDDFromHash(uint hash)
    {
        var arch = _gameFileCache.GetArchetype(hash);
        return arch != null ? arch._BaseArchetypeDef.drawableDictionary.Hash : (uint)0;
    }
}