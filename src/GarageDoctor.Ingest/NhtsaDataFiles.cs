using System.IO.Compression;
using Microsoft.Extensions.Logging;

namespace GarageDoctor.Ingest;

public sealed class NhtsaDataFiles
{
    private static readonly (string Url, string Archive, string Extracted)[] Sources =
    [
        ("https://static.nhtsa.gov/odi/ffdd/cmpl/FLAT_CMPL.zip", "FLAT_CMPL.zip", "FLAT_CMPL.txt"),
        ("https://static.nhtsa.gov/odi/ffdd/rcl/FLAT_RCL_POST_2010.zip", "FLAT_RCL_POST_2010.zip", "FLAT_RCL_POST_2010.txt"),
        ("https://static.nhtsa.gov/odi/ffdd/rcl/FLAT_RCL_PRE_2010.zip", "FLAT_RCL_PRE_2010.zip", "FLAT_RCL_PRE_2010.txt")
    ];

    private readonly string _rawDirectory;
    private readonly ILogger<NhtsaDataFiles> _logger;

    public NhtsaDataFiles(string dataDirectory, ILogger<NhtsaDataFiles> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(logger);

        _rawDirectory = Path.Combine(dataDirectory, "raw");
        _logger = logger;
    }

    public string ComplaintsFile => Path.Combine(_rawDirectory, "FLAT_CMPL.txt");

    public IReadOnlyList<string> RecallFiles =>
    [
        Path.Combine(_rawDirectory, "FLAT_RCL_POST_2010.txt"),
        Path.Combine(_rawDirectory, "FLAT_RCL_PRE_2010.txt")
    ];

    public async Task EnsureAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_rawDirectory);

        foreach (var (url, archive, extracted) in Sources)
        {
            var extractedPath = Path.Combine(_rawDirectory, extracted);
            if (File.Exists(extractedPath))
            {
                _logger.LogInformation("Found {FileName} ({SizeMegabytes} MB)", extracted, new FileInfo(extractedPath).Length / 1_048_576);
                continue;
            }

            var archivePath = Path.Combine(_rawDirectory, archive);
            if (!File.Exists(archivePath))
            {
                await DownloadAsync(url, archivePath, cancellationToken).ConfigureAwait(false);
            }

            _logger.LogInformation("Extracting {Archive}", archive);
            ZipFile.ExtractToDirectory(archivePath, _rawDirectory, overwriteFiles: true);
        }
    }

    private async Task DownloadAsync(string url, string destinationPath, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Downloading {Url}", url);

        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var temporaryPath = destinationPath + ".partial";
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var destination = File.Create(temporaryPath))
        {
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporaryPath, destinationPath, overwrite: true);
        _logger.LogInformation("Downloaded {SizeMegabytes} MB to {Destination}", new FileInfo(destinationPath).Length / 1_048_576, destinationPath);
    }
}
