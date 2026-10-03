using ValorantHelper.Services;

namespace ValorantHelper;

public partial class MainWindow
{
    private void UpdateThemeButton()=>ThemeButton.Content=ThemeManager.Current=="Light"?"切换深色":"切换浅色";
    private void Theme_Click(object sender,RoutedEventArgs e)
    {
        settings.Theme=ThemeManager.Current=="Light"?"Dark":"Light";
        ThemeManager.Apply(settings.Theme);UpdateThemeButton();Save();
    }
    private async Task RestoreWebPageAsync(int session)
    {
        try
        {
            await Browser.CoreWebView2.ExecuteScriptAsync("window.__vrhPlayer?.exit()");
            if(closed||session!=generation)return;
            pure=false;RefreshControls();
            RecognitionHint.Text="识别已暂停，网页弹幕和聊天已恢复";
            if(RegionPreview.Source is not null&&!PreviewLabel.Text.Contains("暂停前"))PreviewLabel.Text+=" · 暂停前最后一帧";
        }
        catch(Exception ex)
        {
            if(closed||session!=generation)return;
            storage.Log("恢复网页失败",ex);SetStatus("识别已暂停，网页恢复失败，请刷新网页");
        }
    }
}
