namespace WinFileRecovery.Core.Verification;

public enum AuthenticodeStatus
{
    /// <summary>File carries a valid Authenticode signature chaining to a trusted root.</summary>
    Valid,

    /// <summary>File has no embedded signature at all — normal for documents, media, plain data files.</summary>
    NotSigned,

    /// <summary>File is signed, but the signature is invalid, tampered, or its certificate is untrusted/revoked.</summary>
    Invalid,

    /// <summary>Verification could not be completed (I/O error, unsupported format for signing, etc.).</summary>
    Unknown,
}
