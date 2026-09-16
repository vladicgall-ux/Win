using System.Runtime.InteropServices;

namespace WinFileRecovery.Core.Native;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct WINTRUST_FILE_INFO
{
    public uint cbStruct;
    [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
    public IntPtr hFile;
    public IntPtr pgKnownSubject;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WINTRUST_DATA
{
    public uint cbStruct;
    public IntPtr pPolicyCallbackData;
    public IntPtr pSIPClientData;
    public uint dwUIChoice;
    public uint fdwRevocationChecks;
    public uint dwUnionChoice;
    public IntPtr pFile;
    public uint dwStateAction;
    public IntPtr hWVTStateData;
    public IntPtr pwszURLReference;
    public uint dwProvFlags;
    public uint dwUIContext;
    public IntPtr pSignatureSettings;
}

internal static class WinTrustNative
{
    public static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 =
        new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    public const uint WTD_UI_NONE = 2;
    public const uint WTD_REVOKE_NONE = 0;
    public const uint WTD_CHOICE_FILE = 1;
    public const uint WTD_STATEACTION_VERIFY = 1;
    public const uint WTD_STATEACTION_CLOSE = 2;
    public const uint WTD_SAFER_FLAG = 0x100;

    // Common WinVerifyTrust return codes (HRESULT-style, as uint).
    public const uint ERROR_SUCCESS = 0;
    public const uint TRUST_E_NOSIGNATURE = 0x800B0100;
    public const uint TRUST_E_EXPLICIT_DISTRUST = 0x800B0111;
    public const uint TRUST_E_SUBJECT_NOT_TRUSTED = 0x800B0004;
    public const uint CRYPT_E_SECURITY_SETTINGS = 0x80092026;
    public const uint TRUST_E_BAD_DIGEST = 0x80096010;

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID, IntPtr pWVTData);
}
