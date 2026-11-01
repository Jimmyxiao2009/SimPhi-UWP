# SimPhi.Uwp — sim-phi 的 UWP / Windows 10 Mobile 移植

把 [`sim-phi`](../sim-phi-main/sim-phi-main)（TypeScript + Vite，Canvas2D + Web Audio 的 Phigros 模拟器）**照抄**重写为 UWP / W10M、C# 7。
渲染严格还原原版写法：`CanvasDrawingSession` 对 `CanvasRenderingContext2D` 近乎逐行对应。

- 渲染后端：**Win2D**（`Win2D.uwp`）
- 音频后端：**XAudio2**（待接，目前为静音桩）
- 最低系统：**10.0.10586.0 (TH2)**，MaxVersionTested 16299
- 语言：**C# 7.3**

## 目录结构

```
SimPhi.Uwp/
├─ Core/        平台无关（已本机编译验证 C#7.3/netstandard2.0）
│   Type.cs        JS Number 语义（|0 截断 / ||0 NaN→0 / 强制转换）
│   Chart.cs       谱面模型（基础+运行期字段合并）+ Duplicate()
│   Clock.cs       performance.now() → Stopwatch 毫秒主时钟
│   Renderer.cs    照抄 core.ts：prerenderChart / updateByTime / transform / getMaxVisiblePos(位运算)
├─ Game/        平台无关（已本机编译验证）
│   Timer / FrameTimer / HitEvents / HitManager(+JudgeEvent) / Stat / JsCompat(进制/localStorage 垫片)
├─ Render/      Win2D（需在 VS/UWP 工具链编译）
│   Ctx.cs          ★ Canvas2D→Win2D 外壳：globalAlpha / globalCompositeOperation / fillText 三难点
│   ScaledNote.cs   note 绘制原语 full/head/body/tail
│   ImageTools.cs   imgShader/imgPainter/imgBlur/imgSplit/ImgAny 解码（GetPixelColors 逐像素）
│   NoteRender.cs   note 皮肤 + 打击特效帧持有者
│   HitEffects.cs   HitImage/HitWord/HitFeedback
│   Res.cs          资源表（图像+音频）
│   Bindings.cs     Settings/Info（原 DOM 控件抽成可绑定对象，UI 由你重做）
│   IAudio.cs       音频抽象（AudioOptions/IAudioController/IAudioSystem）
│   SvgPath.cs      结算页两个 Path2D 按钮的 SVG 路径解析
│   GameRenderer.cs ★ 照抄 index.ts：mainLoop/loopNoCanvas/loopCanvas/drawNotes/drawLine/atDraw3/judgeManager
├─ Audio/       SilentAudio.cs（静音桩，先出画面；待换 XAudio2）
├─ Platform/    UwpLocalStorage.cs（ApplicationData 支撑成绩存储）
├─ App.xaml(.cs) / MainPage.xaml(.cs)  宿主：CanvasAnimatedControl + 游戏循环 + 触摸
├─ Package.appxmanifest / SimPhi.Uwp.csproj
└─ _verify/     仅本机校验 Core+Game 能否以 C#7.3 编译（不进 UWP 工程）
```

## 已完成 + 已验证

- **Core + Game 两层**：照抄完成，`dotnet build`（netstandard2.0 / LangVersion 7.3）**0 警告 0 错误**。
  - 重写时复刻的 JS 语义：`Type` 的 `|0`/`||0`；`Math.fround` 的 float32 取整；`getMaxVisiblePos` 的 `Float32Array`/`Uint32Array` 位重解释（`BitConverter`）；`toFixed`/`toPrecision`/`toString(radix)`/`parseInt(radix)`。
