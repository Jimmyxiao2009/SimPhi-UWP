using Windows.ApplicationModel.Activation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace SimPhi.Uwp
{
    public sealed partial class App : Application
    {
        public App()
        {
            this.InitializeComponent();
            // 未捕获异常落盘，崩溃后用 WDP 拉 LocalState\lasterror.txt 看真因
            this.UnhandledException += (s, e) =>
            {
                CrashLog.Write("App.UnhandledException: " + e.Message, e.Exception);
            };
            Windows.ApplicationModel.Core.CoreApplication.UnhandledErrorDetected += (s, e) =>
            {
                try { e.UnhandledError.Propagate(); }
                catch (System.Exception ex) { CrashLog.Write("CoreApplication.UnhandledErrorDetected", ex); throw; }
            };
            // 开机标记：部署后看 lasterror.txt 有没有这行，确认新版确实在跑（消除"装没装上"的歧义）
            CrashLog.Mark("boot build=sw+halfres b16");
            // 记真实单应用内存上限，并在用量等级跨阈值时落盘（捕捉亚秒级尖峰冲到 OverLimit 的瞬间）
            try
            {
                CrashLog.Mark("memlimit=" + (Windows.System.MemoryManager.AppMemoryUsageLimit / (1024UL * 1024)) + "MB usage=" + (Windows.System.MemoryManager.AppMemoryUsage / (1024UL * 1024)) + "MB");
                Windows.System.MemoryManager.AppMemoryUsageIncreased += (s, e) =>
                {
                    var lvl = Windows.System.MemoryManager.AppMemoryUsageLevel;
                    if (lvl == Windows.System.AppMemoryUsageLevel.High || lvl == Windows.System.AppMemoryUsageLevel.OverLimit)
                        CrashLog.Mark("MEM level=" + lvl + " usage=" + (Windows.System.MemoryManager.AppMemoryUsage / (1024UL * 1024)) + "MB limit=" + (Windows.System.MemoryManager.AppMemoryUsageLimit / (1024UL * 1024)) + "MB");
                };
            }
            catch { }
        }

        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            Frame rootFrame = Window.Current.Content as Frame;
            if (rootFrame == null)
            {
                rootFrame = new Frame();
                Window.Current.Content = rootFrame;
            }
            if (rootFrame.Content == null)
            {
                rootFrame.Navigate(typeof(MainPage), e.Arguments);
            }
            Window.Current.Activate();
        }
    }
}
