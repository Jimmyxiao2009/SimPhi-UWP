using System;
using System.Collections.Generic;
using System.Globalization;
using SimPhi.Core;
using CT = SimPhi.Core.Type;   // 消歧：避免与 System.Type 撞名

namespace SimPhi.Pec
{
    /// <summary>parseRPE 的返回。data 已是可直接喂给 Renderer 的 Core.Chart。</summary>
    public sealed class RpeResult
    {
        public Chart data;
        public Dictionary<string, string> info;
        public List<Dictionary<string, string>> line;
        public string format;
    }

    /// <summary>
    /// 照抄 @sim-phi/extends src/pec/rpe2json.ts（+ index.ts 的归一化）：RPE → PGS(Core.Chart)。
    /// 输入是已解析的 RPE JSON（IDictionary/IList，由 UWP 加载层从 mp3/jpg/json zip 里取出并解析）。
    /// </summary>
    public static class Rpe2Json
    {
        // ===== 可移植 JSON 访问 =====
        private static IDictionary<string, object> Obj(object o) { return o as IDictionary<string, object> ?? new Dictionary<string, object>(); }
        private static IList<object> Arr(object o) { return o as IList<object> ?? new List<object>(); }
        private static object G(IDictionary<string, object> m, string k) { object v; return m != null && m.TryGetValue(k, out v) ? v : null; }
        private static bool Has(IDictionary<string, object> m, string k) { object v; return m != null && m.TryGetValue(k, out v) && v != null; }
        private static double Num(object o) { return CT.Float(o); }              // Number(x)||0
        private static double D(IDictionary<string, object> m, string k, double def) { return Has(m, k) ? Num(G(m, k)) : def; }
        private static double[] Beat(object o) { var a = Arr(o); return new double[] { a.Count > 0 ? Num(a[0]) : 0, a.Count > 1 ? Num(a[1]) : 0, a.Count > 2 ? Num(a[2]) : 1 }; }

        // ===== 中间类型 =====
        private sealed class EvR2 { public double startTime, endTime, start, end; public Func<double, double> easingFn; }
        private sealed class EvDelta { public double startTime, endTime, start, end, delta; }
        private sealed class EvXY { public double startTime, endTime, start, end, start2, end2; }
        private sealed class SpeedPec { public double time, value; }
        private struct PosV { public double v1, v4, vmin; }
        private sealed class SpeedFloor { public double startTime, endTime, value, floorPosition, floorPositionMin; }

        private enum EaseCode { NoError = 0, EqualsLinear = 1, LeftEqualsRight = -1, TypeNotSupported = -2, ValueNotFinite = -3 }

        // ===== 缓动 =====
        private static void GetEasingFn(IDictionary<string, object> le, double startTime, double endTime, out EaseCode code, out Func<double, double> fn)
        {
            double start = D(le, "start", 0), end = D(le, "end", 0), bezier = D(le, "bezier", 0);
            if (Math.Floor(bezier) == 1)
            {
                if (start == end) { code = EaseCode.EqualsLinear; fn = null; return; }
                var bp = Arr(G(le, "bezierPoints"));
                double p1x = bp.Count > 0 ? Num(bp[0]) : 0, p1y = bp.Count > 1 ? Num(bp[1]) : 0, p2x = bp.Count > 2 ? Num(bp[2]) : 0, p2y = bp.Count > 3 ? Num(bp[3]) : 0;
                if (p1x == p1y && p2x == p2y) { code = EaseCode.EqualsLinear; fn = null; return; }
                double eSpeedB = 1 / (endTime - startTime);
                double e1DeltaB = end - start;
                var easeB = CubicBezier.Make(p1x, p1y, p2x, p2y);
                code = EaseCode.NoError;
                fn = t => easeB((t - startTime) * eSpeedB) * e1DeltaB;
                return;
            }
            double easingType = D(le, "easingType", 0), easingLeft = D(le, "easingLeft", 0), easingRight = D(le, "easingRight", 1);
            if (easingLeft == easingRight) { code = EaseCode.LeftEqualsRight; fn = null; return; }
            if (start == end) { code = EaseCode.EqualsLinear; fn = null; return; }
            var easingFn = Easing.Get(easingType);
            if (easingFn == null) { code = easingType == 1 ? EaseCode.EqualsLinear : EaseCode.TypeNotSupported; fn = null; return; }
            double eHead = easingFn(easingLeft);
            double eTail = easingFn(easingRight);
            double eSpeed = (easingRight - easingLeft) / (endTime - startTime);
            double e1Delta = (end - start) / (eTail - eHead);
            if (double.IsNaN(e1Delta) || double.IsInfinity(e1Delta)) { code = EaseCode.ValueNotFinite; fn = null; return; }
            code = EaseCode.NoError;
            fn = t => (easingFn((t - startTime) * eSpeed + easingLeft) - eHead) * e1Delta;
        }

