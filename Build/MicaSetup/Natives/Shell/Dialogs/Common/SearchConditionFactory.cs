using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;

namespace MicaSetup.Shell.Dialogs;

public static class SearchConditionFactory
{
    public static SearchCondition CreateAndOrCondition(SearchConditionType conditionType, bool simplify, params SearchCondition[] conditionNodes)
    {
        var nativeConditionFactory = (IConditionFactory)new ConditionFactoryCoClass();
        ICondition result = null!;

        try
        {
            var conditionList = new List<ICondition>();
            if (conditionNodes != null)
            {
                foreach (var c in conditionNodes)
                {
                    conditionList.Add(c.NativeSearchCondition);
                }
            }

            IEnumUnknown subConditions = new EnumUnknownClass(conditionList.ToArray());

            var hr = nativeConditionFactory.MakeAndOr(conditionType, subConditions, simplify, out result);

            if (!CoreErrorHelper.Succeeded(hr)) { throw new ShellException(hr); }
        }
        finally
        {
            if (nativeConditionFactory != null)
            {
                Marshal.ReleaseComObject(nativeConditionFactory);
            }
        }

        return new SearchCondition(result);
    }

    public static SearchCondition CreateLeafCondition(string propertyName, string value, SearchConditionOperation operation)
    {
        using (var propVar = new PropVariant(value))
        {
            return CreateLeafCondition(propertyName, propVar, null!, operation);
        }
    }

    public static SearchCondition CreateLeafCondition(string propertyName, DateTime value, SearchConditionOperation operation)
    {
        using (var propVar = new PropVariant(value))
        {
            return CreateLeafCondition(propertyName, propVar, "System.StructuredQuery.CustomProperty.DateTime", operation);
        }
    }

    public static SearchCondition CreateLeafCondition(string propertyName, int value, SearchConditionOperation operation)
    {
        using (var propVar = new PropVariant(value))
        {
            return CreateLeafCondition(propertyName, propVar, "System.StructuredQuery.CustomProperty.Integer", operation);
        }
    }

    public static SearchCondition CreateLeafCondition(string propertyName, bool value, SearchConditionOperation operation)
    {
        using (var propVar = new PropVariant(value))
        {
            return CreateLeafCondition(propertyName, propVar, "System.StructuredQuery.CustomProperty.Boolean", operation);
        }
    }

    public static SearchCondition CreateLeafCondition(string propertyName, double value, SearchConditionOperation operation)
    {
        using (var propVar = new PropVariant(value))
        {
            return CreateLeafCondition(propertyName, propVar, "System.StructuredQuery.CustomProperty.FloatingPoint", operation);
        }
    }

    public static SearchCondition CreateLeafCondition(PropertyKey propertyKey, string value, SearchConditionOperation operation)
    {
        PropertySystemNativeMethods.PSGetNameFromPropertyKey(ref propertyKey, out var canonicalName);

        if (string.IsNullOrEmpty(canonicalName))
        {
            throw new ArgumentException(LocalizedMessages.SearchConditionFactoryInvalidProperty, "propertyKey");
        }

        return CreateLeafCondition(canonicalName, value, operation);
    }

    public static SearchCondition CreateLeafCondition(PropertyKey propertyKey, DateTime value, SearchConditionOperation operation)
    {
        PropertySystemNativeMethods.PSGetNameFromPropertyKey(ref propertyKey, out var canonicalName);

        if (string.IsNullOrEmpty(canonicalName))
        {
            throw new ArgumentException(LocalizedMessages.SearchConditionFactoryInvalidProperty, "propertyKey");
        }
        return CreateLeafCondition(canonicalName, value, operation);
    }

    public static SearchCondition CreateLeafCondition(PropertyKey propertyKey, bool value, SearchConditionOperation operation)
    {
        PropertySystemNativeMethods.PSGetNameFromPropertyKey(ref propertyKey, out var canonicalName);

        if (string.IsNullOrEmpty(canonicalName))
        {
            throw new ArgumentException(LocalizedMessages.SearchConditionFactoryInvalidProperty, "propertyKey");
        }
        return CreateLeafCondition(canonicalName, value, operation);
    }

    public static SearchCondition CreateLeafCondition(PropertyKey propertyKey, double value, SearchConditionOperation operation)
    {
        PropertySystemNativeMethods.PSGetNameFromPropertyKey(ref propertyKey, out var canonicalName);

        if (string.IsNullOrEmpty(canonicalName))
        {
            throw new ArgumentException(LocalizedMessages.SearchConditionFactoryInvalidProperty, "propertyKey");
        }
        return CreateLeafCondition(canonicalName, value, operation);
    }

