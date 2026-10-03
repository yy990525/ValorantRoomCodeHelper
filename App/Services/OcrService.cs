using RapidOcrNet;
using SkiaSharp;
using System.Diagnostics;

namespace ValorantHelper.Services;

public sealed record OcrFrame(List<Candidate> Candidates, string Text, long ElapsedMs);

public sealed class OcrService : IDisposable
{
    private readonly RapidOcr engine = new();
    public void Initialize()
    {
        string root=Path.Combine(AppContext.BaseDirectory,"models","v5");
        using var options=RapidOcr.GetDefaultSessionOptions(2);
        engine.InitModels(Path.Combine(root,"ch_PP-OCRv5_mobile_det.onnx"),Path.Combine(root,"ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx"),Path.Combine(root,"latin_PP-OCRv5_rec_mobile_infer.onnx"),Path.Combine(root,"ppocrv5_latin_dict.txt"),options);
    }
    public OcrFrame Recognize(byte[] png, (int X,int Y,int Width,int Height) crop)
    {
        var watch=Stopwatch.StartNew();
        using var full=SKBitmap.Decode(png) ?? throw new InvalidDataException("截图解码失败");
        using var roi=new SKBitmap();
        if(!full.ExtractSubset(roi,SKRectI.Create(crop.X,crop.Y,crop.Width,crop.Height))) throw new InvalidDataException("识别区域超出画面");
        float scale=roi.Height<100 ? Math.Min(3f,100f/roi.Height) : 1;
        using var scaled=new SKBitmap(Math.Max(1,(int)(roi.Width*scale)),Math.Max(1,(int)(roi.Height*scale)));
        using(var canvas=new SKCanvas(scaled))
        {
            canvas.Clear(SKColors.Black);
            using var paint=new SKPaint { IsAntialias=true };
            canvas.DrawBitmap(roi,new SKRect(0,0,scaled.Width,scaled.Height),paint);
        }
        var result=engine.Detect(scaled,RapidOcrOptions.Default with { DoAngle=false, TextScore=.4f, ImgResize=960, Padding=25 });
        var candidates=new List<Candidate>();
        foreach(var block in result.TextBlocks)
        {
            float score=block.CharScores is { Length: > 0 } ? (float)block.CharScores.Average() : 0;
            foreach(string code in CodeExtractor.Extract(block.Text)) candidates.Add(new(code,score));
        }
        return new(candidates,result.StrRes,watch.ElapsedMilliseconds);
    }
    public void Dispose()=>engine.Dispose();
}
