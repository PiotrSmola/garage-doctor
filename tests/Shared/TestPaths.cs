namespace GarageDoctor.Tests;

public static class TestPaths
{
    public static string RepositoryRoot { get; } = LocateRepositoryRoot();

    public static string Fixture(string fileName) => Path.Combine(RepositoryRoot, "tests", "fixtures", fileName);

    public static string DataFile(string fileName) => Path.Combine(RepositoryRoot, "data", fileName);

    private static string LocateRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GarageDoctor.sln")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new DirectoryNotFoundException("GarageDoctor.sln not found above the test output directory.");
    }
}
