using System.Text.Json;

namespace GgpkColorizer;

internal sealed class AppStorage(string directory)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public T? Read<T>(string name)
    {
        string path = Path.Combine(directory, name);
        if (!File.Exists(path)) return default;
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<T>(stream, JsonOptions)
                ?? throw new InvalidDataException($"Empty data file: {path}");
        }
        catch (JsonException exception)
        {
            string recovery = File.Exists(path + ".bak")
                ? " A previous copy is available at " + path + ".bak. Preserve both files before recovery."
                : " Preserve the file before recovery.";
            throw new InvalidDataException("Invalid local data: " + path + recovery, exception);
        }
    }

    public void Save<T>(string name, T value)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew,
                FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, value, JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            if (name == "changes.json" && File.Exists(path))
                File.Replace(temporary, path, path + ".bak");
            else
                File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
