using System.Reflection;

namespace FiveHead.State.Api;

public static class AdminAssets
{
    private static readonly Assembly Assembly = typeof(AdminAssets).Assembly;

    public static string Html { get; } = Read("admin.html");
    public static string Css { get; } = Read("admin.css");
    public static string Script { get; } = Read("admin.js");

    private static string Read(string fileName)
    {
        var name = Assembly.GetManifestResourceNames()
            .SingleOrDefault(candidate => candidate.EndsWith("." + fileName, StringComparison.Ordinal));
        if (name is null)
        {
            var found = string.Join(", ", Assembly.GetManifestResourceNames());
            throw new InvalidOperationException($"Admin asset {fileName} was not embedded. Resources: {found}");
        }

        using var stream = Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Admin asset {name} could not be opened.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
