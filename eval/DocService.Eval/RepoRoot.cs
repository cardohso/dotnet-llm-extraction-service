namespace DocService.Eval;

internal static class RepoRoot
{
    /// <summary>Walks up from the build output (or the working directory) to the folder holding global.json and eval/.</summary>
    public static string Find()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "global.json")) && Directory.Exists(Path.Combine(dir.FullName, "eval")))
                {
                    return dir.FullName;
                }
            }
        }

        throw new DirectoryNotFoundException("Could not find the repository root (a folder with global.json and eval/).");
    }
}
