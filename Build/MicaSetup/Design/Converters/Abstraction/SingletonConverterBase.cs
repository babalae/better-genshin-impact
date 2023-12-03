using System;
using System.Threading;

namespace MicaSetup.Design.Converters;

public abstract class SingletonConverterBase<TConverter> : ConverterBase
    where TConverter : new()
{
    private static readonly Lazy<TConverter> InstanceConstructor = new(() =>
    {
        return new TConverter();
    }, LazyThreadSafetyMode.PublicationOnly);

    public static TConverter Instance => InstanceConstructor.Value;
}
