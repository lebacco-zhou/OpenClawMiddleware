using System.IO.Hashing;

namespace OpenClawMiddleware.Services;

public interface IFileStorageService
{
    Task<string> SaveFileAsync(string filename, byte[] content, string mimeType);
    Task<byte[]?> GetFileAsync(string fileId);
    Task<bool> DeleteFileAsync(string fileId);
    Task<string> GetTempPathAsync(string uploadId);
    Task SaveChunkAsync(string uploadId, int chunkIndex, byte[] chunk);
    Task<byte[]?> MergeChunksAsync(string uploadId, int totalChunks);
}

public class FileStorageService : IFileStorageService
{
    private readonly string _basePath;
    private readonly string _tempPath;
    private readonly string _fileUrlPrefix;
    private readonly long _maxFileSizeBytes;
    private readonly ILogger<FileStorageService> _logger;

    public FileStorageService(ILogger<FileStorageService> logger, IConfiguration config)
    {
        _logger = logger;
        _basePath = config.GetValue<string>("FileStorage:BasePath") ?? "/var/www/openclaw-files/uploads";
        _tempPath = config.GetValue<string>("FileStorage:TempPath") ?? "/var/www/openclaw-files/temp";
        _fileUrlPrefix = config.GetValue<string>("FileStorage:FileUrlPrefix") ?? "https://www.lebacco.cn:8444/files/";
        _maxFileSizeBytes = config.GetValue<long>("FileStorage:MaxFileSizeBytes", 52428800); // 50MB

        EnsureDirectoriesExist();
    }

    private void EnsureDirectoriesExist()
    {
        if (!Directory.Exists(_basePath))
        {
            Directory.CreateDirectory(_basePath);
            _logger.LogInformation("Created base directory: {Path}", _basePath);
        }

        if (!Directory.Exists(_tempPath))
        {
            Directory.CreateDirectory(_tempPath);
            _logger.LogInformation("Created temp directory: {Path}", _tempPath);
        }
    }

    public async Task<string> SaveFileAsync(string filename, byte[] content, string mimeType)
    {
        if (content.Length > _maxFileSizeBytes)
        {
            throw new InvalidOperationException($"File size exceeds maximum ({_maxFileSizeBytes} bytes)");
        }

        var fileId = Guid.NewGuid().ToString();
        var extension = Path.GetExtension(filename);
        var datePath = DateTime.UtcNow.ToString("yyyy/MM/dd");
        var directory = Path.Combine(_basePath, datePath);

        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var filePath = Path.Combine(directory, $"{fileId}{extension}");
        await File.WriteAllBytesAsync(filePath, content);

        _logger.LogInformation("Saved file {FileId} ({Size} bytes) to {Path}", fileId, content.Length, filePath);

        return fileId;
    }

    public Task<byte[]?> GetFileAsync(string fileId)
    {
        // 需要遍历目录查找文件，或使用数据库索引
        // 简化实现：返回 null，实际使用需要索引
        return Task.FromResult<byte[]?>(null);
    }

    public Task<bool> DeleteFileAsync(string fileId)
    {
        // TODO: 实现文件删除
        return Task.FromResult(false);
    }

    public async Task<string> GetTempPathAsync(string uploadId)
    {
        var uploadTempPath = Path.Combine(_tempPath, uploadId);
        if (!Directory.Exists(uploadTempPath))
        {
            Directory.CreateDirectory(uploadTempPath);
        }
        return uploadTempPath;
    }

    public async Task SaveChunkAsync(string uploadId, int chunkIndex, byte[] chunk)
    {
        var tempPath = await GetTempPathAsync(uploadId);
        var chunkPath = Path.Combine(tempPath, $"chunk-{chunkIndex}");
        await File.WriteAllBytesAsync(chunkPath, chunk);
    }

    public async Task<byte[]?> MergeChunksAsync(string uploadId, int totalChunks)
    {
        var tempPath = await GetTempPathAsync(uploadId);
        var mergedPath = Path.Combine(tempPath, "merged");

        using var mergedStream = new FileStream(mergedPath, FileMode.Create);
        
        for (int i = 0; i < totalChunks; i++)
        {
            var chunkPath = Path.Combine(tempPath, $"chunk-{i}");
            if (!File.Exists(chunkPath))
            {
                _logger.LogWarning("Missing chunk {Index} for upload {UploadId}", i, uploadId);
                return null;
            }

            var chunkData = await File.ReadAllBytesAsync(chunkPath);
            await mergedStream.WriteAsync(chunkData);
        }

        return await File.ReadAllBytesAsync(mergedPath);
    }
}
