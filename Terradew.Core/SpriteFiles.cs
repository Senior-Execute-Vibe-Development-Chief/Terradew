using System.IO;

namespace Terradew.Core;

public static class SpriteFiles
{
    public static bool Exists(string fileName)
        => File.Exists(Path.Combine(SpriteNames.Folder, fileName));

    public static void Publish(string fileName, int width, int height, byte[] rgba)
        => Publish(SpriteNames.Folder, fileName, width, height, rgba);

    public static void Publish(string folder, string fileName, int width, int height, byte[] rgba)
    {
        Directory.CreateDirectory(folder);
        string finalPath = Path.Combine(folder, fileName);
        if (File.Exists(finalPath))
            return;
        string partial = finalPath + ".partial";
        Png.Write(partial, width, height, rgba);
        try
        {
            File.Move(partial, finalPath, overwrite: false);
        }
        catch (IOException) when (File.Exists(finalPath))
        {
            TryDelete(partial);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
