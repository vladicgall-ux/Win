using System.Management;

namespace WinFileRecovery.Core.Disks;

/// <summary>
/// Lists physical drives and volumes via WMI so the UI can offer scan
/// targets without needing raw access just to enumerate them.
/// </summary>
public static class DriveEnumerator
{
    public static IReadOnlyList<PhysicalDrive> ListPhysicalDrives()
    {
        var results = new List<PhysicalDrive>();
        using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");
        foreach (ManagementObject disk in searcher.Get())
        {
            int index = Convert.ToInt32(disk["Index"]);
            results.Add(new PhysicalDrive(
                DevicePath: $@"\\.\PhysicalDrive{index}",
                Index: index,
                Model: disk["Model"]?.ToString() ?? "Unknown",
                SizeBytes: disk["Size"] is null ? 0 : Convert.ToInt64(disk["Size"]),
                MediaType: disk["MediaType"]?.ToString() ?? "Unknown"));
        }
        return results;
    }

    public static IReadOnlyList<Volume> ListVolumes()
    {
        var results = new List<Volume>();
        using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_LogicalDisk WHERE DriveType=3");
        foreach (ManagementObject vol in searcher.Get())
        {
            string letter = vol["DeviceID"]?.ToString() ?? "";
            results.Add(new Volume(
                DevicePath: $@"\\.\{letter}",
                DriveLetter: letter,
                FileSystem: vol["FileSystem"]?.ToString() ?? "Unknown",
                SizeBytes: vol["Size"] is null ? 0 : Convert.ToInt64(vol["Size"]),
                Label: vol["VolumeName"]?.ToString() ?? ""));
        }
        return results;
    }
}
