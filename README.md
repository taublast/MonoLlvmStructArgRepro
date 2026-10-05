# Mono LLVM AOT on Android arm64: a struct argument passed on the stack arrives corrupted

A plain .NET Android app, no other libraries. In a Release build with `RunAOTCompilation` and
`EnableLLVM`, a method receives wrong values for a 4-float struct argument that is passed on the
stack, when the caller and the callee are not both compiled by LLVM. No exception, no crash: the
values are just wrong. With `EnableLLVM=false` the same code is correct.

## The case

```csharp
public struct Rect4 { public float Left, Top, Right, Bottom; }   // an HFA in the arm64 ABI

Rect4 Callee(Rect4 first, float a, float b, Rect4 second) => second;
```

`first` takes v0-v3, `a` and `b` take v4-v5. `second` needs 4 more FP registers, only v6-v7 are left,
so by AAPCS64 it goes on the stack.

A method that contains an exception filter (`catch (Exception e) when (...)`) is compiled without
LLVM. When one side of the call has such a filter and the other does not, `second` is read from the
wrong place:

| Call | `EnableLLVM=true` | `EnableLLVM=false` |
|---|---|---|
| plain caller → plain callee | `[0, 0, 1080, 138]` OK | OK |
| caller with filter → plain callee | `[0, 0, -3.3471348E+29, -3.2095935E+33]` WRONG | OK |
| plain caller → callee with filter | `[0, 0, 0, 0]` WRONG | OK |
| plain caller → callee with filter, `in Rect4 second` | `[0, 0, 1080, 138]` OK | OK |

Passed value: `[0, 0, 1080, 138]` (screen width and 46 dp, read at run time so nothing is constant).
All methods are `[MethodImpl(MethodImplOptions.NoInlining)]`. Code: [MainActivity.cs](MainActivity.cs).

## Run it

```
dotnet build -c Release
adb install -r bin/Release/net10.0-android/android-arm64/com.repro.monollvmstructarg-Signed.apk
adb shell monkey -p com.repro.monollvmstructarg -c android.intent.category.LAUNCHER 1
adb logcat -d -s REPRO
```

The app shows the same lines on screen. For the baseline, delete `bin` and `obj` and build with
`-p:EnableLLVM=false` (switching the property alone does not re-run AOT).

## Environment

- .NET SDK 10.0.401, Microsoft.NETCore.App 10.0.12, android workload 36.1.69/10.0.100, Windows 11 host
- `net10.0-android`, Release, `RunAOTCompilation=true`, `EnableLLVM=true`, `AndroidEnableProfiledAot=false`, `RuntimeIdentifier=android-arm64`
- Device: Blackview BV8800 (MediaTek mt6781, arm64-v8a), Android 11

## Where it was found

In the layout code of [DrawnUI](https://github.com/DrawnUi/DrawnUi.Net), a published .NET MAUI app lost
parts of its UI only in its Play Store build: a layout method received the second `SKRect` of a
`(SKRect, float, object, bool, float, object[], SKRect)` signature as `[0, 1080, -4.87E+14, -8.08E+30]`
while the caller passed `[0, 0, 1080, 138]`, so controls were arranged with a negative width and drew
nothing. There the trigger was not an exception filter; the filter is just a reliable way to get one
side of the call compiled without LLVM. Passing the struct `in` avoids the problem.
