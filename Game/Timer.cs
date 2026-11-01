using System;
using SimPhi.Core;

namespace SimPhi.Game
{
    /// <summary>照抄 src/components/Timer.ts。performance.now() → Clock.Now()。</summary>
    public sealed class Timer
    {
        private double t0;
        private double t1;
        private bool isPaused;

        public Timer()
        {
            t0 = 0;
            t1 = 0;
            isPaused = true;
        }

        public double Time
        {
            get
            {
                if (isPaused) return t0;
                return t0 + Clock.Now() - t1;
            }
        }

        public double Second { get { return Time / 1e3; } }

        public void Play()
        {
            if (!isPaused) throw new InvalidOperationException("Time has been playing");
            t1 = Clock.Now();
            isPaused = false;
        }

        public void Pause()
        {
            if (isPaused) throw new InvalidOperationException("Time has been paused");
            t0 = Time;
            isPaused = true;
        }

        public void Reset()
        {
            t0 = 0;
            t1 = 0;
            isPaused = true;
        }

        public void AddTime(double num = 0)
        {
            t0 += num;
        }
    }
}
