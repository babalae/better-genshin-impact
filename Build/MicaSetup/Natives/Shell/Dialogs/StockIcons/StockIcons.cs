using System;
using System.Collections.Generic;

namespace MicaSetup.Shell.Dialogs;

public class StockIcons
{
    private readonly IDictionary<StockIconIdentifier, StockIcon> stockIconCache;
    private readonly StockIconSize defaultSize = StockIconSize.Large;
    private readonly bool isSelected;
    private readonly bool isLinkOverlay;

    public StockIcons()
    {
        stockIconCache = new Dictionary<StockIconIdentifier, StockIcon>();

        var allIdentifiers = Enum.GetValues(typeof(StockIconIdentifier));

        foreach (StockIconIdentifier id in allIdentifiers)
        {
            stockIconCache.Add(id, null!);
        }
    }

    public StockIcons(StockIconSize size, bool linkOverlay, bool selected)
    {
        defaultSize = size;
        isLinkOverlay = linkOverlay;
        isSelected = selected;

        stockIconCache = new Dictionary<StockIconIdentifier, StockIcon>();

        var allIdentifiers = Enum.GetValues(typeof(StockIconIdentifier));

        foreach (StockIconIdentifier id in allIdentifiers)
        {
            stockIconCache.Add(id, null!);
        }
    }

    public StockIconSize DefaultSize => defaultSize;

    public bool DefaultLinkOverlay => isLinkOverlay;

    public bool DefaultSelectedState => isSelected;

    public ICollection<StockIcon> AllStockIcons => GetAllStockIcons();

    public StockIcon DocumentNotAssociated => GetStockIcon(StockIconIdentifier.DocumentNotAssociated);

    public StockIcon DocumentAssociated => GetStockIcon(StockIconIdentifier.DocumentAssociated);

    public StockIcon Application => GetStockIcon(StockIconIdentifier.Application);

    public StockIcon Folder => GetStockIcon(StockIconIdentifier.Folder);

    public StockIcon FolderOpen => GetStockIcon(StockIconIdentifier.FolderOpen);

    public StockIcon Drive525 => GetStockIcon(StockIconIdentifier.Drive525);

    public StockIcon Drive35 => GetStockIcon(StockIconIdentifier.Drive35);

    public StockIcon DriveRemove => GetStockIcon(StockIconIdentifier.DriveRemove);

    public StockIcon DriveFixed => GetStockIcon(StockIconIdentifier.DriveFixed);

    public StockIcon DriveNetwork => GetStockIcon(StockIconIdentifier.DriveNetwork);

    public StockIcon DriveNetworkDisabled => GetStockIcon(StockIconIdentifier.DriveNetworkDisabled);

    public StockIcon DriveCD => GetStockIcon(StockIconIdentifier.DriveCD);

    public StockIcon DriveRam => GetStockIcon(StockIconIdentifier.DriveRam);

    public StockIcon World => GetStockIcon(StockIconIdentifier.World);

    public StockIcon Server => GetStockIcon(StockIconIdentifier.Server);

    public StockIcon Printer => GetStockIcon(StockIconIdentifier.Printer);

    public StockIcon MyNetwork => GetStockIcon(StockIconIdentifier.MyNetwork);

    public StockIcon Find => GetStockIcon(StockIconIdentifier.Find);

    public StockIcon Help => GetStockIcon(StockIconIdentifier.Help);

    public StockIcon Share => GetStockIcon(StockIconIdentifier.Share);

    public StockIcon Link => GetStockIcon(StockIconIdentifier.Link);

    public StockIcon SlowFile => GetStockIcon(StockIconIdentifier.SlowFile);

    public StockIcon Recycler => GetStockIcon(StockIconIdentifier.Recycler);

    public StockIcon RecyclerFull => GetStockIcon(StockIconIdentifier.RecyclerFull);

    public StockIcon MediaCDAudio => GetStockIcon(StockIconIdentifier.MediaCDAudio);