        // ===== 事件处理（照抄）=====
        private static void PushLineEvent(List<EvDelta> ls, EvR2 le)
        {
            double startTime = le.startTime, endTime = le.endTime, start = le.start, end = le.end;
            var easingFn = le.easingFn;
            double delta = (end - start) / (endTime - startTime);
            for (int i = ls.Count - 1; i >= 0; i--)
            {
                var e = ls[i];
                if (e.endTime < startTime) // 相离：补全空隙
                {
                    if (i + 1 < ls.Count) ls[i + 1] = new EvDelta { startTime = e.endTime, endTime = startTime, start = e.end, end = e.end, delta = 0 };
                    else ls.Add(new EvDelta { startTime = e.endTime, endTime = startTime, start = e.end, end = e.end, delta = 0 });
                    break;
                }
                if (e.startTime == startTime) { ls.RemoveRange(i, ls.Count - i); break; } // 相切：截断
                if (e.startTime < startTime) // 相交：截断交点以后
                {
                    e.end = e.start + (startTime - e.startTime) * e.delta;
                    e.endTime = startTime;
                    e.delta = (e.end - e.start) / (startTime - e.startTime);
                    ls.RemoveRange(i + 1, ls.Count - (i + 1));
                    break;
                }
            }
            if (startTime >= endTime)
            {
                ls.Add(new EvDelta { startTime = startTime, endTime = startTime, start = end, end = end, delta = 0 });
                return;
            }
            if (easingFn == null)
            {
                ls.Add(new EvDelta { startTime = startTime, endTime = endTime, start = start, end = end, delta = delta });
            }
            else
            {
                double v1 = 0, v2 = 0;
                for (double j = startTime; j < endTime; j++)
                {
                    v1 = v2;
                    v2 = easingFn(j + 1);
                    ls.Add(new EvDelta { startTime = j, endTime = j + 1, start = start + v1, end = start + v2, delta = v2 - v1 });
                }
            }
        }

        private static List<SpeedPec> ToSpeedEvent(List<EvDelta> le)
        {
            var result = new List<SpeedPec>();
            if (le.Count == 0 || le[0].startTime > 0) result.Add(new SpeedPec { time = 0, value = 0 });
            foreach (var i in le)
            {
                double startTime = i.startTime, endTime = i.endTime, start = i.start, end = i.end;
                result.Add(new SpeedPec { time = startTime, value = start });
                if (start != end)
                {
                    double t1 = (end - start) / (endTime - startTime);
                    for (double j = startTime; j < endTime; j++)
                    {
                        double x = j + 0.5 - startTime;
                        result.Add(new SpeedPec { time = j, value = start + x * t1 });
                    }
                    result.Add(new SpeedPec { time = endTime, value = end });
                }
            }
            return result;
        }

        private static double GetEventsValue(List<EvDelta> e, double t, bool d)
        {
            double result = e.Count > 0 ? e[0].start : 0;
            foreach (var i in e)
            {
                if (t < i.startTime) break;
                if (d && t == i.startTime) break;
                if (t >= i.endTime) result = i.end;
                else result = i.start + (t - i.startTime) * i.delta;
            }
            return result;
        }

