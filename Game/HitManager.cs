using System;
using System.Collections.Generic;
using SimPhi.Core;

namespace SimPhi.Game
{
    /// <summary>触摸/鼠标/键盘的判定事件类型标识。</summary>
    public enum HitType { Keyboard, Mouse, Touch }

    /// <summary>照抄 src/components/HitManager.ts 的 HitEvent。</summary>
    public sealed class HitEvent
    {
        public HitType type;
        public object id;          // number | string
        public double offsetX;
        public double offsetY;
        public bool isActive;
        public bool isTapped;
        public bool isMoving;
        public double lastDeltaX;
        public double lastDeltaY;
        public double nowDeltaX;
        public double nowDeltaY;
        public double deltaTime;
        public double currentTime;
        public bool flicking;
        public bool flicked;

        public HitEvent(HitType type, object id, double offsetX, double offsetY)
        {
            this.type = type;
            this.id = id;
            this.offsetX = offsetX;
            this.offsetY = offsetY;
            isActive = true;
            isTapped = false;
            isMoving = false;
            lastDeltaX = 0;
            lastDeltaY = 0;
            nowDeltaX = 0;
            nowDeltaY = 0;
            deltaTime = 0;
            currentTime = Clock.Now();
            flicking = false;
            flicked = false;
        }

        public void Move(double offsetX, double offsetY)
        {
            lastDeltaX = nowDeltaX;
            lastDeltaY = nowDeltaY;
            nowDeltaX = offsetX - this.offsetX;
            nowDeltaY = offsetY - this.offsetY;
            this.offsetX = offsetX;
            this.offsetY = offsetY;
            double time = Clock.Now();
            deltaTime = time - currentTime;
            currentTime = time;
            isMoving = true;
            double flickSpeed = (nowDeltaX * lastDeltaX + nowDeltaY * lastDeltaY)
                / Math.Sqrt(lastDeltaX * lastDeltaX + lastDeltaY * lastDeltaY) / deltaTime;
            if (flicking && flickSpeed < 0.5)
            {
                flicking = false;
                flicked = false;
            }
            else if (!flicking && flickSpeed > 1.0) flicking = true;
        }
    }

    /// <summary>照抄 HitManager。id 用 object 比较（number 用 int 装箱、string 用字符串）。</summary>
    public sealed class HitManager
    {
        public readonly List<HitEvent> list = new List<HitEvent>();

        private static bool IdEq(object a, object b)
        {
            return Equals(a, b);
        }

        public void Activate(HitType type, object id, double offsetX, double offsetY)
        {
            int idx = list.FindIndex(hit => hit.type == type && IdEq(hit.id, id));
            if (idx != -1) list.RemoveAt(idx);
            list.Add(new HitEvent(type, id, offsetX, offsetY));
        }

        public void Moving(HitType type, object id, double offsetX, double offsetY)
        {
            var hitEl = list.Find(hit => hit.type == type && IdEq(hit.id, id));
            if (hitEl != null) hitEl.Move(offsetX, offsetY);
        }

        public void Deactivate(HitType type, object id)
        {
            var hitEl = list.Find(hit => hit.type == type && IdEq(hit.id, id));
            if (hitEl != null) hitEl.isActive = false;
        }

        public void Update()
        {
            for (int i = 0; i < list.Count; i++)
            {
                var hitEl = list[i];
                if (hitEl.isActive)
                {
                    hitEl.isTapped = true;
                    hitEl.isMoving = false;
                }
                else { list.RemoveAt(i--); }
            }
        }

        public void Clear(HitType type)
        {
            foreach (var i in list) if (i.type == type) Deactivate(type, i.id);
        }
    }

    /// <summary>照抄 JudgeEvent。1-Tap,2-Hold/Drag,3-Move。</summary>
    public sealed class JudgeEvent
    {
        public double offsetX;
        public double offsetY;
        public int type;
        public bool judged;
        public HitEvent @event;
        public bool preventBad;

        public JudgeEvent(double offsetX, double offsetY, int type, HitEvent ev = null)
        {
            this.offsetX = offsetX;
            this.offsetY = offsetY;
            this.type = type;
            judged = false;
            @event = ev;
            preventBad = false;
        }
    }
}
