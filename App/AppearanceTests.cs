using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using ValorantHelper.Services;

namespace ValorantHelper;

public partial class MainWindow
{
    private static double Contrast(Brush foreground,Brush background)
    {
        static double Luma(Brush brush)
        {
            var c=((SolidColorBrush)brush).Color;
            double Channel(byte b){double x=b/255d;return x<=.04045?x/12.92:Math.Pow((x+.055)/1.055,2.4);}
            return .2126*Channel(c.R)+.7152*Channel(c.G)+.0722*Channel(c.B);
        }
        double a=Luma(foreground),b=Luma(background);
        return(Math.Max(a,b)+.05)/(Math.Min(a,b)+.05);
    }
    private static BitmapSource RenderElement(FrameworkElement element)
    {
        element.UpdateLayout();
        var image=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),(int)Math.Ceiling(element.ActualHeight),96,96,PixelFormats.Pbgra32);
        image.Render(element);return image;
    }
    private static void SaveBitmap(BitmapSource image,string path)
    {
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var stream=File.Create(path);encoder.Save(stream);
    }
    private async Task VerifyAppearanceAsync(List<string> report)
    {
        await Browser.CoreWebView2.ExecuteScriptAsync("document.getElementById('gift-overlay-test')?.remove()");
        if(Icon is null)throw new Exception("窗口图标未加载");
        ApplyTemplate();
        var caption=(Border)Template.FindName("WindowFrame",this);
        if(caption is null||WindowStyle!=WindowStyle.None)throw new Exception("主题标题栏未加载");
        report.Add("PASS embedded program icon and themed window chrome loaded");
        foreach(string theme in new[]{"Dark","Light"})
        {
            ThemeManager.Apply(theme);UpdateThemeButton();UpdateLayout();await Task.Delay(100);
            if(Contrast(LatestCode.Foreground,Background)<4.5)throw new Exception(theme+" 主界面文字对比度不足");
            TextBlock? FindText(DependencyObject parent)
            {
                if(parent is TextBlock text&&!string.IsNullOrEmpty(text.Text))return text;
                for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var found=FindText(VisualTreeHelper.GetChild(parent,i));if(found is not null)return found;}
                return null;
            }
            var primaryText=FindText(ToggleButton)??throw new Exception("主按钮文字未渲染");
            if(Contrast(primaryText.Foreground,ToggleButton.Background)<4.5)throw new Exception(theme+" 主按钮文字对比度不足");
            await SaveUiSnapshotAsync(Path.Combine(storage.Root,"ui-"+theme.ToLowerInvariant()+".png"));
            var dialog=new SettingsWindow(settings,storage.Root){Owner=this};
            try
            {
                dialog.Show();dialog.ApplyTemplate();dialog.UpdateLayout();await Task.Delay(100);
                if(Contrast(dialog.ToggleKeyBox.Foreground,dialog.ToggleKeyBox.Background)<4.5||Contrast(dialog.IntervalBox.Foreground,dialog.IntervalBox.Background)<4.5)
                    throw new Exception(theme+" 设置文字对比度不足");
                dialog.ToggleKeyBox.IsDropDownOpen=true;await Task.Delay(250);dialog.UpdateLayout();
                var popup=(Popup)dialog.ToggleKeyBox.Template.FindName("PART_Popup",dialog.ToggleKeyBox);
                var panel=(Border)popup.Child;
                var option=(ComboBoxItem)dialog.ToggleKeyBox.ItemContainerGenerator.ContainerFromIndex(0);
                if(option is null||Contrast(option.Foreground,panel.Background)<4.5)throw new Exception(theme+" 下拉列表对比度不足");
                if(Contrast(FindText(option)!.Foreground,panel.Background)<4.5)throw new Exception(theme+" 实际下拉文字对比度不足");
                var saveButton=FindSaveButton(dialog);
                if(saveButton is null||Contrast(FindText(saveButton)!.Foreground,saveButton.Background)<4.5)throw new Exception(theme+" 保存按钮对比度不足");
                var body=RenderElement(dialog);var dropdown=RenderElement(panel);
                var origin=dialog.PointToScreen(new Point());var position=panel.PointToScreen(new Point());
                var transform=PresentationSource.FromVisual(dialog).CompositionTarget.TransformFromDevice;
                var offset=transform.Transform(position-origin);
                var visual=new DrawingVisual();
                using(var context=visual.RenderOpen())
                {
                    context.DrawImage(body,new Rect(0,0,body.PixelWidth,body.PixelHeight));
                    context.DrawImage(dropdown,new Rect(offset.X,offset.Y,panel.ActualWidth,panel.ActualHeight));
                }
                var composite=new RenderTargetBitmap(body.PixelWidth,body.PixelHeight,96,96,PixelFormats.Pbgra32);composite.Render(visual);
                SaveBitmap(composite,Path.Combine(storage.Root,"settings-"+theme.ToLowerInvariant()+".png"));
                if(dialog.ToggleKeyBox.SelectedItem is not string key||key!="F8")throw new Exception("快捷键选择未正确显示");
                dialog.ToggleKeyBox.SelectedItem="F10";
                if(dialog.ToggleKeyBox.SelectedItem?.ToString()!="F10")throw new Exception("下拉选择失败");
                report.Add("PASS "+theme+" theme updates main UI, title bar, settings, inputs and readable dropdown (contrast >= 4.5)");
            }
            finally {dialog.ToggleKeyBox.IsDropDownOpen=false;dialog.Close();}
        }
        Theme_Click(this,new RoutedEventArgs());
        if(ThemeManager.Current!="Dark"||settings.Theme!="Dark"||running||pure)throw new Exception("主题切换按钮未生效或改变了识别状态");
        report.Add("PASS theme toggle switches appearance without navigating or starting recognition");
    }
    private static Button? FindSaveButton(DependencyObject parent)
    {
        if(parent is Button button&&button.Content?.ToString()=="保存设置")return button;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var found=FindSaveButton(VisualTreeHelper.GetChild(parent,i));if(found is not null)return found;}
        return null;
    }
}
