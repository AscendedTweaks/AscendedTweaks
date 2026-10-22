using AscendedTweaks.Core;

namespace AscendedTweaks.Catalog;

/// <summary>Vorgefertigte Profile (Abschnitt 6.10), eingebettet unter profiles/*.json. Sie durchlaufen dieselbe Pruefung wie ein Import.</summary>
public static class ProfileLoader
{
    /// <summary>Reihenfolge in der Oberflaeche.</summary>
    public static readonly IReadOnlyList<string> BuiltInKeys = ["safe", "gaming", "competitive", "laptop", "privacy", "cleanup", "office"];

    public static IReadOnlyList<TweakProfile> LoadBuiltIn(ITweakCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var assembly = typeof(ProfileLoader).Assembly;
        var result = new List<TweakProfile>();
        foreach (var key in BuiltInKeys)
        {
            using var stream = assembly.GetManifestResourceStream($"catalog/profiles/{key}.json")
                ?? throw new AscendedTweaksException(ErrorKind.CatalogInvalid, $"Built-in profile {key} is missing.");
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var profile = ProfileCodec.Parse(buffer.ToArray(), catalog, builtIn: true);
            if (profile.Key != key)
            {
                throw new AscendedTweaksException(ErrorKind.CatalogInvalid, $"Profile {key} has the key {profile.Key}.");
            }

            result.Add(profile);
        }

        return result;
    }
}
