using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Routing;
using Xunit;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.QuestVoiceTests;

/// <summary>移植回归验证：RoutingDisplayNameTests。</summary>
public sealed class RoutingDisplayNameTests
{
    /// <summary>移植回归验证：Display name is identity first standard name even when voice falls back。</summary>
    [Theory]
    [InlineData("测试别名", "甘雨", RoutingLayer.Alias)]
    [InlineData("测试错字", "甘雨", RoutingLayer.Correction)]
    [InlineData("刻晴", "刻晴", RoutingLayer.Canonical)]
    [InlineData("旅行者", "空", RoutingLayer.Protagonist)]
    public async Task Display_name_is_identity_first_standard_name_even_when_voice_falls_back(string input, string expected, RoutingLayer layer)
    {
        using var fixture = new RoutingFixture();
        var result = await fixture.ResolveAsync(new(input, SpeakerNameStatus.Accepted));
        Assert.Equal(expected, result.CanonicalName);
        Assert.Equal(layer, result.Speaker.Layer);
        if (input == "刻晴") Assert.Equal("通用声线", result.Voice.Name);
        if (input == "旅行者") Assert.Equal("甘雨", result.Voice.Name);
    }
}
