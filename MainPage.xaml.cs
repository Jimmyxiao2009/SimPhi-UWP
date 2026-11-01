using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using SimPhi.Core;
using SimPhi.Game;
using SimPhi.Render;
using SimPhi.Audio;
using SimPhi.Platform;

namespace SimPhi.Uwp
{
    public sealed partial class MainPage : Page
    {
        private readonly Renderer app;
        private readonly Stat stat;
        private readonly HitManager hitManager;
        private readonly Res res;
        private readonly NoteRender noteRender;
        private readonly Settings settings;
        private readonly Info info;
        private readonly GameRenderer game;
        private readonly IAudioSystem audio;

        private CanvasRenderTarget _fgRT;
        private volatile bool _resourcesLoaded;
        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        // 内存诊断/探针：每 ~300 帧记一次 app 真实用量峰值（无 GC）
        private int _frameCnt;
        private long _peakMem;
        // 内部渲染分辨率系数（软渲下降低以保帧率）。0.5 = 半边长 = 1/4 像素
        private double RenderScale = 0.5;

        // UI 线程的指针事件 → 游戏线程在 OnDraw 开头排空，避开竞态
        private readonly ConcurrentQueue<Action> _inputQueue = new ConcurrentQueue<Action>();

        public MainPage()
        {
            InitializeComponent();

            // 崩溃根因（崩溃转储铁证）：高通 Adreno D3D11 用户态驱动 qcdx11um8994!OpenAdapter10_2 的确定性 AV，
            // 由某个 D3D11 调用(d3d11+0x78cae)触发，改渲染细节/帧率都绕不过。唯一可靠解：不用这颗显卡驱动 →
            // ForceSoftwareRenderer 走 WARP 软件渲染，彻底绕开。配合 30fps 固定步长控负载。
            GameCanvas.ForceSoftwareRenderer = true;
            GameCanvas.TargetElapsedTime = TimeSpan.FromTicks(333333); // ~30fps
            GameCanvas.IsFixedTimeStep = true;

            LocalStorage.Backend = new UwpLocalStorage();
            settings = new Settings();
            SettingsStore.Load(settings);
            info = new Info();
            res = new Res();
            noteRender = new NoteRender();
            stat = new Stat();
            hitManager = new HitManager();
            app = new Renderer();
            app.playMode = settings.Autoplay ? 1 : 0;
            audio = new SimPhi.Audio.RealAudio();

            game = new GameRenderer(app, stat, hitManager, res, noteRender, settings, info)
            {
                audio = audio
            };
            game.musicController = audio.CreateController();
            game.soundController = audio.CreateController();
        }

        // ===== 资源加载 =====
        private void OnCreateResources(ICanvasAnimatedControl sender, CanvasCreateResourcesEventArgs args)
        {
            args.TrackAsyncAction(LoadResourcesAsync(sender.Device).AsAsyncAction());
        }

        private async Task LoadResourcesAsync(CanvasDevice device)
        {
            // 先拉在线资源包(官方皮肤)，离线/失败自动回退到程序化兜底
            await SimPhi.Resource.Resources.GenerateAsync(device, res, noteRender, audio, _http);
            _resourcesLoaded = true;
            // 不自动开谱：进入开始菜单，由用户选谱/测试谱
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => ShowMenu(true));
        }

        private void ShowMenu(bool show)
        {
            StartPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            MenuButton.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        }

        private void OnTestChart(object sender, RoutedEventArgs e)
        {
            info.Name = ""; info.Artist = ""; info.Charter = ""; info.Illustrator = "";
            info.LevelText = ""; info.DifficultyIndex = -1; info.LevelNumber = 0;
            _inputQueue.Enqueue(() =>
            {
                Stop();
                StartChart(SimPhi.Resource.TestChart.Build(), null, null, "00000000000000000000000000000000", "PGS(3)", 0);
            });
            ShowMenu(false);
        }

        private void OnMenu(object sender, RoutedEventArgs e)
        {
            _inputQueue.Enqueue(() => Stop());
            ShowMenu(true);
        }

