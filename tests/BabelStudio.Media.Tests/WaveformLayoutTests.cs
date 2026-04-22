using BabelStudio.Application.Contracts;
using BabelStudio.Media.Playback;

namespace BabelStudio.Media.Tests;

public sealed class WaveformLayoutTests
{
    [Fact]
    public void Build_returns_expected_bar_marker_and_cursor_positions()
    {
        var waveform = new WaveformSummary(
            BucketCount: 4,
            SampleRate: 48000,
            ChannelCount: 1,
            DurationSeconds: 10.0,
            Peaks: [0.1f, 0.5f, 0.3f, 0.2f]);
        WaveformCanvasLayout layout = WaveformLayout.Build(
            waveform,
            [
                new WaveformSegmentBoundary(2.5, 5.0),
                new WaveformSegmentBoundary(7.5, 10.0)
            ],
            playbackPositionSeconds: 6.25,
            width: 200f,
            height: 100f);

        Assert.Equal(4, layout.Bars.Count);
        Assert.Equal(50f, layout.Bars[1].X, 3);
        Assert.Equal(25f, layout.Bars[1].TopY, 3);
        Assert.Equal(75f, layout.Bars[1].BottomY, 3);
        Assert.Equal(50f, layout.SegmentStartMarkerXs[0], 3);
        Assert.Equal(150f, layout.SegmentStartMarkerXs[1], 3);
        Assert.Equal(100f, layout.SegmentEndMarkerXs[0], 3);
        Assert.Equal(200f, layout.SegmentEndMarkerXs[1], 3);
        Assert.Equal(125f, layout.CursorX, 3);
    }
}
