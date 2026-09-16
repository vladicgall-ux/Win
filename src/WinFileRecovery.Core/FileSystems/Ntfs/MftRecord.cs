using System.Text;

namespace WinFileRecovery.Core.FileSystems.Ntfs;

[Flags]
public enum MftRecordFlags : ushort
{
    None = 0,
    InUse = 0x0001,
    IsDirectory = 0x0002,
}

/// <summary>
/// One parsed 1024-byte (typically) NTFS FILE record: header, fixed-up via
/// the update sequence array, plus the attributes we care about for
/// recovery ($FILE_NAME, $DATA).
/// </summary>
public sealed class MftRecord
{
    public long RecordIndex { get; init; }
    public MftRecordFlags Flags { get; init; }
    public string? FileName { get; init; }
    public long LogicalFileSize { get; init; }
    public bool DataIsResident { get; init; }
    public byte[]? ResidentData { get; init; }
    public List<DataRun> DataRuns { get; init; } = new();

    /// <summary>
    /// The MFT record's own "last changed" timestamp, from $STANDARD_INFORMATION.
    /// NTFS updates this whenever the record itself is modified — including
    /// when a file is deleted (the record is rewritten to clear the in-use
    /// flag and unlink it) — so for a deleted record this is the closest
    /// available approximation of "when it was deleted". It is not exact:
    /// any other metadata change (rename, attribute edit) shortly before
    /// deletion would also move this timestamp.
    /// </summary>
    public DateTime? RecordChangedUtc { get; init; }

    public bool IsDeleted => (Flags & MftRecordFlags.InUse) == 0;
    public bool IsDirectory => (Flags & MftRecordFlags.IsDirectory) != 0;
    public bool HasFileName => !string.IsNullOrEmpty(FileName);

    /// <summary>
    /// Parses one raw MFT record buffer (already sector-fixed-up via
    /// <see cref="ApplyFixup"/>). Returns null for records that are not
    /// valid FILE records (empty/corrupt slack space).
    /// </summary>
    public static MftRecord? Parse(byte[] raw, long recordIndex, int bytesPerSector)
    {
        if (raw.Length < 48) return null;
        if (raw[0] != 'F' || raw[1] != 'I' || raw[2] != 'L' || raw[3] != 'E')
            return null;

        byte[] record = ApplyFixup(raw, bytesPerSector);

        ushort flags = BitConverter.ToUInt16(record, 22);
        uint bytesUsed = BitConverter.ToUInt32(record, 24);
        ushort firstAttrOffset = BitConverter.ToUInt16(record, 20);

        string? fileName = null;
        bool dataResident = false;
        byte[]? residentData = null;
        var dataRuns = new List<DataRun>();
        long logicalSize = 0;
        DateTime? recordChangedUtc = null;

        int pos = firstAttrOffset;
        int limit = Math.Min((int)bytesUsed, record.Length);

        while (pos + 16 <= limit)
        {
            uint attrType = BitConverter.ToUInt32(record, pos);
            if (attrType == 0xFFFFFFFF) break; // end marker

            uint attrLength = BitConverter.ToUInt32(record, pos + 4);
            if (attrLength == 0 || pos + attrLength > record.Length) break;

            byte nonResidentFlag = record[pos + 8];

            switch (attrType)
            {
                case 0x10: // $STANDARD_INFORMATION
                    if (nonResidentFlag == 0)
                        recordChangedUtc = ParseStandardInformationRecordChanged(record, pos);
                    break;

                case 0x30: // $FILE_NAME
                    if (nonResidentFlag == 0)
                        fileName = ParseFileNameAttribute(record, pos);
                    break;

                case 0x80: // $DATA (unnamed stream only)
                    ushort nameLength = record[pos + 9];
                    if (nameLength == 0) // unnamed => the primary data stream
                    {
                        if (nonResidentFlag == 0)
                        {
                            dataResident = true;
                            uint contentLen = BitConverter.ToUInt32(record, pos + 16);
                            ushort contentOffset = BitConverter.ToUInt16(record, pos + 20);
                            residentData = new byte[contentLen];
                            Array.Copy(record, pos + contentOffset, residentData, 0, (int)Math.Min(contentLen, record.Length - (pos + contentOffset)));
                            logicalSize = contentLen;
                        }
                        else
                        {
                            long realSize = BitConverter.ToInt64(record, pos + 48);
                            ushort runListOffset = BitConverter.ToUInt16(record, pos + 32);
                            int runListLength = (int)attrLength - runListOffset;
                            dataRuns = DataRunParser.Parse(record, pos + runListOffset, runListLength);
                            logicalSize = realSize;
                        }
                    }
                    break;
            }

            pos += (int)attrLength;
        }

        return new MftRecord
        {
            RecordIndex = recordIndex,
            Flags = (MftRecordFlags)flags,
            FileName = fileName,
            LogicalFileSize = logicalSize,
            DataIsResident = dataResident,
            ResidentData = residentData,
            DataRuns = dataRuns,
            RecordChangedUtc = recordChangedUtc,
        };
    }

