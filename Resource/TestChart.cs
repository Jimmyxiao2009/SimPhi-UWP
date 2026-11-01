using System;
using System.Collections.Generic;
using SimPhi.Core;

namespace SimPhi.Resource
{
    /// <summary>
    /// 内置最小测试谱：一条居中静止判定线 + 一串下落 Tap。
    /// 仅为"先出画面"冒烟测试——自动播放会逐个判定、出打击特效、累计连击/分数，点亮整条渲染+判定管线。
    /// 真正的谱面读取（reader/zip/csv）见 [[task-7]]，到位后改用 StartChart 传入解析结果即可。
    /// </summary>
    public static class TestChart
    {
        public static Chart Build()
        {
            const int INF = 1000000000; // 1e9
            const double bpm = 120;

            var line = new JudgeLine
            {
                bpm = bpm,
                speedEvents = new List<SpeedEvent>
                {
                    new SpeedEvent { startTime = 0, endTime = INF, value = 1 }
                },
                judgeLineDisappearEvents = new List<JudgeLineEvent>
                {
                    new JudgeLineEvent { startTime = 0, endTime = INF, start = 1, end = 1, start2 = 0, end2 = 0 } // alpha 恒 1
                },
                judgeLineMoveEvents = new List<JudgeLineEvent>
                {
                    new JudgeLineEvent { startTime = 0, endTime = INF, start = 0.5, end = 0.5, start2 = 0.5, end2 = 0.5 } // 屏幕中心
                },
                judgeLineRotateEvents = new List<JudgeLineEvent>
                {
                    new JudgeLineEvent { startTime = 0, endTime = INF, start = 0, end = 0, start2 = 0, end2 = 0 } // 水平
                },
                notesAbove = new List<Note>(),
                notesBelow = new List<Note>()
            };

            double[] xs = { -6, -3, 0, 3, 6 };
            int idx = 0;
            for (double sec = 2.0; sec <= 12.0; sec += 0.5)
            {
                line.notesAbove.Add(new Note
                {
                    type = 1,                              // Tap
                    time = (int)Math.Round(sec * 64),      // seconds = time*1.875/bpm；bpm120 → 1s=64
                    positionX = xs[idx % xs.Length],
                    holdTime = 0,
                    speed = 1,
                    floorPosition = sec                    // value=1 时 floorPosition 即 seconds，恰好在 sec 时过线
                });
                idx++;
            }
            line.numOfNotesAbove = line.notesAbove.Count;
            line.numOfNotesBelow = 0;
            line.numOfNotes = line.notesAbove.Count;

            var chart = new Chart { formatVersion = 3, offset = 0 };
            chart.judgeLineList.Add(line);
            chart.numOfNotes = line.numOfNotes;
            return chart;
        }
    }
}
