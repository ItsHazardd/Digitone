using System;
using System.Collections.Generic;
using System.Linq;

namespace Digitone
{
    // Listens to the low end of the music and keeps time like a lighting desk: kicks, sub-bass, tempo, beats, bars, phrases,
    // builds and drops. The visualizers move on these instead of on raw sound, so they feel like a set-up show, not noise.
    internal sealed class RaveClock
    {
        internal double Kick;      // 1 when a kick lands, fading to 0 over about a fifth of a second
        internal bool KickNow;     // a kick landed on this update
        internal int Kicks;        // kicks heard since the song (or seek) started
        internal double Bass;      // how strong the low end is right now (0..1, relative to this song)
        internal double Sub;       // the sustained low end: 808s and bass lines (smoothed, 0..1)
        internal double Bpm;       // tempo once it is known (0 before that)
        internal double BeatPhase; // 0..1 through the current beat
        internal int Beat;         // beats counted
        internal bool BeatNow;     // the beat counter moved on this update
        internal int Bar { get { return Beat / 4; } }
        internal int Phrase { get { return Beat / 32; } } // 8 bars
        internal double Build;     // tension before a drop (0..1)
        internal double Drop;      // 1 when the drop hits, fading over about a second
        internal bool DropNow;

        private double time, lastKickAt = -10, slowBass, peak = .004, beatLength = .5, midBass, songBass, thinSince = -1, lastDropAt = -100, lastPosition = -1;
        private readonly List<double> kickTimes = new List<double>();

        internal void Reset()
        {
            Kick = Bass = Sub = Bpm = BeatPhase = Build = Drop = 0; Kicks = Beat = 0; KickNow = BeatNow = DropNow = false;
            lastKickAt = -10; slowBass = 0; peak = .004; beatLength = .5; midBass = songBass = 0; thinSince = -1; lastDropAt = -100; kickTimes.Clear();
        }

        // seconds: time since the last update. bassShort/bassLong: low-end loudness over ~20 ms and ~85 ms. position: song position (a jump means a seek or a new song).
        internal void Feed(double seconds, double bassShort, double bassLong, double position, bool playing)
        {
            KickNow = BeatNow = DropNow = false; seconds = Math.Max(0, Math.Min(.1, seconds)); time += seconds;
            if (playing && lastPosition >= 0 && (position < lastPosition - .5 || position > lastPosition + 2)) Reset();
            if (playing) lastPosition = position;
            Kick *= Math.Exp(-seconds / .11); Drop *= Math.Exp(-seconds / .9);
            if (!playing) { Sub *= Math.Exp(-seconds / .4); Bass *= Math.Exp(-seconds / .2); Build *= Math.Exp(-seconds / 1); return; }

            peak = Math.Max(Math.Max(peak * Math.Exp(-seconds / 8), .004), Math.Max(bassLong, bassShort * .8));
            double level = Math.Min(1, bassShort / peak), sustained = Math.Min(1, bassLong / peak);
            double previous = Bass; Bass = level;
            Sub += (sustained - Sub) * (1 - Math.Exp(-seconds / (sustained > Sub ? .08 : .35)));

            // A kick: the low end jumps well above where it has been, and is jumping right now (a held bass note is not a stream of kicks).
            double before = slowBass; slowBass += (level - slowBass) * (1 - Math.Exp(-seconds / .25));
            if (level > before * 1.35 + .12 && level > previous * 1.15 + .05 && level > .35 && time - lastKickAt > .2)
            {
                KickNow = true; Kick = 1; Kicks++; lastKickAt = time;
                kickTimes.Add(time); while (kickTimes.Count > 24 || kickTimes.Count > 0 && kickTimes[0] < time - 12) kickTimes.RemoveAt(0);
                LearnTempo();
                if (Bpm <= 0) { BeatPhase = 0; Beat++; BeatNow = true; } // no steady tempo yet: every kick is a beat
                else { if (BeatPhase < .25) BeatPhase -= BeatPhase * .5; else if (BeatPhase > .75) BeatPhase += (1 - BeatPhase) * .5; } // stay locked to the kicks
            }

            if (Bpm > 0) { BeatPhase += seconds / beatLength; while (BeatPhase >= 1) { BeatPhase -= 1; Beat++; BeatNow = true; } }

            // Build and drop: the low end thins out for a while (a build), then a big kick lands (the drop).
            // (raw loudness, not scaled to the song: the last second's low end against the song's usual low end)
            midBass += (bassLong - midBass) * (1 - Math.Exp(-seconds / 1.2)); songBass += (bassLong - songBass) * (1 - Math.Exp(-seconds / 25));
            bool thin = songBass > 1e-4 && midBass < songBass * .5;
            if (thin) { if (thinSince < 0) thinSince = time; Build = Math.Min(1, (time - thinSince) / 6); }
            if (Build > .3 && KickNow && level > .65 && time - lastDropAt > 8) { DropNow = true; Drop = 1; Build = 0; thinSince = -1; lastDropAt = time; }
            else if (!thin) { thinSince = -1; Build *= Math.Exp(-seconds / 1.5); }
        }

        // Tempo from the gaps between kicks, folded into 70-180 BPM (so off-beats and double-time kicks still agree).
        private void LearnTempo()
        {
            if (kickTimes.Count < 5) return;
            var gaps = new List<double>();
            for (int i = 1; i < kickTimes.Count; i++) { double gap = kickTimes[i] - kickTimes[i - 1]; if (gap <= 0) continue; while (gap < 60 / 180.0) gap *= 2; while (gap > 60 / 70.0) gap /= 2; gaps.Add(gap); }
            if (gaps.Count < 4) return;
            gaps.Sort(); double median = gaps[gaps.Count / 2];
            int agree = gaps.Count(g => Math.Abs(g - median) < median * .08);
            if (agree < gaps.Count * .5) return; // no steady tempo: keep what we had
            beatLength = Bpm > 0 ? beatLength + (median - beatLength) * .5 : median; Bpm = 60 / beatLength;
        }
    }

    public sealed partial class PlayerApp
    {
        private readonly RaveClock rave = new RaveClock();
        private readonly System.Diagnostics.Stopwatch raveWatch = System.Diagnostics.Stopwatch.StartNew(); private double raveLast;
        internal RaveClock Rave { get { return rave; } }
        // Called by both the live waveform and Beta every frame; only the first call in a frame counts.
        private void FeedRave()
        {
            double now = raveWatch.Elapsed.TotalSeconds, dt = now - raveLast; if (dt < .002) return; raveLast = now;
            double position = ready ? media.Position.TotalSeconds : 0; double[] low = playing && ready && liveWave != null ? liveWave.SampleLow(position) : null;
            rave.Feed(dt, low == null ? 0 : low[0], low == null ? 0 : low[1], position, low != null);
        }
    }
}