        private static void GetMoveValue(List<EvXY> e, double t, bool d, out double result, out double result2)
        {
            result = e.Count > 0 ? e[0].start : 0;
            result2 = e.Count > 0 ? e[0].start2 : 0;
            foreach (var i in e)
            {
                if (t < i.startTime) break;
                if (d && t == i.startTime) break;
                if (t >= i.endTime) { result = i.end; result2 = i.end2; }
                else
                {
                    result = i.start + (t - i.startTime) * (i.end - i.start) / (i.endTime - i.startTime);
                    result2 = i.start2 + (t - i.startTime) * (i.end2 - i.start2) / (i.endTime - i.startTime);
                }
            }
        }

        private static double GetRotateValue(List<EvDelta> e, double t, bool d)
        {
            double result = e.Count > 0 ? e[0].start : 0;
            foreach (var i in e)
            {
                if (t < i.startTime) break;
                if (d && t == i.startTime) break;
                if (t >= i.endTime) result = i.end;
                else result = i.start + (t - i.startTime) * (i.end - i.start) / (i.endTime - i.startTime);
            }
            return result;
        }

        private static List<EvXY> CombineXYEvents(List<EvDelta> xe, List<EvDelta> ye)
        {
            var le = new List<EvXY>();
            var splits = new List<double>();
            foreach (var i in xe) { splits.Add(i.startTime); splits.Add(i.endTime); }
            foreach (var i in ye) { splits.Add(i.startTime); splits.Add(i.endTime); }
            splits.Sort();
            for (int i = 0; i < splits.Count - 1; i++)
            {
                double startTime = splits[i], endTime = splits[i + 1];
                if (startTime == endTime) continue;
                double startX = GetEventsValue(xe, startTime, false);
                double endX = GetEventsValue(xe, endTime, true);
                double startY = GetEventsValue(ye, startTime, false);
                double endY = GetEventsValue(ye, endTime, true);
                le.Add(new EvXY { startTime = startTime, endTime = endTime, start = startX, end = endX, start2 = startY, end2 = endY });
            }
            return le;
        }

        private static List<EvDelta> CombineMultiEvents(List<List<EvDelta>> es)
        {
            var le = new List<EvDelta>();
            var splits = new List<double>();
            foreach (var e in es) foreach (var i in e) { splits.Add(i.startTime); splits.Add(i.endTime); }
            splits.Sort();
            for (int i = 0; i < splits.Count - 1; i++)
            {
                double startTime = splits[i], endTime = splits[i + 1];
                if (startTime == endTime) continue;
                double start = 0, end = 0;
                foreach (var e in es) { start += GetEventsValue(e, startTime, false); end += GetEventsValue(e, endTime, true); }
                le.Add(new EvDelta { startTime = startTime, endTime = endTime, start = start, end = end, delta = (end - start) / (endTime - startTime) });
            }
            return le;
        }

        private static void MergeFather(LineRPE child, LineRPE father)
        {
            var moveEvents = new List<EvXY>();
            var splits = new List<double>();
            foreach (var i in father.moveEvents) { splits.Add(i.startTime); splits.Add(i.endTime); }
            foreach (var i in father.rotateEvents) { splits.Add(i.startTime); splits.Add(i.endTime); }
            foreach (var i in child.moveEvents) { splits.Add(i.startTime); splits.Add(i.endTime); }
            splits.Sort();
            if (splits.Count == 0) { child.moveEvents = moveEvents; return; }
            double lo = splits[0], hi = splits[splits.Count - 1];
            for (double i = lo; i < hi; i++)
            {
                double startTime = i, endTime = i + 1;
                double fatherX, fatherY, fatherX2, fatherY2;
                GetMoveValue(father.moveEvents, startTime, false, out fatherX, out fatherY);
                double fatherR = GetRotateValue(father.rotateEvents, startTime, false) * -Math.PI / 180;
                GetMoveValue(father.moveEvents, endTime, true, out fatherX2, out fatherY2);
                double fatherR2 = GetRotateValue(father.rotateEvents, endTime, true) * -Math.PI / 180;
                double childX, childY, childX2, childY2;
                GetMoveValue(child.moveEvents, startTime, false, out childX, out childY);
                GetMoveValue(child.moveEvents, endTime, true, out childX2, out childY2);
                double start = fatherX + childX * Math.Cos(fatherR) - childY * Math.Sin(fatherR);
                double end = fatherX2 + childX2 * Math.Cos(fatherR2) - childY2 * Math.Sin(fatherR2);
                double start2 = fatherY + childX * Math.Sin(fatherR) + childY * Math.Cos(fatherR);
                double end2 = fatherY2 + childX2 * Math.Sin(fatherR2) + childY2 * Math.Cos(fatherR2);
                moveEvents.Add(new EvXY { startTime = startTime, endTime = endTime, start = start, end = end, start2 = start2, end2 = end2 });
            }
            child.moveEvents = moveEvents;
        }

