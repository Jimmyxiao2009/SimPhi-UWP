using System.Collections.Generic;

namespace SimPhi.Core
{
    // 照抄 src/utils/Chart.ts 的基础字段 + src/core.ts 里 *Extends 接口运行期追加的字段。
    // JS 是在同一对象上动态加属性；C# 不能动态加，于是把"基础 + 运行期"字段合并到同一个类里，
    // 解析阶段填基础字段，prerenderChart/updateByTime 填运行期字段，行为完全等价。

    public sealed class SpeedEvent
    {
        // 基础（Chart.ts）
        public int startTime;
        public int endTime;
        public double value;
        // 运行期（SpeedEventExtends）
        public double floorPosition;
        public double floorPosition2;
        public double startSeconds;
        public double endSeconds;

        public SpeedEvent() { }
        public SpeedEvent(IDictionary<string, object> e)
        {
            startTime = Type.Int(Type.Get(e, "startTime"));
            endTime = Type.Int(Type.Get(e, "endTime"));
            value = Type.Float(Type.Get(e, "value"));
        }
        public SpeedEvent Clone()
        {
            return new SpeedEvent { startTime = startTime, endTime = endTime, value = value };
        }
    }

    public sealed class Note
    {
        // 基础（Chart.ts）
        public int type;
        public int time;
        public double positionX;
        public int holdTime;
        public double speed;
        public double floorPosition;
        // 运行期（NoteExtends）
        public double maxVisiblePos;
        public double offsetX;
        public double offsetY;
        public double alpha;
        public double seconds;
        public double holdSeconds;
        public JudgeLine line;
        public int lineId;
        public int noteId;
        public bool isAbove;
        public string name;
        public bool isMulti;
        public List<Note> nearNotes;
        public double? badTime;   // JS: 用 `== null` / `delete` 判存在，对应可空
        public double? badY;
        public double projectX;
        public double projectY;
        public double cosr;
        public double sinr;
        public bool visible;
        public bool showPoint;
        public int? frameCount;   // JS: 初值 undefined；`undefined > 4` 为 false，可空 int 比较同样为 false
        public int status;
        public bool scored;
        public double holdTapTime;
        public int holdStatus;
        public bool holdBroken;
        public double statOffset;

        public Note() { }
        public Note(IDictionary<string, object> n)
        {
            type = Type.Int(Type.Get(n, "type"));
            time = Type.Int(Type.Get(n, "time"));
            positionX = Type.Float(Type.Get(n, "positionX"));
            holdTime = Type.Int(Type.Get(n, "holdTime"));
            speed = Type.Float(Type.Get(n, "speed"));
            floorPosition = Type.Float(Type.Get(n, "floorPosition"));
        }
        public Note Clone()
        {
            return new Note
            {
                type = type, time = time, positionX = positionX,
                holdTime = holdTime, speed = speed, floorPosition = floorPosition
            };
        }
    }

    public sealed class JudgeLineEvent
    {
        // 基础（Chart.ts）
        public int startTime;
        public int endTime;
        public double start;
        public double end;
        public double start2;
        public double end2;
        // 运行期（LineEventExtends）
        public double startSeconds;
        public double endSeconds;

        public JudgeLineEvent() { }
        public JudgeLineEvent(IDictionary<string, object> e)
        {
            startTime = Type.Int(Type.Get(e, "startTime"));
            endTime = Type.Int(Type.Get(e, "endTime"));
            start = Type.Float(Type.Get(e, "start"));
            end = Type.Float(Type.Get(e, "end"));
            start2 = Type.Float(Type.Get(e, "start2"));
            end2 = Type.Float(Type.Get(e, "end2"));
        }
        public JudgeLineEvent Clone()
        {
            return new JudgeLineEvent
            {
                startTime = startTime, endTime = endTime,
                start = start, end = end, start2 = start2, end2 = end2
            };
        }
    }

    public sealed class JudgeLine
    {
        // 基础（Chart.ts）
        public int numOfNotes;
        public int numOfNotesAbove;
        public int numOfNotesBelow;
        public double bpm;
        public List<SpeedEvent> speedEvents;
        public List<Note> notesAbove;
        public List<Note> notesBelow;
        public List<JudgeLineEvent> judgeLineDisappearEvents;
        public List<JudgeLineEvent> judgeLineMoveEvents;
        public List<JudgeLineEvent> judgeLineRotateEvents;
        // 运行期（JudgeLineExtends）
        public int lineId;
        public double offsetX;
        public double offsetY;
        public double alpha;
        public double rotation;
        public double positionY;
        public double positionY2;
        public int disappearEventsIndex;
        public int moveEventsIndex;
        public int rotateEventsIndex;
        public int speedEventsIndex;
        public double cosr;
        public double sinr;
        // 判定线贴图布局（在 loadLineData 里填，drawLine 里用；纯数值，平台无关）
        public double imageW;
        public double imageH;
        public bool imageD;
        public bool imageU;
        public double imageS;
        public double imageA;
        public bool imageC;
        // 实际位图数组 [normal, MP, AP(null), FC]，运行期为 CanvasBitmap，这里用 object 保持 Core 与渲染后端解耦
        public object[] imageL;

