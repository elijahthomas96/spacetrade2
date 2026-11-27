#if DEBUG
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;

namespace SpaceTrade.Game.Boot
{
    /// <summary>
    /// Editor and Development Builds only: measures frames for the 50 ms rule. A frame's time is the real time from the start
    /// of one frame's first script to the next frame's; its work is the time our scripts take within the frame (the first
    /// Update to the last LateUpdate). One-off frames (building a new game, later loading a save) are recorded separately
    /// with their own time and left out of the ordinary figures, as is the frame after one.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class FrameTimeProbe : MonoBehaviour
    {
        public sealed class Segment
        {
            public string Label;
            public int Frames;
            public double SlowestFrameMs, SlowestWorkMs, TotalFrameMs;
            public double MeanFrameMs => Frames == 0 ? 0 : TotalFrameMs / Frames;
        }

        private readonly Stopwatch _watch = Stopwatch.StartNew();
        private long _lastStart = -1, _frameStart;
        private int _excludeThroughFrame;
        private Segment _current;
        private string _pendingOneOffFrame;

        public List<Segment> Segments { get; } = new List<Segment>();
        /// <summary>Each part of one-off work, as timed by the code doing it.</summary>
        public List<(string What, double Ms)> OneOffs { get; } = new List<(string, double)>();
        /// <summary>The whole real time of each frame that held one-off work, start of that frame to start of the next.</summary>
        public List<(string What, double Ms)> OneOffFrames { get; } = new List<(string, double)>();

        private void Awake()
        {
            var end = gameObject.GetComponent<FrameTimeProbeEnd>();
            if (end == null) end = gameObject.AddComponent<FrameTimeProbeEnd>();
            end.Probe = this;
        }

        public void Begin(string label)
        {
            _current = new Segment { Label = label };
            Segments.Add(_current);
            _excludeThroughFrame = Mathf.Max(_excludeThroughFrame, Time.frameCount);
        }

        public void End() => _current = null;

        public void ClearAll()
        {
            Segments.Clear();
            OneOffs.Clear();
            OneOffFrames.Clear();
            _current = null;
            _pendingOneOffFrame = null;
        }

        /// <summary>Records a one-off frame's work; that frame and the next are left out of the ordinary figures.</summary>
        public void MarkOneOff(string what, double ms)
        {
            OneOffs.Add((what, ms));
            _pendingOneOffFrame = _pendingOneOffFrame == null ? what : _pendingOneOffFrame + " + " + what;
            _excludeThroughFrame = Time.frameCount + 1;
        }

        private void Update()
        {
            long now = _watch.ElapsedTicks;
            double ms = (now - _lastStart) * 1000.0 / Stopwatch.Frequency;
            if (_pendingOneOffFrame != null)
            {
                // The first frame the probe sees has no start time, so one-off work done before it cannot be timed whole.
                if (_lastStart >= 0) OneOffFrames.Add((_pendingOneOffFrame, ms));
                _pendingOneOffFrame = null;
            }
            if (_lastStart >= 0 && _current != null && Time.frameCount > _excludeThroughFrame)
            {
                _current.Frames++;
                _current.TotalFrameMs += ms;
                if (ms > _current.SlowestFrameMs) _current.SlowestFrameMs = ms;
            }
            _lastStart = now;
            _frameStart = now;
        }

        internal void EndOfWork()
        {
            if (_current == null || Time.frameCount <= _excludeThroughFrame) return;
            double ms = (_watch.ElapsedTicks - _frameStart) * 1000.0 / Stopwatch.Frequency;
            if (ms > _current.SlowestWorkMs) _current.SlowestWorkMs = ms;
        }

        public string Report()
        {
            var sb = new StringBuilder();
            foreach (var o in OneOffs) sb.Append($"one-off {o.What}: {o.Ms:0.0} ms\n");
            foreach (var o in OneOffFrames) sb.Append($"one-off frame, whole ({o.What}): {o.Ms:0.0} ms\n");
            foreach (var s in Segments)
                sb.Append($"{s.Label}: {s.Frames} frames, slowest frame {s.SlowestFrameMs:0.0} ms, mean {s.MeanFrameMs:0.0} ms, slowest script work {s.SlowestWorkMs:0.0} ms\n");
            return sb.ToString();
        }
    }
}
#endif
