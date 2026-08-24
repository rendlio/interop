namespace Rendlio.Interop.Conventions.Tests;

/// <summary>
/// Locates the repository from a test run and enumerates what it publishes, so every
/// convention fixture agrees on one definition of "shipped".
/// </summary>
internal static class RepositoryLayout
{
    private const string SolutionFileName = "Rendlio.Interop.slnx";

    /// <summary>
    /// Directory names that never hold shipped content. Build output is excluded because it
    /// is a copy of the sources; the private working trees are excluded because the
    /// vocabulary the public rules forbid is legitimate inside them.
    /// </summary>
    private static readonly string[] PrunedDirectoryNames =
    [
        ".git", ".vs", ".idea", "bin", "obj", "artifacts", "node_modules", "TestResults",
        ".conductor",
    ];

    /// <summary>Repository-relative directories that are private rather than published.</summary>
    private static readonly string[] PrunedRelativePaths = ["docs/internal"];

    private static readonly string[] ShippedExtensions =
    [
        ".md", ".cs", ".csproj", ".props", ".targets", ".config", ".json",
        ".yml", ".yaml", ".slnx",
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
    /// Every file this repository publishes that a person wrote, as absolute paths. Generated
    /// files are left out — see <see cref="GeneratedFileNames"/>.
    /// </summary>
    public static IReadOnlyList<string> EnumerateShippedFiles()
    {
        List<string> files = [];
        Collect(Root, files);

        return files;
    }

    /// <summary>The repository-relative path, for readable assertion messages.</summary>
    public static string Describe(string absolutePath) =>
        Path.GetRelativePath(Root.FullName, absolutePath).Replace(Path.DirectorySeparatorChar, '/');

    private static void Collect(DirectoryInfo directory, List<string> files)
    {
        foreach (FileInfo file in directory.EnumerateFiles())
        {
            if (IsShipped(file.Name))
            {
                files.Add(file.FullName);
            }
        }

        foreach (DirectoryInfo child in directory.EnumerateDirectories())
        {
            if (IsPruned(child))
            {
                continue;
            }

            Collect(child, files);
        }
    }

    private static bool IsPruned(DirectoryInfo directory) =>
        PrunedDirectoryNames.Contains(directory.Name, StringComparer.OrdinalIgnoreCase)
        || PrunedRelativePaths.Contains(Describe(directory.FullName), StringComparer.OrdinalIgnoreCase);

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
