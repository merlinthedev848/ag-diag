using System;
using System.IO;
using System.Linq;

class Fixer
{
    static void Main()
    {
        string mwPath = @""MainWindow.xaml.cs"";
        string mw = File.ReadAllText(mwPath);
        
        string oldReset = @""                if (System.IO.Directory.Exists(targetDir))
                {
                    try
                    {
                        System.IO.Directory.Delete(targetDir, true);
                        cacheCleared = true;
                    }
                    catch (System.IO.IOException)
                    {
                        // Fallback: delete files individually, skip locked files
                        DeleteDirectoryContents(targetDir);
                        cacheCleared = true;
                    }
                }"";

        string newReset = @""                if (System.IO.Directory.Exists(targetDir))
                {
                    string[] cacheFolders = { """"Cache"""", """"GPUCache"""", @""""EBWebView\Default\Cache"""" };
                    foreach (var cf in cacheFolders)
                    {
                        string cDir = System.IO.Path.Combine(targetDir, cf);
                        if (System.IO.Directory.Exists(cDir))
                        {
                            try
                            {
                                System.IO.Directory.Delete(cDir, true);
                                cacheCleared = true;
                            }
                            catch (System.IO.IOException)
                            {
                                DeleteDirectoryContents(cDir);
                                cacheCleared = true;
                            }
                        }
                    }
                }"";
        mw = mw.Replace(oldReset, newReset);
        File.WriteAllText(mwPath, mw);
    }
}
