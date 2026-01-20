using Application.Persistence.Interfaces;
using Common;
using Common.Configuration;
using Infrastructure.Persistence.Interfaces;

namespace Infrastructure.Persistence.SqlServer.ApplicationServices;

/// <summary>
///     Provides a file system-based implementation of blob storage for on-premise deployments
/// </summary>
public sealed class FileSystemBlobStore : IBlobStore
{
    private const string ContentTypeFileName = "_contenttype";
    private readonly string _rootPath;

    private FileSystemBlobStore(string rootPath)
    {
        _rootPath = rootPath;
        EnsureRootPathExists();
    }

    public static FileSystemBlobStore Create(IConfigurationSettings settings)
    {
        var rootPath = settings.Platform.GetSection("ApplicationServices:Persistence:BlobStorage:FileSystem:RootPath")
            .Value;

        if (string.IsNullOrWhiteSpace(rootPath))
        {
            rootPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "saastack", "blobs");
        }

        return new FileSystemBlobStore(rootPath);
    }

    public async Task<Result<Error>> DeleteAsync(string containerName, string blobName,
        CancellationToken cancellationToken)
    {
        try
        {
            var blobPath = GetBlobPath(containerName, blobName);
            var contentTypePath = GetContentTypePath(containerName, blobName);

            if (File.Exists(blobPath))
            {
                await Task.Run(() => File.Delete(blobPath), cancellationToken);
            }

            if (File.Exists(contentTypePath))
            {
                await Task.Run(() => File.Delete(contentTypePath), cancellationToken);
            }

            return Result.Ok;
        }
        catch (Exception ex)
        {
            return Error.Unexpected($"Failed to delete blob {blobName} from {containerName}: {ex.Message}");
        }
    }

#if TESTINGONLY
    public async Task<Result<Error>> DestroyAllAsync(string containerName, CancellationToken cancellationToken)
    {
        try
        {
            var containerPath = GetContainerPath(containerName);

            if (Directory.Exists(containerPath))
            {
                await Task.Run(() => Directory.Delete(containerPath, true), cancellationToken);
            }

            return Result.Ok;
        }
        catch (Exception ex)
        {
            return Error.Unexpected($"Failed to destroy all blobs in {containerName}: {ex.Message}");
        }
    }
#endif

    public async Task<Result<Optional<Blob>, Error>> DownloadAsync(string containerName, string blobName,
        Stream stream, CancellationToken cancellationToken)
    {
        try
        {
            var blobPath = GetBlobPath(containerName, blobName);
            var contentTypePath = GetContentTypePath(containerName, blobName);

            if (!File.Exists(blobPath))
            {
                return Optional<Blob>.None;
            }

            await using var fileStream = new FileStream(blobPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            await fileStream.CopyToAsync(stream, cancellationToken);

            var contentType = "application/octet-stream";
            if (File.Exists(contentTypePath))
            {
                contentType = await File.ReadAllTextAsync(contentTypePath, cancellationToken);
            }

            return new Blob { ContentType = contentType }.ToOptional();
        }
        catch (Exception ex)
        {
            return Error.Unexpected($"Failed to download blob {blobName} from {containerName}: {ex.Message}");
        }
    }

    public async Task<Result<Error>> UploadAsync(string containerName, string blobName, string contentType,
        Stream stream, CancellationToken cancellationToken)
    {
        try
        {
            var blobPath = GetBlobPath(containerName, blobName);
            var contentTypePath = GetContentTypePath(containerName, blobName);

            EnsureContainerPathExists(containerName);

            await using var fileStream = new FileStream(blobPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await stream.CopyToAsync(fileStream, cancellationToken);

            await File.WriteAllTextAsync(contentTypePath, contentType, cancellationToken);

            return Result.Ok;
        }
        catch (Exception ex)
        {
            return Error.Unexpected($"Failed to upload blob {blobName} to {containerName}: {ex.Message}");
        }
    }

    private string GetContainerPath(string containerName)
    {
        return Path.Combine(_rootPath, containerName);
    }

    private string GetBlobPath(string containerName, string blobName)
    {
        return Path.Combine(GetContainerPath(containerName), blobName);
    }

    private string GetContentTypePath(string containerName, string blobName)
    {
        return Path.Combine(GetContainerPath(containerName), $"{blobName}{ContentTypeFileName}");
    }

    private void EnsureRootPathExists()
    {
        if (!Directory.Exists(_rootPath))
        {
            Directory.CreateDirectory(_rootPath);
        }
    }

    private void EnsureContainerPathExists(string containerName)
    {
        var containerPath = GetContainerPath(containerName);
        if (!Directory.Exists(containerPath))
        {
            Directory.CreateDirectory(containerPath);
        }
    }
}
