using System.Runtime.CompilerServices;

namespace MonoLlvmStructArgRepro;

/// <summary>
/// 4 floats: a homogeneous floating-point aggregate (HFA) in the arm64 ABI, like SkiaSharp's SKRect.
/// </summary>
public struct Rect4
{
    public float Left, Top, Right, Bottom;

    public Rect4(float left, float top, float right, float bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    public override string ToString() => $"[{Left}, {Top}, {Right}, {Bottom}]";
}

/// <summary>
/// In every method below `first` takes v0-v3 and the two floats take v4-v5. `second` needs 4 more FP
/// registers, only v6-v7 are left, so by AAPCS64 it is passed on the stack.
/// A method containing an exception filter (catch ... when) is compiled without LLVM. When the caller and
/// the callee were compiled by different backends, `second` arrives corrupted.
/// </summary>
public class Calls
{
    public static int Threshold;

    // both methods compiled with LLVM: correct
    [MethodImpl(MethodImplOptions.NoInlining)]
    public Rect4 PlainCaller(Rect4 first, float a, float b, Rect4 second) => PlainCallee(first, a, b, second);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public Rect4 PlainCallee(Rect4 first, float a, float b, Rect4 second) => second;

    // the caller has an exception filter, the callee is plain: WRONG
    [MethodImpl(MethodImplOptions.NoInlining)]
    public Rect4 CallerWithFilter(Rect4 first, float a, float b, Rect4 second)
    {
        try
        {
            return PlainCallee(first, a, b, second);
        }
        catch (Exception e) when (e.Message.Length > Threshold)
        {
            return default;
        }
    }

    // the caller is plain, the callee has an exception filter: WRONG
    [MethodImpl(MethodImplOptions.NoInlining)]
    public Rect4 CallerOfFilteredCallee(Rect4 first, float a, float b, Rect4 second) => CalleeWithFilter(first, a, b, second);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public Rect4 CalleeWithFilter(Rect4 first, float a, float b, Rect4 second)
    {
        try
        {
            return second;
        }
        catch (Exception e) when (e.Message.Length > Threshold)
        {
            return default;
        }
    }

    // workaround: the same mixed pair with the struct passed by reference: correct
    [MethodImpl(MethodImplOptions.NoInlining)]
    public Rect4 CallerOfFilteredCalleeByRef(Rect4 first, float a, float b, Rect4 second) => CalleeWithFilterByRef(first, a, b, second);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public Rect4 CalleeWithFilterByRef(Rect4 first, float a, float b, in Rect4 second)
    {
        try
        {
            return second;
        }
        catch (Exception e) when (e.Message.Length > Threshold)
        {
            return default;
        }
    }
}

[Activity(Label = "@string/app_name", MainLauncher = true)]
public class MainActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // runtime values, so the compiler cannot propagate constants into the callees
        float width = Resources!.DisplayMetrics!.WidthPixels;
        float height = Resources!.DisplayMetrics!.Density * 46;

        var first = new Rect4(0, 0, width, height);
        var second = new Rect4(0, 0, width, height);
        var calls = new Calls();

        string Line(string name, Rect4 received)
        {
            var ok = received.Left == second.Left && received.Top == second.Top
                     && received.Right == second.Right && received.Bottom == second.Bottom;
            return $"{name}: {received}  {(ok ? "OK" : "WRONG")}";
        }

        var lines = new[]
        {
            $"passed: {second}",
            Line("plain caller -> plain callee", calls.PlainCaller(first, width, 0f, second)),
            Line("caller with filter -> plain callee", calls.CallerWithFilter(first, width, 0f, second)),
            Line("plain caller -> callee with filter", calls.CallerOfFilteredCallee(first, width, 0f, second)),
            Line("plain caller -> callee with filter, in", calls.CallerOfFilteredCalleeByRef(first, width, 0f, second)),
        };

        foreach (var line in lines)
            Android.Util.Log.Warn("REPRO", line);

        SetContentView(new TextView(this)
        {
            Text = string.Join("\n\n", lines),
            TextSize = 15,
            Typeface = Android.Graphics.Typeface.Monospace
        });
    }
}