- **Render 层**：照抄完成（Win2D 依赖，本机无 UWP 工具链未编译验证）。双画布模型用 `CanvasRenderTarget`(前景) + 主 session 还原；`setTransform/drawImage/fillText/fillRect` 逐行对应。
- **UWP 外壳**：宿主把 `CanvasAnimatedControl.Draw` 接到 `DrawForeground`(前景 RT) → `DrawMain`(主画布贴前景)；触摸经线程安全队列在渲染线程排空喂 `hitManager`。

## Canvas2D → Win2D 映射（Ctx 封装）

| Canvas2D | Win2D / Ctx |
|---|---|
| `setTransform(a,b,c,d,e,f)` | `ds.Transform = Matrix3x2(...)` |
| `globalAlpha` | 画图塞进 DrawImage opacity；画形状/文字烘进颜色 alpha |
| `globalCompositeOperation` | DrawImage 用 `CanvasComposite`；非 source-over 填充/文字走 `CanvasCommandList` 贴回；`difference`(版权字) 留 BlendEffect |
| `fillText` + `textAlign` | `CanvasTextLayout` 求 baseline + 按对齐换算锚点 x |
| `drawImage` | `ds.DrawImage(bmp, destRect, srcRect, opacity, …)` |
| `fill(new Path2D())` | `SvgPath.Build` → `CanvasGeometry` → `FillGeometry` |
| `imgShader/Blur/Split` | `GetPixelColors` 逐像素 / `GaussianBlurEffect` / `CanvasRenderTarget` 裁切 |

## 剩余工作（让它真正跑起来）

1. **资源包加载**（对应 index.ts 的 `window.load` 处理 + `fixme.ts`）：拉 `pack.json`、解码贴图/隐写音频（`ImageTools.Decode/DecodeAlt` 已就绪）、`noteRender.Update/UpdateFX`、填 `res`、`res.Ranks = ImgSplit(res.Rank)`、置 `MainPage._resourcesLoaded = true`。入口已留在 `MainPage.LoadResourcesAsync`。
2. **谱面/文件读取**（对应 `reader.ts`/`parseCSV`/`structInfo`/zip）：识别 chart(JSON/PEC)、line.csv/info.csv、音频、图片；zip 用 `System.IO.Compression`。解析出 `Chart` 后调用 `MainPage.StartChart(chart, bgm, bgImage, …)`。
3. **XAudio2 音频**（对应 `AudioTools.ts`）：实现 `IAudioBuffer`/`IAudioController`/`IAudioSystem`，替换 `SilentAudio`。SourceVoice 池打多押零延迟，`SetFrequencyRatio` 对 playbackRate，按采样 offset 起播；主时钟仍用 `Clock`。
4. **你的 XAML UI**：上传/设置/播放控制，叠在 `MainPage.xaml` 的 Grid 上，绑定 `Settings`/`Info`，调用 `StartChart`/`Stop`。

## 构建

**推荐**：VS 里新建「空白应用(通用 Windows)」，最低版本选 10586，加 NuGet `Win2D.uwp`，把 `Core/Game/Render/Audio/Platform` 与 `App`/`MainPage`/`Package.appxmanifest` 拖进去。比手写 csproj 稳。

或直接用本目录的 `SimPhi.Uwp.csproj`（需补 `Assets\` 下的 Logo 图，VS 模板自带）。

本机快速校验平台无关层：
```
dotnet build _verify/CoreCheck.csproj
```

## 真机/上手待调点

- `Ctx.FillText` 的 baseline 取自 `CanvasTextLayout.LineMetrics[0].Baseline`，字体回退（`Custom,Noto Sans SC` 取首个族名）在真机上需核对，必要时打进自带字体。
- `imgBlur` 用 `GaussianBlurEffect` 近似 stackblur（背景虚化，非像素级敏感）。
- `DevicePixelRatio` 默认 1（工作在 DIP）。要更锐可设为实际像素比并让前景 RT 用设备像素尺寸。
- `difference` 合成（主画布版权字）当前以 source-over 退化，接 `BlendEffect{Difference}` 可完全还原。
