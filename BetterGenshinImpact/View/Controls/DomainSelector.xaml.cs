using BetterGenshinImpact.GameTask.AutoTrackPath.Model;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace BetterGenshinImpact.View.Controls;

public partial class DomainSelector : UserControl
{
    public DomainSelector()
    {
        InitializeComponent();
        Countries = MapLazyAssets.Instance.CountryToDomains.Keys.Reverse().ToList();
    }

    public List<string> Countries
    {
        get { return (List<string>)GetValue(CountriesProperty); }
        set { SetValue(CountriesProperty, value); }
    }

    public static readonly DependencyProperty CountriesProperty =
        DependencyProperty.Register("Countries", typeof(List<string>), typeof(DomainSelector), new PropertyMetadata(null));

    public string SelectedCountry
    {
        get { return (string)GetValue(SelectedCountryProperty); }
        set { SetValue(SelectedCountryProperty, value); }
    }

    public static readonly DependencyProperty SelectedCountryProperty =
        DependencyProperty.Register("SelectedCountry", typeof(string), typeof(DomainSelector), new PropertyMetadata(null, OnSelectedCountryChanged));

    private static void OnSelectedCountryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (DomainSelector)d;
        var country = (string)e.NewValue;
        if (string.IsNullOrEmpty(country))
        {
            control.FilteredDomains = new List<GiTpPosition>();
        }
        else
        {
            if (MapLazyAssets.Instance.CountryToDomains.TryGetValue(country, out var domains))
            {
                // Reverse the list for display
                control.FilteredDomains = domains.AsEnumerable().Reverse().ToList();
            }
            else
            {
                control.FilteredDomains = new List<GiTpPosition>();
            }
        }
    }

    public List<GiTpPosition> FilteredDomains
    {
        get { return (List<GiTpPosition>)GetValue(FilteredDomainsProperty); }
        set { SetValue(FilteredDomainsProperty, value); }
    }

    public static readonly DependencyProperty FilteredDomainsProperty =
        DependencyProperty.Register("FilteredDomains", typeof(List<GiTpPosition>), typeof(DomainSelector), new PropertyMetadata(null));

    public string SelectedDomain
    {
        get { return (string)GetValue(SelectedDomainProperty); }
        set { SetValue(SelectedDomainProperty, value); }
    }

    public static readonly DependencyProperty SelectedDomainProperty =
        DependencyProperty.Register("SelectedDomain", typeof(string), typeof(DomainSelector), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedDomainChanged));

    private static void OnSelectedDomainChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (DomainSelector)d;
        var domain = (string)e.NewValue;
        
        if (string.IsNullOrEmpty(domain)) return;

        // Verify if domain matches current country, if not, update country
        var country = MapLazyAssets.Instance.GetCountryByDomain(domain);
        if (country != null && country != control.SelectedCountry)
        {
            control.SelectedCountry = country;
        }
    }

    private void DomainListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MainToggle.IsChecked == true)
        {
            MainToggle.IsChecked = false;
        }
    }
}
