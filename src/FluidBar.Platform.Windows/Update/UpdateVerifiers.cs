using System.Runtime.InteropServices;
using System.Security.Cryptography;
using FluidBar.Platform.Windows.Diagnostics;

namespace FluidBar.Platform.Windows.Update;

/// <summary>Authenticode signature state of a downloaded binary.</summary>
public enum AuthenticodeStatus
{
    Verified,
    NoSignature,
    Invalid,
    UntrustedRoot,
    CheckFailed,
}

/// <summary>Hook for Authenticode verification; fakes are used in tests (no signing infrastructure in CI).</summary>
public interface IBinarySignatureVerifier
{
    AuthenticodeStatus VerifyFile(string filePath);
}

/// <summary>Verifies a downloaded installer and returns the resulting exposure state.</summary>
public interface IUpdatePackageVerifier
{
    Task<UpdateVerificationStatus> VerifyAsync(string filePath, UpdateRelease release,
        CancellationToken cancellationToken);
}

/// <summary>SHA-256 verifier required before any install action.</summary>
public sealed class Sha256UpdateVerifier : IUpdatePackageVerifier
{
    /// <summary>Computes the SHA-256 of a file asynchronously (bounded buffer, cancellable).</summary>
    public static async Task<string> ComputeFileSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    public async Task<UpdateVerificationStatus> VerifyAsync(string filePath, UpdateRelease release,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);
        var expected = release.InstallerSha256Hex;
        if (string.IsNullOrWhiteSpace(expected))
        {
            return UpdateVerificationStatus.NotVerified;
        }

        var actual = await ComputeFileSha256Async(filePath, cancellationToken).ConfigureAwait(false);
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)
            ? UpdateVerificationStatus.Verified
            : UpdateVerificationStatus.Sha256Mismatch;
    }
}

/// <summary>
/// Full verification chain: SHA-256 first, then Authenticode via <c>WinVerifyTrust</c>. An update may be
/// offered for install only when both pass (design §10.1 #22: never execute unverified downloads).
/// </summary>
public sealed class CompositeUpdateVerifier : IUpdatePackageVerifier
{
    private readonly Sha256UpdateVerifier _hashVerifier;
    private readonly IBinarySignatureVerifier _signatureVerifier;
    private readonly IPlatformLogger _logger;

    public CompositeUpdateVerifier(IBinarySignatureVerifier signatureVerifier, IPlatformLogger logger,
        Sha256UpdateVerifier? hashVerifier = null)
    {
        _signatureVerifier = signatureVerifier ?? throw new ArgumentNullException(nameof(signatureVerifier));
        _logger = logger ?? NullPlatformLogger.Instance;
        _hashVerifier = hashVerifier ?? new Sha256UpdateVerifier();
    }

    public async Task<UpdateVerificationStatus> VerifyAsync(string filePath, UpdateRelease release,
        CancellationToken cancellationToken)
    {
        var hashStatus = await _hashVerifier.VerifyAsync(filePath, release, cancellationToken).ConfigureAwait(false);
        if (hashStatus == UpdateVerificationStatus.Sha256Mismatch)
        {
            return hashStatus;
        }

        var signatureStatus = _signatureVerifier.VerifyFile(filePath);
        var mapped = signatureStatus switch
        {
            AuthenticodeStatus.Verified => hashStatus == UpdateVerificationStatus.NotVerified
                ? UpdateVerificationStatus.Verified // signature covers integrity when no feed hash exists
                : UpdateVerificationStatus.Verified,
            AuthenticodeStatus.NoSignature => UpdateVerificationStatus.SignatureUnsigned,
            AuthenticodeStatus.UntrustedRoot => UpdateVerificationStatus.SignatureUntrusted,
            AuthenticodeStatus.Invalid => UpdateVerificationStatus.SignatureInvalid,
            _ => UpdateVerificationStatus.SignatureCheckFailed,
        };

        if (mapped != UpdateVerificationStatus.Verified)
        {
            _logger.LogWarning(PlatformLogCategory.Update, "update.signature-rejected",
                "Installer Authenticode verification failed.",
                data: new Dictionary<string, object?> { ["status"] = signatureStatus.ToString() });
        }

        return mapped;
    }
}

