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
}
