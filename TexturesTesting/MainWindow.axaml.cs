using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
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
    private static string _GTApath;
    private GameFileCache _gameFileCache;

    public MainWindow()
    {
        InitializeComponent();
        ToggleControls(false);
    }

    private void ToggleControls(bool state)
    {
        BtnLookEnts.IsEnabled = state;
        cbExtractTextures.IsEnabled = state;
        cbExtractXml.IsEnabled = state;
        GameSettingsMI.IsEnabled = state;
        CBoxExtractType.IsEnabled = state;
        BtnLookfor.IsEnabled = state;
    }

    private async void BtnGTAPath_OnClick(object? sender, RoutedEventArgs e)
    {
        var selectGtaPath = await GetTopLevel(this).StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions()
        {
            Title = "Select your GTA V Path",
            AllowMultiple = false,
        });

        _GTApath = selectGtaPath[0].Path.LocalPath;

        if (IsGtaPathValid(_GTApath))
        {
            GTA5Keys.LoadFromPath(_GTApath);
            _gameFileCache = new GameFileCache(2147483648, 10, _GTApath, null, false, "Installers;_CommonRedist");
            labelCache.Foreground = Brushes.GreenYellow;
            labelCache.FontFamily = FontFamily.Parse("Consolas");
            await Task.Run(() => _gameFileCache.Init(UpdateStatusCache, UpdateStatusCache));
            if (_gameFileCache.IsInited)
            {
                ToggleControls(true);
                
            }
        }
        else
        {
            var box = MessageBoxManager.GetMessageBoxStandard("Error", "Invalid GTA5 directory", ButtonEnum.Ok,
                MsBox.Avalonia.Enums.Icon.Error, WindowStartupLocation.CenterScreen);
            SystemSoundPlayer.PlaySystemSound(SystemSoundType.Hand);
            var result2 = await box.ShowAsync();
        }
    }

    private static bool IsGtaPathValid(string path)
    {
        return File.Exists(path + "\\GTA5.exe");
    }

    private void UpdateStatusCache(string text)
    {
        Dispatcher.UIThread.InvokeAsync(() => { labelCache.Content = text; });
    }

    private async void BtnLookEnts_OnClick(object? sender, RoutedEventArgs e)
    {
            //WIP
    }

    private void MiExit_OnClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void MIApplySettings_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!_gameFileCache.IsInited) return;
        _gameFileCache.SetModsEnabled((bool)cbEnableMods.IsChecked!);
    }

    private void ApplySettings()
    {
        _gameFileCache.Init(UpdateStatusCache, UpdateStatusCache);
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
                    tex = TryGetTexture(texhash, txdHash);
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
                
                var ymapResult = await GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions()
                {
                    Title = "Select YMAP(s) folder",
                    AllowMultiple = true,
                    FileTypeFilter = new[] { new FilePickerFileType("YMAP(s)"){Patterns = new[] {"*.ymap"}} }
                });
                
                var ymapMsgInfo =MessageBoxManager.GetMessageBoxStandard($"Information", $"Detected {ymapResult.ToList().Count} YMAP(s)", ButtonEnum.Ok,
                    MsBox.Avalonia.Enums.Icon.Info, WindowStartupLocation.CenterScreen);
                await ymapMsgInfo.ShowAsync();
                break;
            case 1:
                var ytypResult = await GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions()
                {
                    Title = "Select YTYP(s) folder",
                    AllowMultiple = true,
                    FileTypeFilter = new[] { new FilePickerFileType("YTYP(s)"){Patterns = new[] {"*.ytyp"}} }
                });
                
                var ytypMsgInfo =MessageBoxManager.GetMessageBoxStandard($"Information", $"Detected {ytypResult.ToList().Count} YTYP(s)", ButtonEnum.Ok,
                    MsBox.Avalonia.Enums.Icon.Info, WindowStartupLocation.CenterScreen);
                await ytypMsgInfo.ShowAsync();
                
                break;
            case 2:
                var textFileResult = await GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions()
                {
                    Title = "Select Text File folder",
                    AllowMultiple = false,
                    FileTypeFilter = new[] { new FilePickerFileType("Text File"){Patterns = new[] {"*.txt"}} }
                });
                
                var textFileMsgInfo =MessageBoxManager.GetMessageBoxStandard($"Information", $"Valid Text File", ButtonEnum.Ok,
                    MsBox.Avalonia.Enums.Icon.Info, WindowStartupLocation.CenterScreen);
                await textFileMsgInfo.ShowAsync();
                break;
        }
    }

    private List<uint> GetEntityHashesFromFile(List<string> files, int type)
    {
        List<uint> hashes = new();
        switch (type)
        {
            case 0:
                List<YmapFile> ymapFiles = new();
                foreach (var ymap in files)
                {
                    var ymapFile = new YmapFile();
                    ymapFile.Load(File.ReadAllBytes(ymap));
                    ymapFiles.Add(ymapFile);
                }
        
                foreach (var ymap in ymapFiles)
                {
                    hashes.AddRange(ymap.AllEntities.Select(entity => entity._CEntityDef.archetypeName.Hash));
                }
                return hashes.Distinct().ToList();
            case 1:
                List<YtypFile> ytypFiles = new();
                foreach (var ytyp in files)
                {
                    var ytypFile = new YtypFile();
                    ytypFile.Load(File.ReadAllBytes(ytyp));
                    ytypFiles.Add(ytypFile);
                }
                List<uint> ytypHashes = new();
                foreach (var ytyp in ytypFiles)
                {
                    ytypHashes.AddRange(ytyp.AllArchetypes.Select(arch => arch._BaseArchetypeDef.name.Hash));
                }
                return ytypHashes.Distinct().ToList();
                
        }
        
        return hashes;
        
    }

    private List<uint> GetEntityHashesFromFile(IEnumerable<string> textLines)
    {
        List<uint> hashes = textLines.Select(line => JenkHash.GenHash(line.ToLowerInvariant().Trim())).ToList();
        return hashes.Distinct().ToList();
    }
}