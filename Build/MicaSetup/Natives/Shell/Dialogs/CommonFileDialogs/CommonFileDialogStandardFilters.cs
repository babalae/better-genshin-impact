namespace MicaSetup.Shell.Dialogs;

#pragma warning disable CS8618

public static class CommonFileDialogStandardFilters
{
    private static CommonFileDialogFilter officeFilesFilter;
    private static CommonFileDialogFilter pictureFilesFilter;
    private static CommonFileDialogFilter textFilesFilter;

    public static CommonFileDialogFilter OfficeFiles
    {
        get
        {
            if (officeFilesFilter == null)
            {
                officeFilesFilter = new CommonFileDialogFilter(LocalizedMessages.CommonFiltersOffice,
                    "*.doc, *.docx, *.xls, *.xlsx, *.ppt, *.pptx");
            }
            return officeFilesFilter;
        }
    }

    public static CommonFileDialogFilter PictureFiles
    {
        get
        {
            if (pictureFilesFilter == null)
            {
                pictureFilesFilter = new CommonFileDialogFilter(LocalizedMessages.CommonFiltersPicture,
                    "*.bmp, *.jpg, *.jpeg, *.png, *.ico");
            }
            return pictureFilesFilter;
        }
    }

    public static CommonFileDialogFilter TextFiles
    {
        get
        {
            if (textFilesFilter == null)
            {
                textFilesFilter = new CommonFileDialogFilter(LocalizedMessages.CommonFiltersText, "*.txt");
            }
            return textFilesFilter;
        }
    }
}
