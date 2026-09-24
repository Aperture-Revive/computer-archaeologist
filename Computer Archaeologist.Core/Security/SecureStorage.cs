using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace ComputerArchaeologist.Core.Security;

/// <summary>
/// Stores the API key outside of any plain-text configuration file.
/// The interface exists so the pipeline never depends on a specific Windows API.
/// </summary>
public interface ISecureStorage
{
    /// <summary>Returns the stored key, or null. Never logged, never serialised.</summary>
    string? GetApiKey();

    void SetApiKey(string? apiKey);

    bool HasApiKey { get; }

    void ClearApiKey();

    /// <summary>Localization key describing where the secret physically lives.</summary>
    string StorageDescriptionKey { get; }
}

/// <summary>
/// Windows implementation. The key is written to the <b>Windows Credential Manager</b>
/// (credential type <c>CRED_TYPE_GENERIC</c>, local-machine persistence). When the Credential API is
/// unavailable the key is instead written to a DPAPI-protected file scoped to the current user.
/// <para>
/// Under no circumstances is the key written to <c>settings.json</c>, the repository, a log file or
/// a crash dump.
/// </para>
/// </summary>
public sealed class WindowsSecureStorage : ISecureStorage
{
    public const string TargetName = "ComputerArchaeologist/OpenAI";

    private readonly ILogger<WindowsSecureStorage>? _logger;
    private readonly string _fallbackPath;
    private string? _cached;
    private bool _cacheValid;

    public WindowsSecureStorage(ILogger<WindowsSecureStorage>? logger = null)
    {
        _logger = logger;
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Computer Archaeologist");
        _fallbackPath = Path.Combine(root, "credentials.bin");
    }

    public string StorageDescriptionKey =>
        OperatingSystem.IsWindows() ? "Settings_KeyStorage_CredentialManager" : "Settings_KeyStorage_Memory";

    public bool HasApiKey => !string.IsNullOrEmpty(GetApiKey());

    public string? GetApiKey()
    {
        if (_cacheValid)
        {
            return _cached;
        }

        var value = ReadCore();
        _cached = value;
        _cacheValid = true;
        return value;
    }

    public void SetApiKey(string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            ClearApiKey();
            return;
        }

        var trimmed = apiKey.Trim();

        if (OperatingSystem.IsWindows())
        {
            if (TryWriteCredential(trimmed))
            {
                _cached = trimmed;
                _cacheValid = true;
                return;
            }

            WriteDpapiFallback(trimmed);
        }
        else
        {
            // Non-Windows hosts (unit tests) keep the value in memory only.
            _logger?.LogWarning("Secure storage is running without a Windows credential store; the key is held in memory only");
        }

        _cached = trimmed;
        _cacheValid = true;
    }

    public void ClearApiKey()
    {
        if (OperatingSystem.IsWindows())
        {
            TryDeleteCredential();
            TryDeleteFile(_fallbackPath);
        }

        _cached = null;
        _cacheValid = true;
        _logger?.LogInformation("Stored API key was cleared");
    }

    private string? ReadCore()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var fromCredential = TryReadCredential();
        if (!string.IsNullOrEmpty(fromCredential))
        {
            return fromCredential;
        }

        return ReadDpapiFallback();
    }

    // ------------------------------------------------------------ Credential Manager

    private bool TryWriteCredential(string secret)
    {
        var blob = Encoding.Unicode.GetBytes(secret);
        var blobHandle = Marshal.AllocHGlobal(blob.Length);
        var targetHandle = Marshal.StringToHGlobalUni(TargetName);
        var userHandle = Marshal.StringToHGlobalUni("ComputerArchaeologist");

        try
        {
            Marshal.Copy(blob, 0, blobHandle, blob.Length);

            var credential = new NativeCredential
            {
                Type = CredTypeGeneric,
                TargetName = targetHandle,
                CredentialBlob = blobHandle,
                CredentialBlobSize = (uint)blob.Length,
                Persist = CredPersistLocalMachine,
                UserName = userHandle,
                Comment = IntPtr.Zero,
                Attributes = IntPtr.Zero,
                AttributeCount = 0,
                TargetAlias = IntPtr.Zero,
                Flags = 0,
            };

            var size = Marshal.SizeOf<NativeCredential>();
            var pointer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(credential, pointer, false);
                if (CredWriteW(pointer, 0))
                {
                    _logger?.LogInformation("API key stored in the Windows Credential Manager");
                    return true;
                }

                _logger?.LogWarning("CredWrite failed with Win32 error {Error}; falling back to DPAPI", Marshal.GetLastWin32Error());
                return false;
            }
            finally
            {
                Marshal.FreeHGlobal(pointer);
            }
        }
        finally
        {
            Array.Clear(blob);
            Marshal.FreeHGlobal(blobHandle);
            Marshal.FreeHGlobal(targetHandle);
            Marshal.FreeHGlobal(userHandle);
        }
    }

    private string? TryReadCredential()
    {
        if (!CredReadW(TargetName, CredTypeGeneric, 0, out var pointer) || pointer == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            if (credential.CredentialBlobSize <= 0 || credential.CredentialBlob == IntPtr.Zero)
            {
                return null;
            }

            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            try
            {
                return Encoding.Unicode.GetString(bytes);
            }
            finally
            {
                Array.Clear(bytes);
            }
        }
        finally
        {
            CredFree(pointer);
        }
    }

    private void TryDeleteCredential()
    {
        if (!CredDeleteW(TargetName, CredTypeGeneric, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != 1168 /* ERROR_NOT_FOUND */)
            {
                _logger?.LogDebug("CredDelete reported Win32 error {Error}", error);
            }
        }
    }

    // ------------------------------------------------------------ DPAPI fallback

    private void WriteDpapiFallback(string secret)
    {
        try
        {
            var directory = Path.GetDirectoryName(_fallbackPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var protectedBytes = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(secret),
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser);

            File.WriteAllBytes(_fallbackPath, protectedBytes);
            _logger?.LogInformation("API key stored with DPAPI at {Path}", _fallbackPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or PlatformNotSupportedException)
        {
            _logger?.LogWarning(ex, "Could not persist the API key; it will only live for this session");
        }
    }

    private string? ReadDpapiFallback()
    {
        try
        {
            if (!File.Exists(_fallbackPath))
            {
                return null;
            }

            var plain = ProtectedData.Unprotect(
                File.ReadAllBytes(_fallbackPath),
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser);

            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or PlatformNotSupportedException)
        {
            _logger?.LogWarning(ex, "Could not read the DPAPI-protected API key");
            return null;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best effort.
        }
    }

    // ------------------------------------------------------------ interop

    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWriteW(IntPtr credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredReadW(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDeleteW(string target, uint type, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredFree")]
    private static extern void CredFree(IntPtr buffer);
}

/// <summary>Volatile storage used by tests and by any non-Windows host.</summary>
public sealed class InMemorySecureStorage : ISecureStorage
{
    private string? _value;

    public string StorageDescriptionKey => "Settings_KeyStorage_Memory";

    public bool HasApiKey => !string.IsNullOrEmpty(_value);

    public string? GetApiKey() => _value;

    public void SetApiKey(string? apiKey) => _value = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();

    public void ClearApiKey() => _value = null;
}