    public StockIcon Lock => GetStockIcon(StockIconIdentifier.Lock);

    public StockIcon AutoList => GetStockIcon(StockIconIdentifier.AutoList);

    public StockIcon PrinterNet => GetStockIcon(StockIconIdentifier.PrinterNet);

    public StockIcon ServerShare => GetStockIcon(StockIconIdentifier.ServerShare);

    public StockIcon PrinterFax => GetStockIcon(StockIconIdentifier.PrinterFax);

    public StockIcon PrinterFaxNet => GetStockIcon(StockIconIdentifier.PrinterFaxNet);

    public StockIcon PrinterFile => GetStockIcon(StockIconIdentifier.PrinterFile);

    public StockIcon Stack => GetStockIcon(StockIconIdentifier.Stack);

    public StockIcon MediaSvcd => GetStockIcon(StockIconIdentifier.MediaSvcd);

    public StockIcon StuffedFolder => GetStockIcon(StockIconIdentifier.StuffedFolder);

    public StockIcon DriveUnknown => GetStockIcon(StockIconIdentifier.DriveUnknown);

    public StockIcon DriveDvd => GetStockIcon(StockIconIdentifier.DriveDvd);

    public StockIcon MediaDvd => GetStockIcon(StockIconIdentifier.MediaDvd);

    public StockIcon MediaDvdRam => GetStockIcon(StockIconIdentifier.MediaDvdRam);

    public StockIcon MediaDvdRW => GetStockIcon(StockIconIdentifier.MediaDvdRW);

    public StockIcon MediaDvdR => GetStockIcon(StockIconIdentifier.MediaDvdR);

    public StockIcon MediaDvdRom => GetStockIcon(StockIconIdentifier.MediaDvdRom);

    public StockIcon MediaCDAudioPlus => GetStockIcon(StockIconIdentifier.MediaCDAudioPlus);

    public StockIcon MediaCDRW => GetStockIcon(StockIconIdentifier.MediaCDRW);

    public StockIcon MediaCDR => GetStockIcon(StockIconIdentifier.MediaCDR);

    public StockIcon MediaCDBurn => GetStockIcon(StockIconIdentifier.MediaCDBurn);

    public StockIcon MediaBlankCD => GetStockIcon(StockIconIdentifier.MediaBlankCD);

    public StockIcon MediaCDRom => GetStockIcon(StockIconIdentifier.MediaCDRom);

    public StockIcon AudioFiles => GetStockIcon(StockIconIdentifier.AudioFiles);

    public StockIcon ImageFiles => GetStockIcon(StockIconIdentifier.ImageFiles);

    public StockIcon VideoFiles => GetStockIcon(StockIconIdentifier.VideoFiles);

    public StockIcon MixedFiles => GetStockIcon(StockIconIdentifier.MixedFiles);

    public StockIcon FolderBack => GetStockIcon(StockIconIdentifier.FolderBack);

    public StockIcon FolderFront => GetStockIcon(StockIconIdentifier.FolderFront);

    public StockIcon Shield => GetStockIcon(StockIconIdentifier.Shield);

    public StockIcon Warning => GetStockIcon(StockIconIdentifier.Warning);

    public StockIcon Info => GetStockIcon(StockIconIdentifier.Info);

    public StockIcon Error => GetStockIcon(StockIconIdentifier.Error);

    public StockIcon Key => GetStockIcon(StockIconIdentifier.Key);

    public StockIcon Software => GetStockIcon(StockIconIdentifier.Software);

    public StockIcon Rename => GetStockIcon(StockIconIdentifier.Rename);

    public StockIcon Delete => GetStockIcon(StockIconIdentifier.Delete);

    public StockIcon MediaAudioDvd => GetStockIcon(StockIconIdentifier.MediaAudioDvd);

    public StockIcon MediaMovieDvd => GetStockIcon(StockIconIdentifier.MediaMovieDvd);

    public StockIcon MediaEnhancedCD => GetStockIcon(StockIconIdentifier.MediaEnhancedCD);

