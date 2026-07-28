namespace InstallSentinel.Common.Helpers;

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

public static class PathSanitizer
{
    private static readonly Dictionary<string, string> VolumeCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ReaderWriterLockSlim CacheLock = new();

    public static string NormalizePath(string kernelPath)
    {
        if (string.IsNullOrWhiteSpace(kernelPath))
            return kernelPath;

        if (!kernelPath.StartsWith(@"\Device\", StringComparison.OrdinalIgnoreCase))
            return kernelPath;

        var devicePath = ExtractDevicePath(kernelPath);
        if (string.IsNullOrEmpty(devicePath))
            return kernelPath;

        var driveLetter = GetDriveLetterForDevice(devicePath);
        if (string.IsNullOrEmpty(driveLetter))
            return kernelPath;

        var remainder = kernelPath[devicePath.Length..];
        return driveLetter + remainder.Replace('/', '\\');
    }

    private static string ExtractDevicePath(string kernelPath)
    {
        var parts = kernelPath.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return string.Empty;

        return $@"\{parts[0]}\{parts[1]}";
    }

    private static string GetDriveLetterForDevice(string devicePath)
    {
        CacheLock.EnterReadLock();
        try
        {
            if (VolumeCache.TryGetValue(devicePath, out var cached))
                return cached;
        }
        finally
        {
            CacheLock.ExitReadLock();
        }

        CacheLock.EnterWriteLock();
        try
        {
            if (VolumeCache.TryGetValue(devicePath, out var cached))
                return cached;

            var drives = DriveInfo.GetDrives();
            foreach (var drive in drives)
            {
                if (!drive.IsReady)
                    continue;

                try
                {
                    var deviceName = GetDeviceNameForDrive(drive.Name);
                    if (deviceName != null && deviceName.Equals(devicePath, StringComparison.OrdinalIgnoreCase))
                    {
                        var letter = drive.Name.TrimEnd('\\');
                        VolumeCache[devicePath] = letter;
                        return letter;
                    }
                }
                catch
                {
                    // Ignore inaccessible drives
                }
            }

            VolumeCache[devicePath] = string.Empty;
            return string.Empty;
        }
        finally
        {
            CacheLock.ExitWriteLock();
        }
    }

    private static string? GetDeviceNameForDrive(string drivePath)
    {
        var sb = new StringBuilder(260);
        if (GetVolumeNameForVolumeMountPoint(drivePath, sb, sb.Capacity))
        {
            return sb.ToString().TrimEnd('\\');
        }
        return null;
    }

    public static void ClearCache()
    {
        CacheLock.EnterWriteLock();
        try
        {
            VolumeCache.Clear();
        }
        finally
        {
            CacheLock.ExitWriteLock();
        }
    }

    public static string TruncatePath(string path, int maxLength = 80)
    {
        if (path.Length <= maxLength)
            return path;

        var parts = path.Split('\\');
        if (parts.Length <= 2)
            return path[..maxLength];

        var start = parts[0] + "\\" + parts[1] + "\\";
        var end = string.Join("\\", parts[^2..]);
        var middle = "...";

        var result = start + middle + "\\" + end;
        return result.Length > maxLength ? result[..maxLength] : result;
    }

    public static string GetShortPath(string path, int maxLength = 80)
        => TruncatePath(path, maxLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeNameForVolumeMountPoint(string lpszVolumeMountPoint, StringBuilder lpszVolumeName, int cchBufferLength);
}

public static class HashUtils
{
    public static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken = default)
    {
        using var sha256 = SHA256.Create();
        await using var stream = File.OpenRead(filePath);
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string ComputeSha256(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static async Task<string> ComputeMd5Async(string filePath, CancellationToken cancellationToken = default)
    {
        using var md5 = MD5.Create();
        await using var stream = File.OpenRead(filePath);
        var hash = await md5.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string ComputeMd5(string filePath)
    {
        using var md5 = MD5.Create();
        using var stream = File.OpenRead(filePath);
        var hash = md5.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}