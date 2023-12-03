using MicaSetup.Helper;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MicaSetup.Shell.Dialogs;

#pragma warning disable CS8618

public class ShellSearchFolder : ShellSearchCollection
{
    private SearchCondition searchCondition;

    private string[] searchScopePaths;

    public ShellSearchFolder(SearchCondition searchCondition, params ShellContainer[] searchScopePath)
    {
        OsVersionHelper.ThrowIfNotVista();

        NativeSearchFolderItemFactory = (ISearchFolderItemFactory)new SearchFolderItemFactoryCoClass();

        SearchCondition = searchCondition;

        if (searchScopePath != null && searchScopePath.Length > 0 && searchScopePath[0] != null!)
        {
            SearchScopePaths = searchScopePath.Select(cont => cont.ParsingName);
        }
    }

    public ShellSearchFolder(SearchCondition searchCondition, params string[] searchScopePath)
    {
        OsVersionHelper.ThrowIfNotVista();

        NativeSearchFolderItemFactory = (ISearchFolderItemFactory)new SearchFolderItemFactoryCoClass();

        if (searchScopePath != null && searchScopePath.Length > 0 && searchScopePath[0] != null)
        {
            SearchScopePaths = searchScopePath;
        }

        SearchCondition = searchCondition;
    }

    public SearchCondition SearchCondition
    {
        get => searchCondition;
        private set
        {
            searchCondition = value;

            NativeSearchFolderItemFactory.SetCondition(searchCondition.NativeSearchCondition);
        }
    }

    public IEnumerable<string> SearchScopePaths
    {
        get
        {
            foreach (var scopePath in searchScopePaths)
            {
                yield return scopePath;
            }
        }
        private set
        {
            searchScopePaths = value.ToArray();
            var shellItems = new List<IShellItem>(searchScopePaths.Length);

            var shellItemGuid = new Guid(ShellIIDGuid.IShellItem);

            foreach (var path in searchScopePaths)
            {
                var hr = Shell32.SHCreateItemFromParsingName(path, 0, ref shellItemGuid, out IShellItem scopeShellItem);

                if (CoreErrorHelper.Succeeded(hr)) { shellItems.Add(scopeShellItem); }
            }

            IShellItemArray scopeShellItemArray = new ShellItemArray(shellItems.ToArray());

            var hResult = NativeSearchFolderItemFactory.SetScope(scopeShellItemArray);

            if (!CoreErrorHelper.Succeeded((int)hResult)) { throw new ShellException((int)hResult); }
        }
    }

    internal ISearchFolderItemFactory NativeSearchFolderItemFactory { get; set; }

    internal override IShellItem NativeShellItem
    {
        get
        {
            var guid = new Guid(ShellIIDGuid.IShellItem);

            if (NativeSearchFolderItemFactory == null) { return null!; }

            var hr = NativeSearchFolderItemFactory.GetShellItem(ref guid, out var shellItem);

            if (!CoreErrorHelper.Succeeded(hr)) { throw new ShellException(hr); }

            return shellItem;
        }
    }
}
