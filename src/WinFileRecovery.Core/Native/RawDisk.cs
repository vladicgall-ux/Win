using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WinFileRecovery.Core.Native;

/// <summary>
/// Opens a physical drive or volume ("\\.\PhysicalDrive0", "\\.\C:") for raw,
/// unbuffered, sector-aligned reading. Requires an elevated (administrator)
/// process; CreateFile fails with ERROR_ACCESS_DENIED otherwise.
/// </summary>
public sealed class RawDisk : IDisposable
{
    public const int DefaultSectorSize = 512;

    private readonly SafeFileHandle _handle;

    public string DevicePath { get; }
    public int SectorSize { get; }
    public long LengthBytes { get; }

    /// <summary>Total sectors on the device, or -1 if the length could not be determined (IOCTL failed).</summary>
    public long TotalSectors => LengthBytes < 0 ? -1 : LengthBytes / SectorSize;

    private RawDisk(string devicePath, SafeFileHandle handle, int sectorSize, long lengthBytes)
    {
        DevicePath = devicePath;
        _handle = handle;
        SectorSize = sectorSize;
        LengthBytes = lengthBytes;
    }

    public static RawDisk Open(string devicePath, int sectorSize = DefaultSectorSize)
    {
        var handle = NativeMethods.CreateFileW(
            devicePath,
            NativeMethods.GENERIC_READ,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            IntPtr.Zero,
            NativeMethods.OPEN_EXISTING,
            NativeMethods.FILE_FLAG_NO_BUFFERING | NativeMethods.FILE_FLAG_SEQUENTIAL_SCAN,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            throw new Win32Exception(error,
                $"Не удалось открыть {devicePath}. Убедитесь, что приложение запущено от имени администратора. (код {error})");
        }

        long length = QueryLength(handle);
        return new RawDisk(devicePath, handle, sectorSize, length);
    }

    private static long QueryLength(SafeFileHandle handle)
    {
        IntPtr buffer = Marshal.AllocHGlobal(8);
        try
        {
            bool ok = NativeMethods.DeviceIoControl(
                handle,
                NativeMethods.IOCTL_DISK_GET_LENGTH_INFO,
                IntPtr.Zero, 0,
                buffer, 8,
                out _, IntPtr.Zero);

            return ok ? Marshal.ReadInt64(buffer) : -1;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// Reads <paramref name="sectorCount"/> sectors starting at
    /// <paramref name="startSector"/>. Both the file offset and the in-memory
    /// buffer must be sector-aligned because the handle was opened with
    /// FILE_FLAG_NO_BUFFERING.
    ///
    /// All callers ultimately derive startSector/sectorCount from values
    /// parsed out of on-disk filesystem metadata, which a corrupt or
    /// malicious volume can set to anything — so every argument is treated
    /// as untrusted input and validated here, not just at the call sites.
    /// </summary>
    public byte[] ReadSectors(long startSector, int sectorCount)
    {
        if (startSector < 0)
            throw new ArgumentOutOfRangeException(nameof(startSector), "Начальный сектор не может быть отрицательным.");
        if (sectorCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(sectorCount), "Количество секторов должно быть положительным.");
        if (SectorSize <= 0)
            throw new InvalidOperationException("Некорректный размер сектора.");

        // Subtraction form (not startSector + sectorCount > TotalSectors) so
        // a huge sectorCount can't wrap the sum around and slip past the check.
        if (TotalSectors >= 0 && (startSector > TotalSectors || sectorCount > TotalSectors - startSector))
            throw new ArgumentOutOfRangeException(nameof(sectorCount),
                "Запрошенный диапазон чтения выходит за пределы диска.");

        long offsetLong = startSector * (long)SectorSize;
        long bytesToReadLong = (long)sectorCount * SectorSize;
        if (offsetLong < 0 || bytesToReadLong <= 0 || bytesToReadLong > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(sectorCount), "Слишком большой запрос на чтение.");

        long offset = offsetLong;
        int bytesToRead = (int)bytesToReadLong;

        if (!NativeMethods.SetFilePointerEx(_handle, offset, out _, NativeMethods.FILE_BEGIN))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetFilePointerEx failed");

        using var buffer = new AlignedBuffer(bytesToRead, SectorSize);

        bool ok = ReadFileRaw(_handle, buffer.Pointer, (uint)bytesToRead, out uint read, IntPtr.Zero);
        if (!ok)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"ReadFile failed at sector {startSector}");

        return buffer.ToManagedArray((int)read);
    }

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "ReadFile")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadFileRaw(
        SafeFileHandle hFile,
        IntPtr lpBuffer,
        uint nNumberOfBytesToRead,
        out uint lpNumberOfBytesRead,
        IntPtr lpOverlapped);

    public void Dispose() => _handle.Dispose();
}
