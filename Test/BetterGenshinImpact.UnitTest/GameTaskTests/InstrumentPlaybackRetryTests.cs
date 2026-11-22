using BetterGenshinImpact.GameTask.Music.Service;

namespace BetterGenshinImpact.UnitTest.GameTaskTests;

public class InstrumentPlaybackRetryTests
{
    [Fact]
    public void OnlyAnActuallySentFirstNoteConsumesTheRetry()
    {
        var playback = new DurinPlayback();
        var sent = new List<char>();
        var neutral = new ScoreReading(true, true, 0, 0);
        var red = neutral with { Red = 1 };
        uint firstCue = 1u << DurinPlayback.KeyLayout.IndexOf('H');

        playback.Observe(neutral, 0, sent.Add);
        playback.Observe(neutral, firstCue, sent.Add);
        playback.Observe(red, 0, sent.Add);
        playback.Observe(neutral, 0, sent.Add);
        playback.Observe(neutral, firstCue, sent.Add, _ => false);
        Assert.False(playback.AwaitingHit);
        Assert.Equal(new[] { 'H' }, sent);

        playback.Observe(red, 0, sent.Add);
        Assert.False(playback.Stopped);
        playback.Observe(neutral, 0, sent.Add);
        playback.Observe(neutral, firstCue, sent.Add, _ => true);
        Assert.True(playback.AwaitingHit);
        Assert.Equal(new[] { 'H', 'H' }, sent);

        playback.Observe(red, 0, sent.Add);
        Assert.True(playback.Stopped);
        playback.Observe(neutral, 0, sent.Add);
        playback.Observe(neutral, firstCue, sent.Add);
        Assert.Equal(2, sent.Count);
    }
}
