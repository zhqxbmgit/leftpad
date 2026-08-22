using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace PcDs4Server.Tests;

internal sealed class RadialVisualPackTestDirectory : IDisposable
{
    private static readonly double[] Radial8Angles =
        { 0d, 45d, 90d, 135d, 180d, 225d, 270d, 315d };

    public RadialVisualPackTestDirectory()
    {
        Root = Path.Combine(
            Path.GetTempPath(),
            "LeftPad.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string AddPack(
        string directoryName,
        string id,
        string name,
        Action<JsonObject>? editManifest = null,
        Action<JsonObject>? editLayout = null)
    {
        string destination = Path.Combine(Root, directoryName);
        Directory.CreateDirectory(destination);
        foreach (string source in Directory.GetFiles(RadialVisualPackDefinition.DefaultDirectory))
            File.Copy(source, Path.Combine(destination, Path.GetFileName(source)));

        string manifestPath = Path.Combine(destination, "manifest.json");
        JsonObject manifest = ParseObject(manifestPath);
        manifest["id"] = id;
        manifest["name"] = name;
        editManifest?.Invoke(manifest);
        WriteObject(manifestPath, manifest);

        string layoutFile = manifest["layout"]!.GetValue<string>();
        string layoutPath = Path.Combine(destination, layoutFile);
        if (editLayout != null)
        {
            JsonObject layout = ParseObject(layoutPath);
            editLayout(layout);
            WriteObject(layoutPath, layout);
        }

        return destination;
    }

    public string AddRadial8Pack(
        Action<JsonObject>? editManifest = null,
        Action<JsonObject>? editLayout = null) =>
        AddPack(
            "radial-8",
            "radial-8-test",
            "Radial 8 Test",
            manifest =>
            {
                manifest["layoutProfile"] = "radial-8";
                manifest["slotCount"] = 8;
                editManifest?.Invoke(manifest);
            },
            layout =>
            {
                layout["slotCount"] = 8;
                layout["slotAnglesDegrees"] = new JsonArray(
                    Radial8Angles
                        .Select(angle => (JsonNode?)JsonValue.Create(angle))
                        .ToArray());

                var slots = new JsonArray();
                for (int index = 0; index < Radial8Angles.Length; index++)
                {
                    double angle = Radial8Angles[index];
                    double radians = angle * (Math.PI / 180d);
                    slots.Add(new JsonObject
                    {
                        ["slot"] = index + 1,
                        ["name"] = $"Slot {index + 1}",
                        ["angleDegreesClockwiseFromTop"] = angle,
                        ["glyphAnchor"] = PointAt(radians, radius: 350d),
                        ["labelAnchor"] = PointAt(radians, radius: 397d)
                    });
                }

                layout["slots"] = slots;
                editLayout?.Invoke(layout);
            });

    public string AddMalformedManifest(string directoryName)
    {
        string destination = Path.Combine(Root, directoryName);
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(destination, "manifest.json"), "{not-json");
        return destination;
    }

    public void Dispose()
    {
        if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
    }

    private static JsonObject ParseObject(string path) =>
        JsonNode.Parse(File.ReadAllText(path))!.AsObject();

    private static JsonObject PointAt(double radians, double radius) => new()
    {
        ["x"] = 627d + (Math.Sin(radians) * radius),
        ["y"] = 627d - (Math.Cos(radians) * radius)
    };

    private static void WriteObject(string path, JsonObject value) =>
        File.WriteAllText(path, value.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        }));
}
