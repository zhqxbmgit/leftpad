namespace PcDs4Server;

internal interface IAtomicFileOperations
{
    bool FileExists(string path);
    string ReadAllText(string path);
    void CreateDirectory(string path);
    void WriteDurable(string path, byte[] content);
    void Replace(string sourcePath, string destinationPath, string? backupPath);
    void Move(string sourcePath, string destinationPath);
    void Delete(string path);
}

internal sealed class SystemAtomicFileOperations : IAtomicFileOperations
{
    public static SystemAtomicFileOperations Instance { get; } = new();

    private SystemAtomicFileOperations()
    {
    }

    public bool FileExists(string path) => File.Exists(path);

    public string ReadAllText(string path) => File.ReadAllText(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void WriteDurable(string path, byte[] content)
    {
        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.WriteThrough);
        stream.Write(content);
        stream.Flush(flushToDisk: true);
    }

    public void Replace(string sourcePath, string destinationPath, string? backupPath) =>
        File.Replace(sourcePath, destinationPath, backupPath, ignoreMetadataErrors: true);

    public void Move(string sourcePath, string destinationPath) =>
        File.Move(sourcePath, destinationPath);

    public void Delete(string path) => File.Delete(path);
}

internal sealed class AtomicFileWriteTransaction
{
    private readonly IAtomicFileOperations _files;
    private readonly string _destinationPath;
    private readonly string? _backupPath;
    private readonly bool _destinationExisted;
    private bool _completed;

    internal AtomicFileWriteTransaction(
        IAtomicFileOperations files,
        string destinationPath,
        string? backupPath,
        bool destinationExisted)
    {
        _files = files;
        _destinationPath = destinationPath;
        _backupPath = backupPath;
        _destinationExisted = destinationExisted;
    }

    public void Commit()
    {
        if (_completed) return;
        _completed = true;
        AtomicFilePersistence.TryDelete(_files, _backupPath);
    }

    public bool TryRollback(out string error)
    {
        if (_completed)
        {
            error = "The persistence transaction has already completed.";
            return false;
        }

        try
        {
            if (_destinationExisted)
            {
                if (string.IsNullOrEmpty(_backupPath) || !_files.FileExists(_backupPath))
                    throw new IOException("The original settings backup is unavailable.");
                _files.Replace(_backupPath, _destinationPath, backupPath: null);
            }
            else if (_files.FileExists(_destinationPath))
            {
                _files.Delete(_destinationPath);
            }

            _completed = true;
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}

internal static class AtomicFilePersistence
{
    public static bool TryWrite(
        string destinationPath,
        byte[] content,
        IAtomicFileOperations files,
        bool retainOriginalForRollback,
        out AtomicFileWriteTransaction? transaction,
        out string error,
        out string? errorType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(files);

        string? directory = Path.GetDirectoryName(destinationPath);
        string leafName = Path.GetFileName(destinationPath);
        string transactionId = Guid.NewGuid().ToString("N");
        string temporaryPath = Path.Combine(
            string.IsNullOrEmpty(directory) ? "." : directory,
            $".{leafName}.{transactionId}.tmp");
        string? backupPath = retainOriginalForRollback
            ? Path.Combine(
                string.IsNullOrEmpty(directory) ? "." : directory,
                $".{leafName}.{transactionId}.bak")
            : null;

        transaction = null;
        try
        {
            if (!string.IsNullOrEmpty(directory))
                files.CreateDirectory(directory);

            files.WriteDurable(temporaryPath, content);
            bool destinationExisted = files.FileExists(destinationPath);
            if (destinationExisted)
                files.Replace(temporaryPath, destinationPath, backupPath);
            else
                files.Move(temporaryPath, destinationPath);

            transaction = new AtomicFileWriteTransaction(
                files,
                destinationPath,
                destinationExisted ? backupPath : null,
                destinationExisted);
            error = string.Empty;
            errorType = null;
            return true;
        }
        catch (Exception ex)
        {
            bool preserveBackup = false;
            if (!string.IsNullOrEmpty(backupPath) && files.FileExists(backupPath))
            {
                try
                {
                    if (files.FileExists(destinationPath))
                        files.Replace(backupPath, destinationPath, backupPath: null);
                    else
                        preserveBackup = true;
                }
                catch
                {
                    // Preserve the backup if an ambiguous replace failure cannot be rolled back.
                    preserveBackup = true;
                }
            }
            TryDelete(files, temporaryPath);
            if (!preserveBackup) TryDelete(files, backupPath);
            error = ex.Message;
            errorType = ex.GetType().Name;
            return false;
        }
    }

    internal static void TryDelete(IAtomicFileOperations files, string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            if (files.FileExists(path)) files.Delete(path);
        }
        catch
        {
            // Cleanup is best effort and must not hide the original persistence result.
        }
    }
}