    private static DateTime? ParseStandardInformationRecordChanged(byte[] record, int attrPos)
    {
        ushort contentOffset = BitConverter.ToUInt16(record, attrPos + 20);
        int contentStart = attrPos + contentOffset;
        if (contentStart + 24 > record.Length) return null;

        long fileTime = BitConverter.ToInt64(record, contentStart + 16); // MFT-changed time
        return FileTimeToUtc(fileTime);
    }

    private static DateTime? FileTimeToUtc(long windowsFileTime)
    {
        if (windowsFileTime <= 0) return null;
        try
        {
            return DateTime.FromFileTimeUtc(windowsFileTime);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null; // corrupt/garbage timestamp in a damaged record
        }
    }

    private static string ParseFileNameAttribute(byte[] record, int attrPos)
    {
        ushort contentOffset = BitConverter.ToUInt16(record, attrPos + 20);
        int nameStart = attrPos + contentOffset;

        byte nameLengthChars = record[nameStart + 64];
        byte nameSpace = record[nameStart + 65]; // namespace: 0=POSIX,1=Win32,2=DOS,3=Win32&DOS

        int nameBytesStart = nameStart + 66;
        int nameByteLength = nameLengthChars * 2;
        if (nameBytesStart + nameByteLength > record.Length) return string.Empty;

        return Encoding.Unicode.GetString(record, nameBytesStart, nameByteLength);
    }

    /// <summary>
    /// NTFS protects each 512-byte sector of a record with a 2-byte
    /// "update sequence" pattern; the last 2 bytes of every sector are
    /// swapped out for it and must be restored before the record is valid.
    /// </summary>
    private static byte[] ApplyFixup(byte[] raw, int bytesPerSector)
    {
        var record = (byte[])raw.Clone();
        ushort usaOffset = BitConverter.ToUInt16(record, 4);
        ushort usaCount = BitConverter.ToUInt16(record, 6); // includes the USN itself

        if (usaOffset == 0 || usaCount < 1) return record;

        ushort updateSequenceNumber = BitConverter.ToUInt16(record, usaOffset);

        for (int i = 1; i < usaCount; i++)
        {
            int sectorEnd = i * bytesPerSector;
            if (sectorEnd + 2 > record.Length) break;

            int checkPos = sectorEnd - 2;
            ushort storedBytes = BitConverter.ToUInt16(record, checkPos);
            // storedBytes should equal updateSequenceNumber in a healthy
            // record; we restore regardless, matching common carving tools.

            int usaEntryPos = usaOffset + i * 2;
            if (usaEntryPos + 2 > record.Length) break;

            record[checkPos] = record[usaEntryPos];
            record[checkPos + 1] = record[usaEntryPos + 1];
        }

        return record;
    }
}
