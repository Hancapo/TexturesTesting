using System.Collections.Generic;
using System.IO;

namespace TexturesTesting;

public class MapTask
{
    public string FilePath { get; set; }
    public string? FileName { get; }
    public List<uint> EntsHashes { get; set; }

    public MapTask(string filePath, List<uint> hashes)
    {
        FilePath = filePath;
        FileName = Path.GetFileNameWithoutExtension(FilePath);
        EntsHashes = hashes;
    }
}