    public StockIcon MediaEnhancedDvd => GetStockIcon(StockIconIdentifier.MediaEnhancedDvd);

    public StockIcon MediaHDDvd => GetStockIcon(StockIconIdentifier.MediaHDDvd);

    public StockIcon MediaBluRay => GetStockIcon(StockIconIdentifier.MediaBluRay);

    public StockIcon MediaVcd => GetStockIcon(StockIconIdentifier.MediaVcd);

    public StockIcon MediaDvdPlusR => GetStockIcon(StockIconIdentifier.MediaDvdPlusR);

    public StockIcon MediaDvdPlusRW => GetStockIcon(StockIconIdentifier.MediaDvdPlusRW);

    public StockIcon DesktopPC => GetStockIcon(StockIconIdentifier.DesktopPC);

    public StockIcon MobilePC => GetStockIcon(StockIconIdentifier.MobilePC);

    public StockIcon Users => GetStockIcon(StockIconIdentifier.Users);

    public StockIcon MediaSmartMedia => GetStockIcon(StockIconIdentifier.MediaSmartMedia);

    public StockIcon MediaCompactFlash => GetStockIcon(StockIconIdentifier.MediaCompactFlash);

    public StockIcon DeviceCellPhone => GetStockIcon(StockIconIdentifier.DeviceCellPhone);

    public StockIcon DeviceCamera => GetStockIcon(StockIconIdentifier.DeviceCamera);

    public StockIcon DeviceVideoCamera => GetStockIcon(StockIconIdentifier.DeviceVideoCamera);

    public StockIcon DeviceAudioPlayer => GetStockIcon(StockIconIdentifier.DeviceAudioPlayer);

    public StockIcon NetworkConnect => GetStockIcon(StockIconIdentifier.NetworkConnect);

    public StockIcon Internet => GetStockIcon(StockIconIdentifier.Internet);

    public StockIcon ZipFile => GetStockIcon(StockIconIdentifier.ZipFile);

    public StockIcon Settings => GetStockIcon(StockIconIdentifier.Settings);

    public StockIcon DriveHDDVD => GetStockIcon(StockIconIdentifier.DriveHDDVD);

    public StockIcon DriveBluRay => GetStockIcon(StockIconIdentifier.DriveBluRay);

    public StockIcon MediaHDDVDROM => GetStockIcon(StockIconIdentifier.MediaHDDVDROM);

    public StockIcon MediaHDDVDR => GetStockIcon(StockIconIdentifier.MediaHDDVDR);

    public StockIcon MediaHDDVDRAM => GetStockIcon(StockIconIdentifier.MediaHDDVDRAM);

    public StockIcon MediaBluRayROM => GetStockIcon(StockIconIdentifier.MediaBluRayROM);

    public StockIcon MediaBluRayR => GetStockIcon(StockIconIdentifier.MediaBluRayR);

    public StockIcon MediaBluRayRE => GetStockIcon(StockIconIdentifier.MediaBluRayRE);

    public StockIcon ClusteredDisk => GetStockIcon(StockIconIdentifier.ClusteredDisk);

    private StockIcon GetStockIcon(StockIconIdentifier stockIconIdentifier)
    {
        if (stockIconCache[stockIconIdentifier] != null)
        {
            return stockIconCache[stockIconIdentifier];
        }
        else
        {
            var icon = new StockIcon(stockIconIdentifier, defaultSize, isLinkOverlay, isSelected);

            try
            {
                stockIconCache[stockIconIdentifier] = icon;
            }
            catch
            {
                icon.Dispose();
                throw;
            }
            return icon;
        }
    }

    private ICollection<StockIcon> GetAllStockIcons()
    {
        var ids = new StockIconIdentifier[stockIconCache.Count];
        stockIconCache.Keys.CopyTo(ids, 0);

        foreach (var id in ids)
        {
            if (stockIconCache[id] == null)
                _ = GetStockIcon(id);
        }

        return stockIconCache.Values;
    }
}