        // ===== EventLayer / LineRPE =====
        private sealed class EventLayer
        {
            public List<EvR2> moveX = new List<EvR2>(), moveY = new List<EvR2>(), rotate = new List<EvR2>(), alpha = new List<EvR2>(), speed = new List<EvR2>();
        }

        private sealed class NoteR
        {
            public int type; public double time, positionX, holdTime, speed; public bool isAbove, isFake, isHidden;
        }

        private sealed class LineRPE
        {
            public double bpm;
            public List<NoteR> notes = new List<NoteR>();
            public List<EventLayer> eventLayers = new List<EventLayer>();
            public int id;
            public LineRPE father;
            public List<EvXY> moveEvents;
            public List<EvDelta> rotateEvents;
            public List<EvDelta> alphaEvents;
            public List<SpeedPec> speedEvents;
            private bool settled, merged;

            public LineRPE(double bpmIn) { bpm = double.IsNaN(bpmIn) ? 120 : bpmIn; }

            public void Preset()
            {
                Comparison<EvR2> sortFn2 = (a, b) => a.startTime.CompareTo(b.startTime);
                var allMoveX = new List<List<EvDelta>>();
                var allMoveY = new List<List<EvDelta>>();
                var allRotate = new List<List<EvDelta>>();
                var allAlpha = new List<List<EvDelta>>();
                var allSpeed = new List<List<EvDelta>>();
                foreach (var e in eventLayers)
                {
                    var mx = new List<EvDelta>(); var my = new List<EvDelta>(); var ro = new List<EvDelta>(); var al = new List<EvDelta>(); var sp = new List<EvDelta>();
                    e.moveX.Sort(sortFn2); foreach (var i in e.moveX) PushLineEvent(mx, i);
                    e.moveY.Sort(sortFn2); foreach (var i in e.moveY) PushLineEvent(my, i);
                    e.rotate.Sort(sortFn2); foreach (var i in e.rotate) PushLineEvent(ro, i);
                    e.alpha.Sort(sortFn2); foreach (var i in e.alpha) PushLineEvent(al, i);
                    e.speed.Sort(sortFn2); foreach (var i in e.speed) PushLineEvent(sp, i);
                    allMoveX.Add(mx); allMoveY.Add(my); allRotate.Add(ro); allAlpha.Add(al); allSpeed.Add(sp);
                }
                var moveX = CombineMultiEvents(allMoveX);
                var moveY = CombineMultiEvents(allMoveY);
                moveEvents = CombineXYEvents(moveX, moveY);
                rotateEvents = CombineMultiEvents(allRotate);
                alphaEvents = CombineMultiEvents(allAlpha);
                speedEvents = ToSpeedEvent(CombineMultiEvents(allSpeed));
                settled = true;
            }

            public void FitFather(List<LineRPE> stack, Action<string> onwarning)
            {
                if (!settled) Preset();
                if (stack.Contains(this))
                {
                    onwarning("检测到循环继承(对应的father将被视为-1)");
                    foreach (var i in stack) i.father = null;
                    return;
                }
                if (father != null) { var s2 = new List<LineRPE>(stack); s2.Add(this); father.FitFather(s2, onwarning); }
                if (father != null && !merged) { MergeFather(this, father); merged = true; }
            }

