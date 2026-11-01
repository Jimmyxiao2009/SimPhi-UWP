using System;
using System.Collections.Generic;
using SimPhi.Core;

namespace SimPhi.Pec
{
    /// <summary>照抄 @sim-phi/extends src/format.ts 的 normalizeSpeedEvents / normalizeLineEvents（补尾事件到 1e9）。</summary>
    public static class Format
    {
        private const int INF = 1000000000; // 1e9

        public static void NormalizeSpeedEvents(List<SpeedEvent> events)
        {
            if (events.Count != 0)
            {
                int maxEndTime = 0;
                foreach (var e in events) maxEndTime = Math.Max(maxEndTime, e.endTime);
                if (maxEndTime < INF)
                {
                    double value = events[events.Count - 1].value;
                    events.Add(new SpeedEvent { startTime = maxEndTime, endTime = INF, value = value });
                }
            }
            else
            {
                events.Add(new SpeedEvent { startTime = 0, endTime = INF, value = 1 });
            }
        }

        public static void NormalizeLineEvents(List<JudgeLineEvent> events)
        {
            if (events.Count != 0)
            {
                int maxEndTime = 0;
                foreach (var e in events) maxEndTime = Math.Max(maxEndTime, e.endTime);
                if (maxEndTime < INF)
                {
                    var last = events[events.Count - 1];
                    events.Add(new JudgeLineEvent
                    {
                        startTime = maxEndTime, endTime = INF,
                        start = last.end, end = last.end, start2 = last.end2, end2 = last.end2
                    });
                }
            }
            else
            {
                events.Add(new JudgeLineEvent { startTime = 0, endTime = INF, start = 0, end = 0, start2 = 0, end2 = 0 });
            }
        }
    }
}
