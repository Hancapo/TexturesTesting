using System.Collections.Generic;
using CodeWalker.GameFiles;

namespace TexturesTesting;

public class ModelType
{
    public List<YdrFile> YdrFiles { get; set; } = new List<YdrFile>();
    public List<YddFile> YddFiles { get; set; } = new List<YddFile>();
    public List<YftFile> YftFiles { get; set; } = new List<YftFile>();
}