using System.Runtime.InteropServices;
using WinFileRecovery.Core.Native;

namespace WinFileRecovery.Core.Verification;

/// <summary>
/// Wraps the WinTrust "WinVerifyTrust" API to check whether a file carries a
/// valid Authenticode signature. Meaningful mainly for .exe/.dll/.msi/.cab
/// and similar signable formats — a document, image, or archive returning
/// NotSigned is expected and not an error, since those formats normally
/// don't carry Authenticode signatures at all.
/// </summary>
public static class AuthenticodeVerifier
{
    public static AuthenticodeStatus Verify(string filePath)
    {
        var fileInfo = new WINTRUST_FILE_INFO
        {
            cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
            pcwszFilePath = filePath,
            hFile = IntPtr.Zero,
            pgKnownSubject = IntPtr.Zero,
        };

        IntPtr fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        try
        {
            Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);

            var trustData = new WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                pPolicyCallbackData = IntPtr.Zero,
                pSIPClientData = IntPtr.Zero,
                dwUIChoice = WinTrustNative.WTD_UI_NONE,
                fdwRevocationChecks = WinTrustNative.WTD_REVOKE_NONE,
                dwUnionChoice = WinTrustNative.WTD_CHOICE_FILE,
                pFile = fileInfoPtr,
                dwStateAction = WinTrustNative.WTD_STATEACTION_VERIFY,
                hWVTStateData = IntPtr.Zero,
                pwszURLReference = IntPtr.Zero,
                dwProvFlags = WinTrustNative.WTD_SAFER_FLAG,
                dwUIContext = 0,
                pSignatureSettings = IntPtr.Zero,
            };

            IntPtr trustDataPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_DATA>());
            try
            {
                Marshal.StructureToPtr(trustData, trustDataPtr, false);

                uint result = WinTrustNative.WinVerifyTrust(
                    IntPtr.Zero, WinTrustNative.WINTRUST_ACTION_GENERIC_VERIFY_V2, trustDataPtr);

                // Always release WinTrust's internal state for this call,
                // regardless of the verification outcome, or it leaks.
                trustData.dwStateAction = WinTrustNative.WTD_STATEACTION_CLOSE;
                Marshal.StructureToPtr(trustData, trustDataPtr, false);
                WinTrustNative.WinVerifyTrust(IntPtr.Zero, WinTrustNative.WINTRUST_ACTION_GENERIC_VERIFY_V2, trustDataPtr);

                return MapResult(result);
            }
            finally
            {
                Marshal.FreeHGlobal(trustDataPtr);
            }
        }
        catch
        {
            return AuthenticodeStatus.Unknown;
        }
        finally
        {
            Marshal.FreeHGlobal(fileInfoPtr);
        }
    }

    private static AuthenticodeStatus MapResult(uint result) => result switch
    {
        WinTrustNative.ERROR_SUCCESS => AuthenticodeStatus.Valid,
        WinTrustNative.TRUST_E_NOSIGNATURE => AuthenticodeStatus.NotSigned,
        WinTrustNative.TRUST_E_EXPLICIT_DISTRUST => AuthenticodeStatus.Invalid,
        WinTrustNative.TRUST_E_SUBJECT_NOT_TRUSTED => AuthenticodeStatus.Invalid,
        WinTrustNative.CRYPT_E_SECURITY_SETTINGS => AuthenticodeStatus.Invalid,
        WinTrustNative.TRUST_E_BAD_DIGEST => AuthenticodeStatus.Invalid,
        _ => AuthenticodeStatus.Unknown,
    };
}
