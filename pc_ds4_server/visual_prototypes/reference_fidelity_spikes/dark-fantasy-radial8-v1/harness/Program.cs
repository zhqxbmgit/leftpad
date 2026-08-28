using System.Collections;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using PcDs4Server;

static object RequiredProperty(object owner, string name) =>
    owner.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(owner)
    ?? throw new InvalidOperationException($"Missing property {owner.GetType().FullName}.{name}");

static int Count(object sequence) => ((IEnumerable)sequence).Cast<object>().Count();

if (args.Length != 2)
{
    Console.Error.WriteLine("usage: SpikeLoaderHarness <package> <radial-8 V1 authority>");
    return 2;
}

string packagePath = Path.GetFullPath(args[0]);
string authorityPath = Path.GetFullPath(args[1]);
RadialVisualPackDefinition authority = RadialVisualPackDefinition.Load(authorityPath);
Assembly assembly = typeof(RadialVisualPackDefinition).Assembly;
Type loaderType = assembly.GetType("PcDs4Server.UiThemeV2Loader", throwOnError: true)!;
MethodInfo load = loaderType.GetMethod("Load", BindingFlags.Static | BindingFlags.Public)
    ?? throw new InvalidOperationException("UiThemeV2Loader.Load was not found.");
object package;
try
{
    package = load.Invoke(null, new object[] { packagePath, authority.LayoutDefinition })
        ?? throw new InvalidOperationException("UiThemeV2Loader returned null.");
}
catch (TargetInvocationException exception) when (exception.InnerException != null)
{
    throw exception.InnerException;
}

object manifest = RequiredProperty(package, "Manifest");
object plan = RequiredProperty(package, "Plan");
object fullState = RequiredProperty(plan, "RenderModel");
object canvas = RequiredProperty(plan, "ReferenceCanvas");
object scale = RequiredProperty(plan, "ReferenceScale");
object placement = RequiredProperty(plan, "Placement");
object anchor = RequiredProperty(placement, "ActivationAnchor");

string id = (string)RequiredProperty(manifest, "Id");
int sourceProtocol = (int)RequiredProperty(plan, "SourceProtocolVersion");
int layers = Count(RequiredProperty(fullState, "OrderedLayers"));
int states = Count(RequiredProperty(fullState, "States"));
int staticLayers = Count(RequiredProperty(plan, "StaticLayers"));
double logicalWidth = Convert.ToDouble(RequiredProperty(scale, "LogicalWidth"));
double logicalHeight = Convert.ToDouble(RequiredProperty(scale, "LogicalHeight"));
double anchorX = Convert.ToDouble(RequiredProperty(anchor, "X"));
double anchorY = Convert.ToDouble(RequiredProperty(anchor, "Y"));
int canvasWidth = Convert.ToInt32(RequiredProperty(canvas, "Width"));
int canvasHeight = Convert.ToInt32(RequiredProperty(canvas, "Height"));

if (id != "reference-dark-fantasy-radial8-spike" || sourceProtocol != 2 ||
    layers != 1 || states != 9 || staticLayers != 0 ||
    canvasWidth != 1254 || canvasHeight != 1254 ||
    logicalWidth != 420 || logicalHeight != 420 || anchorX != 210 || anchorY != 210)
{
    throw new InvalidOperationException(
        $"Unexpected plan: id={id} protocol={sourceProtocol} layers={layers} states={states} " +
        $"static={staticLayers} canvas={canvasWidth}x{canvasHeight} " +
        $"logical={logicalWidth}x{logicalHeight} anchor={anchorX},{anchorY}");
}

Console.WriteLine("C# V2 LOADER VALID");
Console.WriteLine($"Theme: {id}");
Console.WriteLine($"Protocol/States/Layers/Static: {sourceProtocol}/{states}/{layers}/{staticLayers}");
Console.WriteLine($"Reference/Logical/Anchor: {canvasWidth}x{canvasHeight}/{logicalWidth}x{logicalHeight}/{anchorX},{anchorY}");

Type bundleType = assembly.GetType("PcDs4Server.RuntimeRenderBundle", throwOnError: true)!;
MethodInfo buildBundle = bundleType.GetMethod(
    "Build",
    BindingFlags.Static | BindingFlags.Public,
    binder: null,
    types: new[] { plan.GetType(), typeof(RadialMenuSettings), typeof(int), typeof(int) },
    modifiers: null) ?? throw new InvalidOperationException("RuntimeRenderBundle.Build was not found.");
object bundle = buildBundle.Invoke(null, new object[] { plan, RadialMenuSettings.Default, 280, 192 })
    ?? throw new InvalidOperationException("RuntimeRenderBundle.Build returned null.");
try
{
    Size physical = (Size)RequiredProperty(bundle, "PhysicalSurfaceSize");
    bool isFullState = (bool)RequiredProperty(bundle, "IsFullStateFrame");
    if (!isFullState || physical != new Size(840, 840))
        throw new InvalidOperationException($"Unexpected 200% bundle: fullState={isFullState}, physical={physical}");

    MethodInfo getFinalState = bundleType.GetMethod("GetFinalState", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("RuntimeRenderBundle.GetFinalState was not found.");
    string reviewPath = Path.Combine(Directory.GetParent(packagePath)!.FullName, "review");
    Directory.CreateDirectory(reviewPath);
    foreach (int slot in new[] { 0, 1, 3, 5, 8 })
    {
        var state = (Bitmap)(getFinalState.Invoke(bundle, new object[] { slot })
            ?? throw new InvalidOperationException($"State {slot} was null."));
        if (state.Size != physical || state.PixelFormat != PixelFormat.Format32bppPArgb ||
            state.GetPixel(0, 0).A != 0 || state.GetPixel(839, 839).A != 0)
            throw new InvalidOperationException(
                $"Invalid state {slot}: {state.Size}, {state.PixelFormat}, corners={state.GetPixel(0, 0).A}/{state.GetPixel(839, 839).A}");
        string name = slot == 0 ? "idle" : $"selected-{slot}";
        state.Save(Path.Combine(reviewPath, $"runtime-200pct-{name}.png"), ImageFormat.Png);
    }
    Console.WriteLine("C# RUNTIME BUNDLE VALID");
    Console.WriteLine("200% Physical: 840x840 PArgb; states checked: idle,1,3,5,8; transparent corners: yes");
}
finally
{
    if (bundle is IDisposable disposable) disposable.Dispose();
}
return 0;