        // ===== 设置面板 =====
        private bool _suppressToggle;

        private void OnOpenSettings(object sender, RoutedEventArgs e)
        {
            SyncToggles();
            StartPanel.Visibility = Visibility.Collapsed;
            SettingsPanel.Visibility = Visibility.Visible;
        }

        private void OnSettingsBack(object sender, RoutedEventArgs e)
        {
            SettingsPanel.Visibility = Visibility.Collapsed;
            StartPanel.Visibility = Visibility.Visible;
        }

        private void SyncToggles()
        {
            _suppressToggle = true;
            foreach (var child in SettingsList.Children)
            {
                var ts = child as ToggleSwitch;
                if (ts == null) continue;
                ts.IsOn = GetSetting(ts.Tag as string);
            }
            OffsetSlider.Value = settings.Offset;
            OffsetLabel.Text = "音频偏移: " + ((int)settings.Offset) + " ms";
            _suppressToggle = false;
        }

        private void OnOffsetChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_suppressToggle) return;
            settings.Offset = e.NewValue;
            OffsetLabel.Text = "音频偏移: " + ((int)e.NewValue) + " ms";
            SettingsStore.Save(settings);
        }

        private bool GetSetting(string key)
        {
            switch (key)
            {
                case "Autoplay": return settings.Autoplay;
                case "LineColor": return settings.LineColor;
                case "ShowAcc": return settings.ShowAcc;
                case "ShowStat": return settings.ShowStat;
                case "ShowPoint": return settings.ShowPoint;
                case "ShowCE2": return settings.ShowCE2;
                case "Feedback": return settings.Feedback;
                case "ImageBlur": return settings.ImageBlur;
                case "ShowTransition": return settings.ShowTransition;
                default: return false;
            }
        }

        private void OnSettingToggled(object sender, RoutedEventArgs e)
        {
            if (_suppressToggle) return;
            var ts = (ToggleSwitch)sender;
            string key = ts.Tag as string;
            bool on = ts.IsOn;
            switch (key)
            {
                case "Autoplay": settings.Autoplay = on; app.playMode = on ? 1 : 0; break;
                case "LineColor": settings.LineColor = on; break;
                case "ShowAcc": settings.ShowAcc = on; break;
                case "ShowStat": settings.ShowStat = on; break;
                case "ShowPoint": settings.ShowPoint = on; break;
                case "ShowCE2": settings.ShowCE2 = on; break;
                case "Feedback": settings.Feedback = on; break;
                case "ImageBlur": settings.ImageBlur = on; break;
                case "ShowTransition": settings.ShowTransition = on; break;
            }
            SettingsStore.Save(settings);
        }

        // ===== 游戏循环（CanvasAnimatedControl 的渲染线程）=====
        private void OnDraw(ICanvasAnimatedControl sender, CanvasAnimatedDrawEventArgs args)
        {
            try
            {
                DrawFrame(sender, args);
            }
            catch (System.Exception ex)
            {
                // 渲染线程异常：落盘后吞掉本帧（崩溃改成可读日志，便于定位「打击特效一多就闪退」）
                CrashLog.Write("OnDraw", ex);
                try { args.DrawingSession.Clear(Colors.Black); } catch { }
            }
        }

        private void DrawFrame(ICanvasAnimatedControl sender, CanvasAnimatedDrawEventArgs args)
        {
            var ds = args.DrawingSession;

            // 排空输入（游戏线程内应用，避免与 UI 线程竞态）
            Action act;
            while (_inputQueue.TryDequeue(out act)) act();

            if (game.background.Device == null) game.background.Device = sender.Device;

            double w = sender.Size.Width, h = sender.Size.Height;
            // 软件渲染(WARP)下降内部渲染分辨率：像素数 ~1/4，CPU 软渲提速约 4 倍，换取可玩帧率（画面略软）。
            app.DevicePixelRatio = RenderScale;
            app.ResizeCanvas(w, h);

            if (!_resourcesLoaded || game.Emitter == Phase.Stop)
            {
                ds.Clear(Colors.Black);
                return;
            }

            EnsureForeground(sender);
            if (_fgRT == null) { ds.Clear(Colors.Black); return; }

            // 先画前景画布（独立 RT），关掉它的 session 再到主画布采样
            using (var fgds = _fgRT.CreateDrawingSession())
            {
                fgds.Clear(Colors.Transparent);
                var ctxfg = new Ctx(fgds);
                game.DrawForeground(ctxfg);
            }
            var ctx = new Ctx(ds);
            game.DrawMain(ctx, _fgRT);

            // 轻量内存探针（无 GC、无卡顿）：游戏中每 ~300 帧记一次 app 真实用量与峰值
            if (game.Emitter != Phase.Stop && (++_frameCnt % 300) == 0)
            {
                long use = (long)(Windows.System.MemoryManager.AppMemoryUsage / (1024UL * 1024));
                if (use > _peakMem) { _peakMem = use; CrashLog.Mark("appmem PEAK " + use + "MB @f" + _frameCnt); }
            }
        }

        private void EnsureForeground(ICanvasResourceCreator device)
        {
            int fw = app.CanvasFgWidth, fh = app.CanvasFgHeight;
            if (fw <= 0 || fh <= 0) return;
            if (_fgRT == null || (int)_fgRT.SizeInPixels.Width != fw || (int)_fgRT.SizeInPixels.Height != fh)
            {
                if (_fgRT != null) _fgRT.Dispose();
                _fgRT = new CanvasRenderTarget(device, fw, fh, 96);
            }
        }

        // ===== 播放控制（对应 mainPlay 的 stop→play 分支）=====
        public void StartChart(Chart chart, IAudioBuffer bgm, CanvasBitmap bgImage,
            string md5 = "", string format = "", double levelNumber = 0)
        {
            app.bgMusic = bgm;
            app.bgVideo = null;
            app.PrerenderChart(chart);
            stat.level = levelNumber;
            if (app.chart != null) stat.reset(app.chart.numOfNotes, md5, format, settings.Speed);
            game.SetupDefaultLineImages();
            game.duration0 = app.Duration / app.speed;
            game.ResetPlayState();
            background.SetBase(bgImage);
            if (!settings.ShowTransition) game.timeIn.AddTime(3000);
            audio.Play(res.Mute, AudioOptions.Make(loop: true));
            game.timeIn.Play();
            game.Emitter = Phase.Play;
        }

        public void Stop()
        {
            game.Emitter = Phase.Stop;
            audio.Stop();
            game.ClearHitEffects();
            game.timeIn.Reset();
            game.timeOut.Reset();
            game.timeEnd.Reset();
            game.ResetPlayState();
        }

        private Background background { get { return game.background; } }

        // ===== 加载并开始一首 .pez =====
        private async Task LoadAndStart(StorageFile file)
        {
            LoadingText.Visibility = Visibility.Visible;
            try
            {
                var device = GameCanvas.Device;
                Loader.PezData data;
                using (var stream = await file.OpenAsync(FileAccessMode.Read))
                    data = await Loader.PezLoader.LoadAsync(device, stream);

                info.Name = data.Name;
                info.Artist = data.Artist;
                info.Charter = data.Charter;
                info.Illustrator = "";
                info.LevelText = data.LevelText;
                info.DifficultyIndex = data.DifficultyIndex;
                info.LevelNumber = data.LevelNumber;

                // 在渲染线程换谱，避免与 OnDraw 竞态（StartChart 会改 app.lines 等）
                _inputQueue.Enqueue(() =>
                {
                    Stop();
                    StartChart(data.Chart, data.Music, data.Background, data.Md5, data.Format, data.LevelNumber);
                });
                StartPanel.Visibility = Visibility.Collapsed;
                SettingsPanel.Visibility = Visibility.Collapsed;
                LibraryPanel.Visibility = Visibility.Collapsed;
                ShowMenu(false);
            }
            catch (Exception ex)
            {
                await new MessageDialog(ex.ToString(), "谱面加载失败").ShowAsync();
            }
            finally
            {
                LoadingText.Visibility = Visibility.Collapsed;
            }
        }

        private async void OnPickChart(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.Downloads };
            picker.FileTypeFilter.Add(".pez");
            picker.FileTypeFilter.Add(".zip");
            var file = await picker.PickSingleFileAsync();
            if (file != null) await LoadAndStart(file);
        }

        // ===== 谱面库 =====
        private async void OnOpenLibrary(object sender, RoutedEventArgs e)
        {
            StartPanel.Visibility = Visibility.Collapsed;
            LibraryPanel.Visibility = Visibility.Visible;
            await RefreshLibrary();
        }

        private void OnLibraryBack(object sender, RoutedEventArgs e)
        {
            LibraryPanel.Visibility = Visibility.Collapsed;
            StartPanel.Visibility = Visibility.Visible;
        }

        private async Task RefreshLibrary()
        {
            var items = await SimPhi.Library.ChartLibrary.ListAsync();
            ChartGrid.ItemsSource = items;
            LibraryEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void OnImport(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.Downloads };
            picker.FileTypeFilter.Add(".pez");
            picker.FileTypeFilter.Add(".zip");
            var files = await picker.PickMultipleFilesAsync();
            if (files == null || files.Count == 0) return;
            LoadingText.Visibility = Visibility.Visible;
            foreach (var f in files) { try { await SimPhi.Library.ChartLibrary.ImportAsync(f); } catch { } }
            LoadingText.Visibility = Visibility.Collapsed;
            await RefreshLibrary();
        }

        private async void OnChartItemClick(object sender, ItemClickEventArgs e)
        {
            var item = e.ClickedItem as SimPhi.Library.ChartItem;
            if (item == null) return;
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(item.FilePath);
                await LoadAndStart(file);
            }
            catch (Exception ex)
            {
                await new MessageDialog(ex.ToString(), "打开失败").ShowAsync();
            }
        }

        // ===== 触摸输入 → hitManager（'touch'）=====
        // 关键：坐标基准用 null（相对窗口），不能用 GameCanvas（CanvasAnimatedControl 渲染控件）。
        // 在 UI 线程上对正被渲染线程高速渲染的该控件做坐标变换会原生层并发访问 → 竞态崩溃（戳就炸）。
        // GameCanvas 铺满全窗口，故相对窗口坐标与相对控件坐标一致。
        private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            try
            {
                var p = e.GetCurrentPoint(null).Position;
                object id = (int)e.Pointer.PointerId;
                double x = p.X - (app.CanvasWidth - app.CanvasFgWidth) / 2.0, y = p.Y;
                _inputQueue.Enqueue(() => hitManager.Activate(HitType.Touch, id, x, y));
            }
            catch (System.Exception ex) { CrashLog.Write("OnPointerPressed", ex); }
        }
        private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            try
            {
                if (e.Pointer.IsInContact == false) return;
                var p = e.GetCurrentPoint(null).Position;
                object id = (int)e.Pointer.PointerId;
                double x = p.X - (app.CanvasWidth - app.CanvasFgWidth) / 2.0, y = p.Y;
                _inputQueue.Enqueue(() => hitManager.Moving(HitType.Touch, id, x, y));
            }
            catch (System.Exception ex) { CrashLog.Write("OnPointerMoved", ex); }
        }
        private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            try
            {
                object id = (int)e.Pointer.PointerId;
                _inputQueue.Enqueue(() => hitManager.Deactivate(HitType.Touch, id));
            }
            catch (System.Exception ex) { CrashLog.Write("OnPointerReleased", ex); }
        }
        private void OnPointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            try
            {
                object id = (int)e.Pointer.PointerId;
                _inputQueue.Enqueue(() => hitManager.Deactivate(HitType.Touch, id));
            }
            catch (System.Exception ex) { CrashLog.Write("OnPointerCanceled", ex); }
        }
    }
}