    public static SearchCondition CreateLeafCondition(PropertyKey propertyKey, int value, SearchConditionOperation operation)
    {
        PropertySystemNativeMethods.PSGetNameFromPropertyKey(ref propertyKey, out var canonicalName);

        if (string.IsNullOrEmpty(canonicalName))
        {
            throw new ArgumentException(LocalizedMessages.SearchConditionFactoryInvalidProperty, "propertyKey");
        }
        return CreateLeafCondition(canonicalName, value, operation);
    }

    public static SearchCondition CreateNotCondition(SearchCondition conditionToBeNegated, bool simplify)
    {
        if (conditionToBeNegated == null)
        {
            throw new ArgumentNullException("conditionToBeNegated");
        }

        var nativeConditionFactory = (IConditionFactory)new ConditionFactoryCoClass();
        ICondition result;

        try
        {
            var hr = nativeConditionFactory.MakeNot(conditionToBeNegated.NativeSearchCondition, simplify, out result);

            if (!CoreErrorHelper.Succeeded(hr)) { throw new ShellException(hr); }
        }
        finally
        {
            if (nativeConditionFactory != null)
            {
                Marshal.ReleaseComObject(nativeConditionFactory);
            }
        }

        return new SearchCondition(result);
    }

    public static SearchCondition ParseStructuredQuery(string query) => ParseStructuredQuery(query, null!);

    public static SearchCondition ParseStructuredQuery(string query, CultureInfo cultureInfo)
    {
        if (string.IsNullOrEmpty(query))
        {
            throw new ArgumentNullException("query");
        }

        var nativeQueryParserManager = (IQueryParserManager)new QueryParserManagerCoClass();
        IQueryParser queryParser = null!;
        IQuerySolution querySolution = null!;
        ICondition result = null!;

        IEntity mainType = null!;
        SearchCondition searchCondition = null!;
        try
        {
            var guid = new Guid(ShellIIDGuid.IQueryParser);
            var hr = nativeQueryParserManager.CreateLoadedParser(
                "SystemIndex",
                cultureInfo == null ? (ushort)0 : (ushort)cultureInfo.LCID,
                ref guid,
                out queryParser);

            if (!CoreErrorHelper.Succeeded(hr)) { throw new ShellException(hr); }

            if (queryParser != null)
            {
                using (var optionValue = new PropVariant(true))
                {
                    hr = queryParser.SetOption(StructuredQuerySingleOption.NaturalSyntax, optionValue);
                }

                if (!CoreErrorHelper.Succeeded(hr)) { throw new ShellException(hr); }

                hr = queryParser.Parse(query, null!, out querySolution);

                if (!CoreErrorHelper.Succeeded(hr)) { throw new ShellException(hr); }

                if (querySolution != null)
                {
                    hr = querySolution.GetQuery(out result, out mainType);

                    if (!CoreErrorHelper.Succeeded(hr)) { throw new ShellException(hr); }
                }
            }

            searchCondition = new SearchCondition(result);
            return searchCondition;
        }
        catch
        {
            if (searchCondition != null) { searchCondition.Dispose(); }
            throw;
        }
        finally
        {
            if (nativeQueryParserManager != null)
            {
                Marshal.ReleaseComObject(nativeQueryParserManager);
            }

            if (queryParser != null)
            {
                Marshal.ReleaseComObject(queryParser);
            }

            if (querySolution != null)
            {
                Marshal.ReleaseComObject(querySolution);
            }

            if (mainType != null)
            {
                Marshal.ReleaseComObject(mainType);
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
    private static SearchCondition CreateLeafCondition(string propertyName, PropVariant propVar, string valueType, SearchConditionOperation operation)
    {
        IConditionFactory nativeConditionFactory = null!;
        SearchCondition condition = null!;

        try
        {
            nativeConditionFactory = (IConditionFactory)new ConditionFactoryCoClass();

            if (string.IsNullOrEmpty(propertyName) || propertyName.ToUpperInvariant() == "SYSTEM.NULL")
            {
                propertyName = null!;
            }

            var hr = HResult.Fail;

            hr = nativeConditionFactory.MakeLeaf(propertyName, operation, valueType,
                propVar, null!, null!, null!, false, out var nativeCondition);

            if (!CoreErrorHelper.Succeeded(hr))
            {
                throw new ShellException(hr);
            }

            condition = new SearchCondition(nativeCondition);
        }
        finally
        {
            if (nativeConditionFactory != null)
            {
                Marshal.ReleaseComObject(nativeConditionFactory);
            }
        }

        return condition;
    }
}
