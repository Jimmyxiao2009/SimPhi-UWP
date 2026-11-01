using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;

namespace SimPhi.Render
{
    /// <summary>
    /// 最小 SVG path 解析 → CanvasGeometry，支持 M/m L/l H/h V/v C/c S/s Z/z 与隐式重复。
    /// 仅为还原 atDraw3 结算页那两个 Path2D 按钮（用到 M/s/v/h/l/z）。
    /// </summary>
    public static class SvgPath
    {
        public static CanvasGeometry Build(ICanvasResourceCreator device, string d)
        {
            var pb = new CanvasPathBuilder(device);
            bool open = false;
            Vector2 cur = Vector2.Zero, start = Vector2.Zero, lastC2 = Vector2.Zero;
            char lastCmd = ' ';
            int i = 0, n = d.Length;

            Action ensureClosed = () => { if (open) { pb.EndFigure(CanvasFigureLoop.Open); open = false; } };

            while (i < n)
            {
                SkipSep(d, ref i);
                if (i >= n) break;
                char c = d[i];
                char cmd;
                if (char.IsLetter(c)) { cmd = c; i++; }
                else cmd = ImplicitCmd(lastCmd); // 隐式重复
                bool rel = char.IsLower(cmd);
                char u = char.ToUpperInvariant(cmd);

                switch (u)
                {
                    case 'M':
                    {
                        double x = Num(d, ref i), y = Num(d, ref i);
                        Vector2 p = rel ? new Vector2(cur.X + (float)x, cur.Y + (float)y) : new Vector2((float)x, (float)y);
                        ensureClosed();
                        pb.BeginFigure(p);
                        open = true; cur = p; start = p; lastCmd = rel ? 'l' : 'L';
                        break;
                    }
                    case 'L':
                    {
                        double x = Num(d, ref i), y = Num(d, ref i);
                        Vector2 p = rel ? new Vector2(cur.X + (float)x, cur.Y + (float)y) : new Vector2((float)x, (float)y);
                        pb.AddLine(p); cur = p; lastCmd = cmd;
                        break;
                    }
                    case 'H':
                    {
                        double x = Num(d, ref i);
                        Vector2 p = rel ? new Vector2(cur.X + (float)x, cur.Y) : new Vector2((float)x, cur.Y);
                        pb.AddLine(p); cur = p; lastCmd = cmd;
                        break;
                    }
                    case 'V':
                    {
                        double y = Num(d, ref i);
                        Vector2 p = rel ? new Vector2(cur.X, cur.Y + (float)y) : new Vector2(cur.X, (float)y);
                        pb.AddLine(p); cur = p; lastCmd = cmd;
                        break;
                    }
                    case 'C':
                    {
                        Vector2 c1 = Pt(d, ref i, rel, cur);
                        Vector2 c2 = Pt(d, ref i, rel, cur);
                        Vector2 p = Pt(d, ref i, rel, cur);
                        pb.AddCubicBezier(c1, c2, p);
                        lastC2 = c2; cur = p; lastCmd = cmd;
                        break;
                    }
                    case 'S':
                    {
                        // 平滑三次：首控制点为上一控制点关于当前点的反射（上一段为 C/S 时）
                        Vector2 c1 = (char.ToUpperInvariant(lastCmd) == 'C' || char.ToUpperInvariant(lastCmd) == 'S')
                            ? new Vector2(2 * cur.X - lastC2.X, 2 * cur.Y - lastC2.Y)
                            : cur;
                        Vector2 c2 = Pt(d, ref i, rel, cur);
                        Vector2 p = Pt(d, ref i, rel, cur);
                        pb.AddCubicBezier(c1, c2, p);
                        lastC2 = c2; cur = p; lastCmd = cmd;
                        break;
                    }
                    case 'Z':
                    {
                        if (open) { pb.EndFigure(CanvasFigureLoop.Closed); open = false; }
                        cur = start; lastCmd = cmd;
                        break;
                    }
                    default:
                        i++; // 跳过未知
                        break;
                }
            }
            ensureClosed();
            return CanvasGeometry.CreatePath(pb);
        }

        private static char ImplicitCmd(char last)
        {
            char u = char.ToUpperInvariant(last);
            if (u == 'M') return char.IsLower(last) ? 'l' : 'L'; // M 之后的隐式坐标当作 L
            return last;
        }

        private static Vector2 Pt(string s, ref int i, bool rel, Vector2 cur)
        {
            double x = Num(s, ref i), y = Num(s, ref i);
            return rel ? new Vector2(cur.X + (float)x, cur.Y + (float)y) : new Vector2((float)x, (float)y);
        }

        private static void SkipSep(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == ',' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++;
        }

        private static double Num(string s, ref int i)
        {
            SkipSep(s, ref i);
            int start = i, n = s.Length;
            if (i < n && (s[i] == '+' || s[i] == '-')) i++;
            while (i < n && (char.IsDigit(s[i]) || s[i] == '.')) i++;
            if (i < n && (s[i] == 'e' || s[i] == 'E'))
            {
                i++;
                if (i < n && (s[i] == '+' || s[i] == '-')) i++;
                while (i < n && char.IsDigit(s[i])) i++;
            }
            double v;
            double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
            return v;
        }
    }
}
