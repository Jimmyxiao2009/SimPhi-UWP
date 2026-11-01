using System;
using System.IO;
using Windows.Storage;

namespace SimPhi.Uwp
{
    /// <summary>
    /// 把未捕获异常 / 渲染线程异常落到 LocalState\lasterror.txt，
    /// 崩溃后用 WDP 文件接口拉回来，区分「托管异常（有栈）」与「原生 OOM（文件为空/无新异常）」。
    /// </summary>
    public static class CrashLog
    {
        private static readonly object _lock = new object();
        private static string _path;

        private static string Path()
        {
            if (_path == null)
                _path = System.IO.Path.Combine(ApplicationData.Current.LocalFolder.Path, "lasterror.txt");
            return _path;
        }

        public static void Write(string source, Exception ex)
        {
            try
            {
                string text = "[" + DateTimeOffset.Now.ToString("HH:mm:ss.fff") + "] " + source + "\r\n"
                    + (ex == null ? "(null)" : ex.ToString()) + "\r\n"
                    + "-----\r\n";
                lock (_lock) File.AppendAllText(Path(), text);
            }
            catch { /* 落盘失败就算了，别在异常处理里再抛 */ }
        }

        public static void Mark(string note)
        {
            try
            {
                lock (_lock) File.AppendAllText(Path(),
                    "[" + DateTimeOffset.Now.ToString("HH:mm:ss.fff") + "] MARK " + note + "\r\n");
            }
            catch { }
        }
    }
}
