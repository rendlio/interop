using System.Diagnostics;

namespace Rendlio.Interop.Conventions.Tests;

/// <summary>
/// Locates the repository from a test run and enumerates what it publishes, so every
/// convention fixture agrees on one definition of "shipped".
/// </summary>
internal static class RepositoryLayout
{
    private const string SolutionFileName = "Rendlio.Interop.slnx";

    /// <summary>
    /// Directory names pruned whatever git says about them. Nothing listed here is normally
    /// tracked — build output and the private working trees are all ignored — so this is a
    /// second answer rather than the first one. It covers the case an ignore rule cannot: a
    /// path added with <c>-f</c> is tracked, and a private note that reached the index would
    /// otherwise be read under rules written for the pages strangers see.
    /// </summary>
    private static readonly string[] PrunedDirectoryNames =
    [
        ".git", ".vs", ".idea", "bin", "obj", "artifacts", "node_modules", "TestResults",
        ".conductor",
    ];

    /// <summary>Repository-relative directories that are private rather than published.</summary>
    private static readonly string[] PrunedRelativePaths = ["docs/internal"];

    /// <summary>
    /// <c>.txt</c> is here for the two <c>PublicAPI</c> files each package keeps beside its
    /// project. They are a written-down copy of the public surface, which makes them the one
    /// document in the tree guaranteed to name every public type by its full name — including
    /// one belonging to a package nobody has announced. The rest of the list is source and
    /// configuration.
    /// </summary>
    private static readonly string[] ShippedExtensions =
    [
        ".md", ".cs", ".csproj", ".props", ".targets", ".config", ".json",
        ".yml", ".yaml", ".slnx", ".txt",
    ];

    /// <summary>Shipped files whose whole name is the extension, plus the licence.</summary>
    private static readonly string[] ShippedFileNames =
    [
        ".editorconfig", ".gitattributes", ".gitignore", "LICENSE",
    ];

    /// <summary>
    /// Committed, but written by a tool rather than by a person. A restore lock is derived
    /// from Directory.Packages.props the way build output is derived from the sources, and it
    /// is mostly base64 content hashes — random text long enough that a scan for a short
    /// forbidden word would eventually hit one by chance, on a regeneration nobody could
    /// review. The lock is checked by <c>BuildContractTests</c>, which asks the question that
    /// actually applies to it: that one exists for every project in the solution.
    /// </summary>
    private static readonly string[] GeneratedFileNames = ["packages.lock.json"];

    private static readonly Lazy<DirectoryInfo> LazyRoot = new(FindRoot);

    /// <summary>
    /// Read once. Every fixture here asks the same question of the same commit, and the answer
    /// costs a process.
    /// </summary>
    private static readonly Lazy<IReadOnlyList<string>> LazyShippedFiles = new(ReadShippedFiles);

    public static DirectoryInfo Root => LazyRoot.Value;

    public static string ReadFile(string relativePath)
    {
        string path = Path.Combine(Root.FullName, relativePath);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Expected '{relativePath}' at the repository root, but '{path}' does not exist.", path);
        }

        return File.ReadAllText(path);
    }

    /// <summary>
    /// Every file this repository publishes that a person wrote, as absolute paths. The set
    /// comes from git rather than from a walk of the working tree, because what a repository
    /// publishes is what it tracks and a walk answers a neighbouring question that is not the
    /// same one: it counts an untracked scratch file as published, so a stray note beside the
    /// solution reddens these fixtures on one machine while CI, which only ever has the
    /// checkout, stays green. Generated files are left out — see
    /// <see cref="GeneratedFileNames"/> — as are the private trees.
    /// </summary>
    public static IReadOnlyList<string> EnumerateShippedFiles() => LazyShippedFiles.Value;

    /// <summary>The repository-relative path, for readable assertion messages.</summary>
    public static string Describe(string absolutePath) =>
        Path.GetRelativePath(Root.FullName, absolutePath).Replace(Path.DirectorySeparatorChar, '/');

    private static List<string> ReadShippedFiles()
    {
        List<string> files = [];

        foreach (string relativePath in TrackedPaths())
        {
            if (IsPruned(relativePath) || !IsShipped(Path.GetFileName(relativePath)))
            {
                continue;
            }

            string absolutePath = Path.Combine(Root.FullName, relativePath);

            // The index can name a file the working tree does not currently hold — a
            // conflicted merge, a sparse checkout. Every caller reads what it is handed.
            if (File.Exists(absolutePath))
            {
                files.Add(absolutePath);
            }
        }

        return files;
    }

    /// <summary>
    /// The paths git tracks, repository-relative and forward-slashed as git writes them.
    /// Separated by NUL so that a name git would otherwise quote and escape arrives whole.
    /// </summary>
    private static string[] TrackedPaths()
    {
        ProcessStartInfo start = new("git")
        {
            WorkingDirectory = Root.FullName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        start.ArgumentList.Add("ls-files");
        start.ArgumentList.Add("-z");

        using Process git = Process.Start(start)
            ?? throw new InvalidOperationException(
                "Could not start 'git'. What this repository publishes is what it tracks, so "
                + "these fixtures have to run from a checkout with git available.");

        // Drain before waiting: the listing is long enough to fill a pipe, and a process
        // blocked on a pipe nobody is reading would hang rather than fail.
        Task<string> listing = git.StandardOutput.ReadToEndAsync();
        Task<string> failure = git.StandardError.ReadToEndAsync();
        git.WaitForExit();

        if (git.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"'git ls-files' exited with {git.ExitCode} in '{Root.FullName}'. "
                + failure.Result.Trim());
        }

        return listing.Result.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    private static bool IsPruned(string relativePath) =>
        PrunedRelativePaths.Any(pruned =>
            relativePath.StartsWith($"{pruned}/", StringComparison.OrdinalIgnoreCase))
        || relativePath.Split('/')
            .Any(segment => PrunedDirectoryNames.Contains(segment, StringComparer.OrdinalIgnoreCase));

    private static bool IsShipped(string fileName) =>
        !GeneratedFileNames.Contains(fileName, StringComparer.OrdinalIgnoreCase)
        && (ShippedFileNames.Contains(fileName, StringComparer.OrdinalIgnoreCase)
            || ShippedExtensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase));

    private static DirectoryInfo FindRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory;
            }
        }

        throw new InvalidOperationException(
            $"Could not locate '{SolutionFileName}' in any directory above '{AppContext.BaseDirectory}'.");
    }
}