        public JudgeLine() { }
        public JudgeLine(IDictionary<string, object> line)
        {
            numOfNotes = Type.Int(Type.Get(line, "numOfNotes"));
            numOfNotesAbove = Type.Int(Type.Get(line, "numOfNotesAbove"));
            numOfNotesBelow = Type.Int(Type.Get(line, "numOfNotesBelow"));
            bpm = Type.Float(Type.Get(line, "bpm"));
            speedEvents = Type.Arr(Type.Get(line, "speedEvents"), m => new SpeedEvent(m));
            notesAbove = Type.Arr(Type.Get(line, "notesAbove"), m => new Note(m));
            notesBelow = Type.Arr(Type.Get(line, "notesBelow"), m => new Note(m));
            if (!Type.Has(line, "numOfNotesAbove")) numOfNotesAbove = notesAbove.Count;
            if (!Type.Has(line, "numOfNotesBelow")) numOfNotesBelow = notesBelow.Count;
            if (!Type.Has(line, "numOfNotes")) numOfNotes = numOfNotesAbove + numOfNotesBelow;
            judgeLineDisappearEvents = Type.Arr(Type.Get(line, "judgeLineDisappearEvents"), m => new JudgeLineEvent(m));
            judgeLineMoveEvents = Type.Arr(Type.Get(line, "judgeLineMoveEvents"), m => new JudgeLineEvent(m));
            judgeLineRotateEvents = Type.Arr(Type.Get(line, "judgeLineRotateEvents"), m => new JudgeLineEvent(m));
        }
        public JudgeLine Clone()
        {
            var c = new JudgeLine
            {
                numOfNotes = numOfNotes,
                numOfNotesAbove = numOfNotesAbove,
                numOfNotesBelow = numOfNotesBelow,
                bpm = bpm,
                speedEvents = CloneList(speedEvents, e => e.Clone()),
                notesAbove = CloneList(notesAbove, n => n.Clone()),
                notesBelow = CloneList(notesBelow, n => n.Clone()),
                judgeLineDisappearEvents = CloneList(judgeLineDisappearEvents, e => e.Clone()),
                judgeLineMoveEvents = CloneList(judgeLineMoveEvents, e => e.Clone()),
                judgeLineRotateEvents = CloneList(judgeLineRotateEvents, e => e.Clone())
            };
            return c;
        }

        private static List<T> CloneList<T>(List<T> src, System.Func<T, T> clone)
        {
            var list = new List<T>(src == null ? 0 : src.Count);
            if (src != null) foreach (var i in src) list.Add(clone(i));
            return list;
        }
    }

    public sealed class Chart
    {
        // 基础（Chart.ts）
        public int formatVersion;
        public double offset;
        public int numOfNotes;
        public List<JudgeLine> judgeLineList;
        // 运行期（ChartExtends）
        public double maxSeconds;

        public Chart() { judgeLineList = new List<JudgeLine>(); }
        public Chart(IDictionary<string, object> input)
        {
            var chart = input ?? new Dictionary<string, object>();
            formatVersion = Type.Int(Type.Get(chart, "formatVersion"));
            offset = Type.Float(Type.Get(chart, "offset"));
            numOfNotes = Type.Int(Type.Get(chart, "numOfNotes"));
            judgeLineList = Type.Arr(Type.Get(chart, "judgeLineList"), m => new JudgeLine(m));
            if (!Type.Has(chart, "numOfNotes"))
            {
                int sum = 0;
                foreach (var l in judgeLineList) sum += l.numOfNotes;
                numOfNotes = sum;
            }
        }

        /// <summary>照抄 chart.duplicate()：等价于 Type.obj(this, Chart)，即只深拷基础字段。</summary>
        public Chart Duplicate()
        {
            var c = new Chart
            {
                formatVersion = formatVersion,
                offset = offset,
                numOfNotes = numOfNotes,
                judgeLineList = new List<JudgeLine>(judgeLineList.Count)
            };
            foreach (var l in judgeLineList) c.judgeLineList.Add(l.Clone());
            return c;
        }
    }
}
