using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace PcDs4Server.Tests;

internal sealed class RadialVisualPackTestDirectory : IDisposable
{
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

    private static void WriteObject(string path, JsonObject value) =>
        File.WriteAllText(path, value.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        }));
}
