using System;
using System.Collections.Generic;

namespace SimPhi.Pec
{
    /// <summary>照抄 @sim-phi/extends src/pec/BpmList.ts：beat → PGS time（baseBpm*32 单位）。</summary>
    public sealed class BpmList
    {
        private struct Ev { public double start, end, bpm, value; }

        public double baseBpm;
        private double accTime;
        private readonly List<Ev> list = new List<Ev>();

        public BpmList(double baseBpmIn)
        {
            baseBpm = double.IsNaN(baseBpmIn) || baseBpmIn == 0 ? 120 : baseBpmIn; // Number(x)||120
            accTime = 0;
        }

        public void Push(double start, double end, double bpm)
        {
            double value = accTime;
            list.Add(new Ev { start = start, end = end, bpm = bpm, value = value });
            accTime += (end - start) / bpm;
        }

        public int Calc(double beat)
        {
            double time = 0;
            foreach (var i in list)
            {
                if (beat > i.end) continue;
                if (beat < i.start) break;
                time = Math.Floor(((beat - i.start) / i.bpm + i.value) * baseBpm * 32 + 0.5); // Math.round
            }
            return (int)time;
        }

        public int Calc2(double[] time)
        {
            return Calc(time[0] + time[1] / time[2]);
        }
    }
}
