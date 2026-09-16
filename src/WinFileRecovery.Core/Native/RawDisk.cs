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
    /// </summary>
    public byte[] ReadSectors(long startSector, int sectorCount)
    {
        long offset = startSector * SectorSize;
        int bytesToRead = sectorCount * SectorSize;

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
