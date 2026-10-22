using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace FluidBar.Platform.Windows.Notifications;

/// <summary>
/// Reads the image referenced by a Windows toast's XML payload. UserNotificationListener exposes
/// text elements only; the image (QQ sender/group avatar, app override, etc.) lives in the Windows
/// notification database payload and is not part of the WinRT projection. The database is opened
/// read-only; failure always returns null so callers can fall back to the app icon.
/// </summary>
internal static class NotificationToastImageReader
{
    private const int SqliteOk = 0;
    private const int SqliteRow = 100;
    private const int SqliteReadOnly = 1;

    private static readonly string DatabasePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Microsoft", "Windows", "Notifications", "wpndatabase.db");

    /// <summary>Returns PNG/JPEG bytes for the toast image, or null when none is available.</summary>
    public static byte[]? TryRead(uint notificationId)
    {
        try
        {
            if (!File.Exists(DatabasePath))
            {
                return null;
            }

            var xml = ReadPayload(notificationId);
            if (string.IsNullOrWhiteSpace(xml))
            {
                return null;
            }

            var src = FindImageSource(xml);
            if (string.IsNullOrEmpty(src))
            {
                return null;
            }

            var path = ToLocalPath(src);
            if (path is null || !File.Exists(path))
            {
                return null;
            }

            var info = new FileInfo(path);
            return info.Length is > 0 and <= 1024 * 1024 ? File.ReadAllBytes(path) : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadPayload(uint notificationId)
    {
        var db = IntPtr.Zero;
        var statement = IntPtr.Zero;
        try
        {
            if (sqlite3_open_v2(DatabasePath, out db, SqliteReadOnly, IntPtr.Zero) != SqliteOk
                || db == IntPtr.Zero)
            {
                return null;
            }

            var sql = $"SELECT Payload FROM Notification WHERE Id = {notificationId} AND Payload IS NOT NULL ORDER BY rowid DESC LIMIT 1";
            if (sqlite3_prepare_v2(db, sql, -1, out statement, IntPtr.Zero) != SqliteOk
                || statement == IntPtr.Zero)
            {
                return null;
            }

            if (sqlite3_step(statement) != SqliteRow)
            {
                return null;
            }

            var pointer = sqlite3_column_blob(statement, 0);
            var length = sqlite3_column_bytes(statement, 0);
            if (pointer == IntPtr.Zero || length <= 0)
            {
                return null;
            }

            var bytes = new byte[length];
            Marshal.Copy(pointer, bytes, 0, length);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            if (statement != IntPtr.Zero)
            {
                _ = sqlite3_finalize(statement);
            }

            if (db != IntPtr.Zero)
            {
                _ = sqlite3_close_v2(db);
            }
        }
    }

    internal static string? FindImageSource(string xml)
    {
        var document = XDocument.Parse(xml);
        var images = document.Descendants()
            .Where(element => element.Name.LocalName == "image")
            .Select(element => new
            {
                Placement = element.Attribute("placement")?.Value,
                Source = element.Attribute("src")?.Value,
            })
            .Where(image => !string.IsNullOrWhiteSpace(image.Source))
            .ToArray();

        return images.FirstOrDefault(image =>
                   string.Equals(image.Placement, "appLogoOverride", StringComparison.OrdinalIgnoreCase))?.Source
               ?? images.FirstOrDefault()?.Source;
    }

    private static string? ToLocalPath(string source)
    {
        if (source.StartsWith("ms-appx:", StringComparison.OrdinalIgnoreCase)
            || source.StartsWith("http:", StringComparison.OrdinalIgnoreCase)
            || source.StartsWith("https:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            return uri.LocalPath;
        }

        return Path.IsPathFullyQualified(source) ? source : null;
    }

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int sqlite3_open_v2(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string filename, out IntPtr db, int flags, IntPtr vfs);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int sqlite3_prepare_v2(
        IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int numBytes, out IntPtr statement,
        IntPtr tail);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_step(IntPtr statement);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_column_blob(IntPtr statement, int column);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_column_bytes(IntPtr statement, int column);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_finalize(IntPtr statement);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_close_v2(IntPtr db);
}
