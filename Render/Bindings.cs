namespace SimPhi.Render
{
    /// <summary>
    /// 原版散落在 DOM 上的勾选框/下拉框/输入框，抽成可绑定对象，由用户重做的 XAML UI 驱动。
    /// 字段名对应原控件：showTransition / autoplay / showStat / showPoint / showAcc /
    /// showCE2(Early/Late特效) / feedback(打击反馈) / imageBlur / lineColor(彩色判定线) / inputOffset / selectspeed。
    /// </summary>
    public sealed class Settings
    {
        public bool Autoplay = true;         // 自动播放 → app.playMode(1=auto,0=game)
        public bool ShowTransition = true;   // showTransition
        public bool ShowStat = false;        // 显示统计
        public bool ShowPoint = false;       // 显示定位点
        public bool ShowAcc = false;         // 显示 Acc
        public bool ShowCE2 = false;         // Early/Late 特效
        public bool Feedback = false;        // 打击反馈
        public bool ImageBlur = false;       // 背景模糊
        public bool LineColor = false;       // 彩色判定线
        public bool AutoDelay = true;        // 音画实时同步（playBgm 的 interval）
        public double Offset = 0;            // inputOffset，单位 ms
        public string Speed = "";            // selectspeed 当前值（""/slowest/slower/faster/fastest）
    }

    /// <summary>曲目信息（对应 inputName/artist/illustrator/charter + 难度文本）。</summary>
    public sealed class Info
    {
        public string Name = "";
        public string Artist = "";
        public string Illustrator = "";
        public string Charter = "";
        public string LevelText = "";       // levelInfoHandler.text
        public int DifficultyIndex = -1;    // levelInfoHandler.getDifficultyIndex（<0 时结算音用 index 2）
        public double LevelNumber = 0;      // levelInfoHandler.getLevelNumber → stat.level

        public string NamePlaceholder = "Unknown";
        public string IllustratorPlaceholder = "Unknown";
        public string CharterPlaceholder = "Unknown";

        public string NameOrPlaceholder { get { return string.IsNullOrEmpty(Name) ? NamePlaceholder : Name; } }
        public string IllustratorOrPlaceholder { get { return string.IsNullOrEmpty(Illustrator) ? IllustratorPlaceholder : Illustrator; } }
        public string CharterOrPlaceholder { get { return string.IsNullOrEmpty(Charter) ? CharterPlaceholder : Charter; } }
    }
}
