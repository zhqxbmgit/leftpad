using System.Security.Cryptography;
using System.Text;

namespace PcDs4Server;

internal static class ReceiverIdentityStore
{
    internal static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "leftpad", "receiver-id");

    internal static byte[] LoadOrCreate(string path, Action<string> log)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        // Serialize creators/repairers across processes. Publish only a fully flushed file.
        using var mutex = new Mutex(false, "Local\\LeftPad.ReceiverIdentity." +
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant()))));
        bool acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(5)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("Timed out acquiring receiver identity store.");
            if (File.Exists(path))
            {
                string text = File.ReadAllText(path);
                if (text.Length == 32 && text.All(Uri.IsHexDigit)) return Convert.FromHexString(text);
                log($"[Discovery] Invalid receiver identity file; rebuilding: {path}");
            }
            byte[] id = RandomNumberGenerator.GetBytes(16);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    file.Write(Encoding.ASCII.GetBytes(Convert.ToHexString(id).ToLowerInvariant()));
                    file.Flush(flushToDisk: true);
                }
                File.Move(temporary, path, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return id;
        }
        finally { if (acquired) mutex.ReleaseMutex(); }
    }
}
