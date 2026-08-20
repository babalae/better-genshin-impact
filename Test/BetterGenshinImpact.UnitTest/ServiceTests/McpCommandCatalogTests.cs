using BetterGenshinImpact.Service.Mcp;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace BetterGenshinImpact.UnitTest.ServiceTests;

public class McpCommandCatalogTests
{
    [Fact]
    public void TaskSettingsCommands_UsedByExplicitMcpTools_ShouldBeDiscoverable()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var catalog = new McpCommandCatalog(
            services,
            new McpCommandCatalogOptions([typeof(TaskSettingsPageViewModel)]));
        var commandNames = catalog.List(null, includeDangerous: true)
            .Select(x => x.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        string[] expected =
        [
            "task_settings_page.switch_auto_genius_invokation",
            "task_settings_page.switch_auto_wood",
            "task_settings_page.switch_auto_fight",
            "task_settings_page.switch_auto_domain",
            "task_settings_page.switch_auto_boss",
            "task_settings_page.switch_auto_stygian_onslaught",
            "task_settings_page.switch_auto_music_game",
            "task_settings_page.switch_auto_album",
            "task_settings_page.switch_auto_cook",
            "task_settings_page.switch_auto_fishing",
            "task_settings_page.switch_auto_ley_line_outcrop",
            "task_settings_page.switch_artifact_salvage",
            "task_settings_page.switch_get_grid_icons",
            "task_settings_page.switch_grid_icons_model_accuracy_test",
        ];

        Assert.All(expected, name => Assert.Contains(name, commandNames));
    }
}