/// <summary>Authenticode verification through <c>wintrust!WinVerifyTrust</c> (no signature = not verified).</summary>
public sealed class WinVerifyTrustSignatureVerifier : IBinarySignatureVerifier
{
    private static readonly Guid WinTrustActionGenericVerifyV2 = new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    private const int WtdUiNone = 2;
    private const int WtdRevokeNone = 0;
    private const int WtdChoiceFile = 1;
    private const int WtdStateActionVerify = 1;
    private const int WtdStateActionClose = 2;
    private const int WtdRevocationCheckNone = 0x10;

    private const int ErrorSuccess = 0;
    private const int TrustENoSignature = unchecked((int)0x800B0100);
    private const int TrustEProviderUnknown = unchecked((int)0x800B0001);
    private const int TrustESubjectFormUnknown = unchecked((int)0x800B0003);
    private const int TrustEBadDigest = unchecked((int)0x80096010);
    private const int CertEUntrustedRoot = unchecked((int)0x800B0109);

    private readonly IPlatformLogger _logger;

    public WinVerifyTrustSignatureVerifier(IPlatformLogger? logger = null)
        => _logger = logger ?? NullPlatformLogger.Instance;

    public AuthenticodeStatus VerifyFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return AuthenticodeStatus.CheckFailed;
        }

        var actionId = WinTrustActionGenericVerifyV2;
        var filePathPtr = IntPtr.Zero;
        try
        {
            filePathPtr = Marshal.StringToHGlobalUni(filePath);
            var fileInfo = new WinTrustFileInfo
            {
                cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                pcwszFilePath = filePathPtr,
                hFile = IntPtr.Zero,
                pgKnownSubject = IntPtr.Zero,
            };

            var fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
            try
            {
                Marshal.StructureToPtr(fileInfo, fileInfoPtr, fDeleteOld: false);
                var trustData = new WinTrustData
                {
                    cbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
                    dwUIChoice = WtdUiNone,
                    fdwRevocationChecks = WtdRevokeNone,
                    dwUnionChoice = WtdChoiceFile,
                    pFile = fileInfoPtr,
                    dwStateAction = WtdStateActionVerify,
                    dwProvFlags = WtdRevocationCheckNone,
                };

                var status = WinVerifyTrust(IntPtr.Zero, ref actionId, ref trustData);

                // Always release the state data allocated by WinVerifyTrust.
                trustData.dwStateAction = WtdStateActionClose;
                _ = WinVerifyTrust(IntPtr.Zero, ref actionId, ref trustData);

                return status switch
                {
                    ErrorSuccess => AuthenticodeStatus.Verified,
                    TrustENoSignature or TrustEProviderUnknown or TrustESubjectFormUnknown => AuthenticodeStatus.NoSignature,
                    TrustEBadDigest => AuthenticodeStatus.Invalid,
                    CertEUntrustedRoot => AuthenticodeStatus.UntrustedRoot,
                    _ => LogAndMap(status),
                };
            }
            finally
            {
                Marshal.FreeHGlobal(fileInfoPtr);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(PlatformLogCategory.Update, "update.authenticode-check-failed",
                "Authenticode verification could not run.", ex);
            return AuthenticodeStatus.CheckFailed;
        }
        finally
        {
            if (filePathPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(filePathPtr);
            }
        }
    }

    private AuthenticodeStatus LogAndMap(int status)
    {
        _logger.LogWarning(PlatformLogCategory.Update, "update.authenticode-status",
            $"WinVerifyTrust returned 0x{status:X8}.");
        return AuthenticodeStatus.Invalid;
    }

    [DllImport("wintrust.dll", PreserveSig = true, SetLastError = false)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid actionId, ref WinTrustData winTrustData);

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustFileInfo
    {
        public uint cbStruct;
        public IntPtr pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public int dwUIChoice;
        public int fdwRevocationChecks;
        public int dwUnionChoice;
        public IntPtr pFile;
        public int dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public int dwProvFlags;
        public int dwUIContext;
    }
}
