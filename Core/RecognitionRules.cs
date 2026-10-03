using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ValorantHelper.Core;

public record Candidate(string Code, float Confidence);
public enum RecognitionState { Waiting, LowConfidence, Conflict, Confirming, Stable, Duplicate }
public record RecognitionDecision(RecognitionState State, Candidate? Candidate = null);

public static class CodeExtractor
{
    // OCR may insert a space between the letter and number groups. Never guess O/0 or I/1.
    private static readonly Regex Pattern = new(@"(?<![A-Za-z0-9])([A-Z]{3})[ \t]*([0-9]{3})(?![A-Za-z0-9])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    public static IEnumerable<string> Extract(string text) => Pattern.Matches(text).Select(m => m.Groups[1].Value + m.Groups[2].Value).Distinct();
    public static bool IsValid(string code) => code.Length == 6 && code.Take(3).All(c => c is >= 'A' and <= 'Z') && code.Skip(3).All(c => c is >= '0' and <= '9');
}

public sealed class RecognitionGate
{
    private readonly Dictionary<string, DateTimeOffset> published = new();
    private string? pending;
    private int count;
    private DateTimeOffset previous;
    private string? lastPublished;
    public int RequiredFrames { get; set; } = 2;
    public float MinimumConfidence { get; set; } = .85f;
    public TimeSpan DuplicateCooldown { get; set; } = TimeSpan.FromSeconds(60);
    public TimeSpan MaxFrameGap { get; set; } = TimeSpan.FromSeconds(3);

    public RecognitionDecision Observe(IEnumerable<Candidate> source, DateTimeOffset now)
    {
        var valid = source.Where(c => CodeExtractor.IsValid(c.Code)).GroupBy(c => c.Code).Select(g => g.MaxBy(c => c.Confidence)!).ToList();
        if (valid.Count == 0) { ResetPending(); return new(RecognitionState.Waiting); }
        // Even a low-confidence alternative is a conflict: never silently pick one of two codes.
        if (valid.Count > 1) { ResetPending(); return new(RecognitionState.Conflict); }
        var candidate = valid[0];
        if (!float.IsFinite(candidate.Confidence) || candidate.Confidence < MinimumConfidence) { ResetPending(); return new(RecognitionState.LowConfidence, candidate); }
        if (pending == candidate.Code && now >= previous && now - previous <= MaxFrameGap) count++;
        else { pending = candidate.Code; count = 1; }
        previous = now;
        if (count < Math.Max(1, RequiredFrames)) return new(RecognitionState.Confirming, candidate);
        if (candidate.Code == lastPublished || (published.TryGetValue(candidate.Code, out var seen) && now - seen < DuplicateCooldown)) return new(RecognitionState.Duplicate, candidate);
        return new(RecognitionState.Stable, candidate);
    }

    public void MarkPublished(string code, DateTimeOffset now)
    {
        lastPublished = code;
        published[code] = now;
        foreach (var key in published.Where(p => now - p.Value > DuplicateCooldown + TimeSpan.FromMinutes(5)).Select(p => p.Key).ToArray()) published.Remove(key);
    }
    public void ResetPending() { pending = null; count = 0; previous = default; }
}

public record NormalizedRegion(double X, double Y, double Width, double Height)
{
    public bool IsValid => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Width) && double.IsFinite(Height) && X >= 0 && Y >= 0 && Width > .005 && Height > .005 && X + Width <= 1.00001 && Y + Height <= 1.00001;
    public (int X, int Y, int Width, int Height) Map(double left, double top, double width, double height, int imageWidth, int imageHeight)
    {
        if (!IsValid || width <= 0 || height <= 0 || imageWidth <= 0 || imageHeight <= 0) throw new ArgumentException("Invalid video/region dimensions");
        const double epsilon = 1e-7; // Avoid a one-pixel drift from .2 + .1 floating-point rounding.
        int x = Math.Clamp((int)Math.Floor(left + X * width + epsilon), 0, imageWidth - 1);
        int y = Math.Clamp((int)Math.Floor(top + Y * height + epsilon), 0, imageHeight - 1);
        int right = Math.Clamp((int)Math.Ceiling(left + (X + Width) * width - epsilon), x + 1, imageWidth);
        int bottom = Math.Clamp((int)Math.Ceiling(top + (Y + Height) * height - epsilon), y + 1, imageHeight);
        return (x, y, right - x, bottom - y);
    }
}
