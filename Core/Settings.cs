using System;
using System.Collections.Generic;

namespace ValorantHelper.Core;

public sealed class AppSettings
{
    public string LastUrl { get; set; } = "https://live.douyin.com/";
    public bool AutoCopy { get; set; } = true;
    public bool FullVideo { get; set; } = true;
    public bool AutoFill { get; set; }
    public string Theme { get; set; } = "Dark";
    public bool Sound { get; set; } = true;
    public bool AlwaysOnTop { get; set; }
    public bool StableConfirm { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public int IntervalMs { get; set; } = 350;
    public float MinimumConfidence { get; set; } = .85f;
    public int CooldownSeconds { get; set; } = 60;
    public int HistoryLimit { get; set; } = 100;
    public int ToggleKey { get; set; } = 8;
    public int CopyKey { get; set; } = 9;
    public Dictionary<string, NormalizedRegion> Regions { get; set; } = new();
    public List<HistoryItem> History { get; set; } = new();
    public void Sanitize()
    {
        Theme = Theme == "Light" ? "Light" : "Dark";
        IntervalMs = Math.Clamp(IntervalMs, 150, 3000);
        MinimumConfidence = float.IsFinite(MinimumConfidence) ? Math.Clamp(MinimumConfidence, .5f, .99f) : .85f;
        CooldownSeconds = Math.Clamp(CooldownSeconds, 0, 3600);
        HistoryLimit = Math.Clamp(HistoryLimit, 10, 500);
        ToggleKey = Math.Clamp(ToggleKey, 6, 12);
        CopyKey = Math.Clamp(CopyKey, 6, 12);
        if (ToggleKey == CopyKey) CopyKey = ToggleKey == 9 ? 8 : 9;
        if (AutoFill) { AutoCopy = true; AlwaysOnTop = false; }
        Regions ??= new(); History ??= new();
        History.RemoveAll(h => !CodeExtractor.IsValid(h.Code));
        if (History.Count > HistoryLimit) History.RemoveRange(HistoryLimit, History.Count - HistoryLimit);
    }
}

public sealed class HistoryItem
{
    public string Code { get; set; } = "";
    public DateTimeOffset Time { get; set; }
    public string Status { get; set; } = "已发现";
    public string DisplayTime => Time.ToLocalTime().ToString("HH:mm:ss");
    public string Source { get; set; } = "游戏画面";
}
