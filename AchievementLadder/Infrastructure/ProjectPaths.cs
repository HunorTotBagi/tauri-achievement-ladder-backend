namespace AchievementLadder.Infrastructure;

public static class ProjectPaths
{
    public static string FindProjectRoot(string startDirectory, string projectFileName)
    {
        var directory = new DirectoryInfo(startDirectory);

        while (directory is not null)
        {
            var projectFilePath = Path.Combine(directory.FullName, projectFileName);
            if (File.Exists(projectFilePath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Could not locate {projectFileName} from {startDirectory}.");
    }

    public static string FindSolutionRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);

        while (directory is not null)
        {
            if (directory.GetFiles("*.sln").Length > 0)
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return startDirectory;
    }

    public static string GetFrontendSrcDirectory(string solutionRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionRoot);

        return Path.GetFullPath(
            Path.Combine(solutionRoot, "..", "tauriachievements.github.io", "src"));
    }

    public static string ResolveCharacterBatchFilePath(
        string solutionRoot,
        string projectRoot,
        string inputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);

        var trimmedInputPath = inputPath.Trim();

        if (Path.IsPathRooted(trimmedInputPath))
        {
            var absolutePath = Path.GetFullPath(trimmedInputPath);
            if (File.Exists(absolutePath))
            {
                return absolutePath;
            }

            throw new FileNotFoundException($"Could not find names file: {absolutePath}", absolutePath);
        }

        var candidates = new List<string>
        {
            Path.GetFullPath(trimmedInputPath, projectRoot),
            Path.GetFullPath(trimmedInputPath, solutionRoot)
        };

        if (!trimmedInputPath.Contains(Path.DirectorySeparatorChar) &&
            !trimmedInputPath.Contains(Path.AltDirectorySeparatorChar))
        {
            foreach (var fileName in ExpandBatchFileNames(trimmedInputPath))
            {
                candidates.Add(Path.Combine(solutionRoot, "AchievementLadder", "Data", "PvPSeasonCharacters", fileName));
                candidates.Add(Path.Combine(solutionRoot, "RareAchiAndItemScan", "Input", fileName));
            }
        }

        var distinctCandidates = candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var candidate in distinctCandidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            $"Could not find names file '{trimmedInputPath}'. Checked: {string.Join(", ", distinctCandidates)}",
            distinctCandidates.FirstOrDefault() ?? trimmedInputPath);
    }

    private static IEnumerable<string> ExpandBatchFileNames(string inputPath)
    {
        yield return inputPath;

        if (string.IsNullOrWhiteSpace(Path.GetExtension(inputPath)))
        {
            yield return inputPath + ".txt";
        }
    }
}
