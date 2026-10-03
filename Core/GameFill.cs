using System;
using System.Threading.Tasks;

namespace ValorantHelper.Core;

public interface IGameInputPlatform
{
    string CheckTarget();
    bool IsForeground { get; }
    bool ModifiersPressed { get; }
    bool Activate();
    bool ClickInput();
    bool SelectAll();
    bool Paste();
}

public record FillResult(bool Sent, string Message);

public sealed class GameFill
{
    private readonly IGameInputPlatform platform;
    public GameFill(IGameInputPlatform platform) => this.platform = platform;
    public async Task<FillResult> FillAsync(string code, Func<bool> stillCurrent, Func<bool> clipboardMatches)
    {
        if (!CodeExtractor.IsValid(code)) return new(false, "房间码格式不正确");
        string Check(bool requireFocus)
        {
            if (!stillCurrent()) return "识别已暂停，取消填入";
            var error = platform.CheckTarget();
            if (error.Length > 0) return error;
            if (platform.ModifiersPressed) return "请松开 Ctrl / Alt / Shift / Win 后重试";
            if (requireFocus && !platform.IsForeground) return "游戏失去焦点，已停止填入";
            if (!clipboardMatches()) return "剪贴板内容已改变，已停止填入";
            return "";
        }
        var reason = Check(false);
        if (reason.Length > 0) return new(false, reason);
        if (!platform.Activate()) return new(false, "无法切换到游戏，请手动切换后重试");
        await Task.Delay(180);
        reason = Check(true);
        if (reason.Length > 0) return new(false, reason);
        if (!platform.ClickInput()) return new(false, "系统未接受输入框点击");
        await Task.Delay(100);
        reason = Check(true);
        if (reason.Length > 0) return new(false, reason);
        if (!platform.SelectAll()) return new(false, "系统未接受选择输入文字");
        await Task.Delay(40);
        reason = Check(true);
        if (reason.Length > 0) return new(false, reason);
        return platform.Paste() ? new(true, "已发送填入，请在游戏确认") : new(false, "系统未接受粘贴，请手动粘贴");
    }
}