            public JudgeLine Format(Action<string> onwarning)
            {
                FitFather(new List<LineRPE>(), onwarning);
                var jl = new JudgeLine
                {
                    bpm = bpm,
                    speedEvents = new List<SpeedEvent>(),
                    notesAbove = new List<Note>(),
                    notesBelow = new List<Note>(),
                    judgeLineDisappearEvents = new List<JudgeLineEvent>(),
                    judgeLineMoveEvents = new List<JudgeLineEvent>(),
                    judgeLineRotateEvents = new List<JudgeLineEvent>()
                };
                foreach (var i in moveEvents)
                    jl.judgeLineMoveEvents.Add(new JudgeLineEvent
                    {
                        startTime = (int)i.startTime, endTime = (int)i.endTime,
                        start = (i.start + 675) / 1350, end = (i.end + 675) / 1350,
                        start2 = (i.start2 + 450) / 900, end2 = (i.end2 + 450) / 900
                    });
                foreach (var i in rotateEvents)
                    jl.judgeLineRotateEvents.Add(new JudgeLineEvent { startTime = (int)i.startTime, endTime = (int)i.endTime, start = -i.start, end = -i.end, start2 = 0, end2 = 0 });
                foreach (var i in alphaEvents)
                    jl.judgeLineDisappearEvents.Add(new JudgeLineEvent { startTime = (int)i.startTime, endTime = (int)i.endTime, start = Math.Max(0, i.start / 255), end = Math.Max(0, i.end / 255), start2 = 0, end2 = 0 });

                // floorPosition
                var sf = new List<SpeedFloor>();
                double floorPos = 0, minPos = 0;
                for (int i = 0; i < speedEvents.Count; i++)
                {
                    double startTime = Math.Max(speedEvents[i].time, 0);
                    double endTime = i < speedEvents.Count - 1 ? speedEvents[i + 1].time : 1e9;
                    double value = speedEvents[i].value * 2 / 9;
                    sf.Add(new SpeedFloor { startTime = startTime, endTime = endTime, value = value, floorPosition = floorPos, floorPositionMin = minPos });
                    jl.speedEvents.Add(new SpeedEvent { startTime = (int)startTime, endTime = (int)endTime, value = value });
                    floorPos += (endTime - startTime) * value / bpm * 1.875;
                    floorPos = (float)floorPos;
                    minPos = Math.Min(minPos, floorPos);
                }

                Func<double, PosV> getPositionValues = time =>
                {
                    double v1 = 0, v2 = 0, v3 = 0, vmin = 0;
                    foreach (var e in sf)
                    {
                        if (time > e.endTime) continue;
                        if (time < e.startTime) break;
                        v1 = e.floorPosition; v2 = e.value; v3 = time - e.startTime; vmin = e.floorPositionMin;
                    }
                    return new PosV { v1 = v1, v4 = v2 * v3, vmin = vmin };
                };
                Func<double, double, double> getHoldSpeedValue = (time, holdTime) =>
                {
                    var start = getPositionValues(time);
                    var end = getPositionValues(time + holdTime);
                    return ((end.v1 - start.v1) / 1.875 * bpm + (end.v4 - start.v4)) / holdTime;
                };

                notes.Sort((a, b) => a.time.CompareTo(b.time));
                foreach (var i in notes)
                {
                    var pv = getPositionValues(i.time);
                    double speedFactor = i.type == 3 ? getHoldSpeedValue(i.time, i.holdTime) : 1;
                    double floorPosition = (float)(pv.v1 + pv.v4 / bpm * 1.875);
                    var note = new Note
                    {
                        type = i.type,
                        time = (int)(i.time + (i.isFake ? 1e9 : 0)),
                        positionX = i.positionX,
                        holdTime = (int)i.holdTime,
                        speed = i.speed * speedFactor,
                        floorPosition = floorPosition
                    };
                    if (i.isHidden) { note.speed = 0; note.floorPosition = Math.Min(pv.vmin, floorPosition) - 1; }
                    if (i.isAbove)
                    {
                        jl.notesAbove.Add(note);
                        if (i.isFake) continue;
                        jl.numOfNotes++; jl.numOfNotesAbove++;
                    }
                    else
                    {
                        jl.notesBelow.Add(note);
                        if (i.isFake) continue;
                        jl.numOfNotes++; jl.numOfNotesBelow++;
                    }
                }
                return jl;
            }
        }

