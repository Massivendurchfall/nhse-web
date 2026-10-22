using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NHSE.Core;

/// <summary>
/// Keeps a whole save folder in memory, keyed by relative path ("main.dat", "Villager0/personal.dat").
/// The browser fills it from the files the user picked and reads it back after saving.
/// </summary>
public sealed class MemorySaveFileProvider : ISaveFileProvider
{
    private readonly Dictionary<string, byte[]> files;
    private readonly string basePath;

    public MemorySaveFileProvider(IEnumerable<KeyValuePair<string, byte[]>> entries)
        : this(new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase), string.Empty)
    {
        foreach (var (path, data) in entries)
            files[Normalize(path)] = data;
    }

    private MemorySaveFileProvider(Dictionary<string, byte[]> files, string basePath)
    {
        this.files = files;
        this.basePath = basePath;
    }

    /// <summary>All files, including the ones rewritten by a save.</summary>
    public IReadOnlyDictionary<string, byte[]> Files => files;

    /// <summary>Paths written since the provider was created.</summary>
    public HashSet<string> Changed { get; } = new(StringComparer.OrdinalIgnoreCase);

    private static string Normalize(string path) => path.Replace('\\', '/').Trim('/');

    private string Key(string relativePath) =>
        Normalize(basePath.Length == 0 ? relativePath : $"{basePath}/{relativePath}");

    public byte[] ReadFile(string relativePath) =>
        files.TryGetValue(Key(relativePath), out var data)
            ? data
            : throw new FileNotFoundException($"File not found in save: {Key(relativePath)}");

    public void WriteFile(string relativePath, ReadOnlySpan<byte> data)
    {
        var key = Key(relativePath);
        files[key] = data.ToArray();
        Root.Changed.Add(key);
    }

    public bool FileExists(string relativePath) => files.ContainsKey(Key(relativePath));

    public string[] GetDirectories(string searchPattern)
    {
        var prefix = basePath.Length == 0 ? string.Empty : basePath + "/";
        var needle = searchPattern.Replace("*", string.Empty);
        return files.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(k => k[prefix.Length..])
            .Where(rest => rest.IndexOf('/') > 0)
            .Select(rest => rest[..rest.IndexOf('/')])
            .Where(dir => dir.Contains(needle, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private MemorySaveFileProvider? root;
    private MemorySaveFileProvider Root => root ?? this;

    public ISaveFileProvider GetSubdirectoryProvider(string subdirectory) =>
        new MemorySaveFileProvider(files, Key(subdirectory)) { root = Root };

    public void Flush()
    {
        // nothing to do: the caller takes Files and writes them where they came from
    }
}
