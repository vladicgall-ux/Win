using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using WinFileRecovery.Core.Native;

namespace WinFileRecovery.Core.Disks;

/// <summary>
/// Resolves a filesystem path (e.g. a folder the user picked for recovered
/// output) to the physical disk index that actually backs it, via
/// IOCTL_STORAGE_GET_DEVICE_NUMBER on its volume. This is the only reliable
/// way to do it: a drive letter alone doesn't guarantee which physical disk
/// it lives on (dynamic disks, mount points, multiple partitions on one
/// disk all break a naive "different letter = different disk" assumption).
/// </summary>
public static class DiskIdentity
{
    /// <summary>Returns the physical disk index (as in "\\.\PhysicalDriveN") backing <paramref name="path"/>, or null if it could not be determined.</summary>
    public static int? GetPhysicalDriveNumber(string path)
    {
        string? root = Path.GetPathRoot(Path.GetFullPath(path));
        if (string.IsNullOrEmpty(root)) return null;

        string volumePath = root.TrimEnd('\\');
        if (volumePath.Length == 0) return null;

        string devicePath = $@"\\.\{volumePath}";

        using var handle = NativeMethods.CreateFileW(
            devicePath,
            0, // no data access needed, only the device-number query
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            IntPtr.Zero,
            NativeMethods.OPEN_EXISTING,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid) return null;

        int size = Marshal.SizeOf<NativeMethods.STORAGE_DEVICE_NUMBER>();
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            bool ok = NativeMethods.DeviceIoControl(
                handle,
                NativeMethods.IOCTL_STORAGE_GET_DEVICE_NUMBER,
                IntPtr.Zero, 0,
                buffer, (uint)size,
                out _, IntPtr.Zero);

            if (!ok) return null;

            var info = Marshal.PtrToStructure<NativeMethods.STORAGE_DEVICE_NUMBER>(buffer);
            return info.DeviceNumber;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// True if <paramref name="destinationPath"/> resolves to the same physical
    /// disk as <paramref name="sourcePhysicalDriveIndex"/>. When the destination's
    /// disk number can't be determined, this fails closed (returns true) —
    /// refusing an unverifiable destination is safer than risking an overwrite.
    /// </summary>
    public static bool IsSamePhysicalDisk(string destinationPath, int sourcePhysicalDriveIndex)
    {
        int? destinationDisk = GetPhysicalDriveNumber(destinationPath);
        return destinationDisk is null || destinationDisk.Value == sourcePhysicalDriveIndex;
    }
}