        // RPE note.type → PGS type：[0,1,4,2,3].indexOf(rpeType)
        private static int MapType(int rpeType)
        {
            int[] order = { 0, 1, 4, 2, 3 };
            for (int k = 0; k < order.Length; k++) if (order[k] == rpeType) return k;
            return -1;
        }

        public static RpeResult Parse(IDictionary<string, object> root, string pathname, string filename)
        {
            var meta = Has(root, "META") ? Obj(G(root, "META")) : root;
            if (!Has(meta, "RPEVersion")) throw new Exception("Invalid rpe file");
            var chart = new Chart { formatVersion = 3, offset = 0 };
            string format = "RPE(" + CT.Str(G(meta, "RPEVersion")) + ")";

            var info = new Dictionary<string, string>
            {
                ["Chart"] = filename,
                ["Music"] = CT.Str(G(meta, "song")),
                ["Image"] = CT.Str(G(meta, "background")),
                ["Name"] = CT.Str(G(meta, "name")),
                ["Artist"] = CT.Str(G(meta, "composer")),
                ["Charter"] = CT.Str(G(meta, "charter")),
                ["Level"] = CT.Str(G(meta, "level"))
            };
            chart.offset = D(meta, "offset", 0) / 1e3;

            var judgeLineListRaw = Arr(G(root, "judgeLineList"));
            var line = new List<Dictionary<string, string>>();
            for (int index = 0; index < judgeLineListRaw.Count; index++)
            {
                var i = Obj(judgeLineListRaw[index]);
                string texture = CT.Str(G(i, "Texture")).Replace("\0", "");
                if (texture == "line.png") continue;
                var extended = Obj(G(i, "extended"));
                var sxe = Arr(G(extended, "scaleXEvents"));
                var sye = Arr(G(extended, "scaleYEvents"));
                double scaleX = sxe.Count > 0 ? D(Obj(sxe[sxe.Count - 1]), "end", 1) : 1;
                double scaleY = sye.Count > 0 ? D(Obj(sye[sye.Count - 1]), "end", 1) : 1;
                line.Add(new Dictionary<string, string>
                {
                    ["Chart"] = filename,
                    ["LineId"] = index.ToString(CultureInfo.InvariantCulture),
                    ["Image"] = texture,
                    ["Scale"] = scaleY.ToString(CultureInfo.InvariantCulture),
                    ["Aspect"] = (scaleX / scaleY).ToString(CultureInfo.InvariantCulture),
                    ["UseBackgroundDim"] = "0",
                    ["UseLineColor"] = "1",
                    ["UseLineScale"] = "1"
                });
            }

            // BPM
            var bpmListRaw = Arr(G(root, "BPMList"));
            var bpmItems = new List<KeyValuePair<double, double>>(); // time -> bpm
            foreach (var b in bpmListRaw)
            {
                var bo = Obj(b);
                double[] st = Beat(G(bo, "startTime"));
                double time = st[0] + st[1] / st[2];
                bpmItems.Add(new KeyValuePair<double, double>(time, D(bo, "bpm", 120)));
            }
            bpmItems.Sort((a, b) => a.Key.CompareTo(b.Key));
            var bpmList = new BpmList(bpmItems.Count > 0 ? bpmItems[0].Value : 120);
            for (int idx = 0; idx < bpmItems.Count; idx++)
            {
                double nextTime = idx + 1 < bpmItems.Count ? bpmItems[idx + 1].Key : 1e9;
                if (idx + 1 < bpmItems.Count && bpmItems[idx + 1].Key <= 0) continue; // 过滤负数
                double t = bpmItems[idx].Key;
                bpmList.Push(t < 0 ? 0 : t, nextTime, bpmItems[idx].Value);
            }

            var lines = new List<LineRPE>();
            for (int li = 0; li < judgeLineListRaw.Count; li++)
            {
                var i = Obj(judgeLineListRaw[li]);
                var lineRPE = new LineRPE(bpmList.baseBpm) { id = li };
                var notesRaw = Arr(G(i, "notes"));
                foreach (var nr in notesRaw)
                {
                    var note = Obj(nr);
                    double alpha = Has(note, "alpha") ? Num(G(note, "alpha")) : 255;
                    int above = (int)D(note, "above", 0);
                    int type = MapType((int)D(note, "type", 0));
                    int time = bpmList.Calc2(Beat(G(note, "startTime")));
                    int holdTime = bpmList.Calc2(Beat(G(note, "endTime"))) - time;
                    double speed = D(note, "speed", 1);
                    double positionX = D(note, "positionX", 0) / 75.375;
                    lineRPE.notes.Add(new NoteR
                    {
                        type = type, time = time, positionX = positionX, holdTime = holdTime, speed = speed,
                        isAbove = above == 1, isFake = D(note, "isFake", 0) != 0, isHidden = alpha == 0
                    });
                }
                var eventLayersRaw = Arr(G(i, "eventLayers"));
                foreach (var elr in eventLayersRaw)
                {
                    if (elr == null) continue;
                    var evts = Obj(elr);
                    var layer = new EventLayer();
                    AddEvents(layer.moveX, Arr(G(evts, "moveXEvents")), bpmList, true);
                    AddEvents(layer.moveY, Arr(G(evts, "moveYEvents")), bpmList, true);
                    AddEvents(layer.rotate, Arr(G(evts, "rotateEvents")), bpmList, true);
                    AddEvents(layer.alpha, Arr(G(evts, "alphaEvents")), bpmList, true);
                    AddEvents(layer.speed, Arr(G(evts, "speedEvents")), bpmList, false); // speed 无缓动
                    lineRPE.eventLayers.Add(layer);
                }
                lines.Add(lineRPE);
            }
            // father
            for (int li = 0; li < judgeLineListRaw.Count; li++)
            {
                int fatherIdx = (int)D(Obj(judgeLineListRaw[li]), "father", -1);
                lines[li].father = (fatherIdx >= 0 && fatherIdx < lines.Count) ? lines[fatherIdx] : null;
            }
            // format
            foreach (var lr in lines)
            {
                var jl = lr.Format(_ => { });
                chart.judgeLineList.Add(jl);
                chart.numOfNotes += jl.numOfNotes;
            }

            // 归一化（PEC.parseRPE 的后处理）
            foreach (var jl in chart.judgeLineList)
            {
                Format.NormalizeSpeedEvents(jl.speedEvents);
                Format.NormalizeLineEvents(jl.judgeLineDisappearEvents);
                Format.NormalizeLineEvents(jl.judgeLineMoveEvents);
                Format.NormalizeLineEvents(jl.judgeLineRotateEvents);
            }

            return new RpeResult { data = chart, info = info, line = line, format = format };
        }

        private static void AddEvents(List<EvR2> dst, IList<object> raw, BpmList bpmList, bool withEasing)
        {
            foreach (var er in raw)
            {
                var evt = Obj(er);
                double startTime = bpmList.Calc2(Beat(G(evt, "startTime")));
                double endTime = bpmList.Calc2(Beat(G(evt, "endTime")));
                Func<double, double> fn = null;
                if (withEasing) { EaseCode code; GetEasingFn(evt, startTime, endTime, out code, out fn); }
                dst.Add(new EvR2 { startTime = startTime, endTime = endTime, start = D(evt, "start", 0), end = D(evt, "end", 0), easingFn = fn });
            }
        }
    }
}
