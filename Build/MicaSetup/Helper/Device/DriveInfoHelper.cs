using System.IO;

namespace MicaSetup.Helper;

public static class DriveInfoHelper
{
    public static long GetAvailableFreeSpace(string path)
    {
        string driveName = Path.GetPathRoot(path);
        DriveInfo driveInfo = new(driveName);
        long availableSpace = driveInfo.AvailableFreeSpace;

        return availableSpace;
    }

    public static string ToFreeSpaceString(this long freeSpace)
    {
        if (freeSpace >= 1073741824)
        {
            return $"{(double)freeSpace / 1073741824:0.##}GB";
        }
        return $"{(double)freeSpace / 1048576:0.##}MB";
    }
}
