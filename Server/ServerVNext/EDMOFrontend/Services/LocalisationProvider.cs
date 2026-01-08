using System.Globalization;
using System.Text.Json;

namespace EDMOFrontend.Components;

using LocalisationBank = Dictionary<string, Dictionary<string, string>>;

public class LocalisationProvider
{
    private readonly Dictionary<string, LocalisationBank> banks = [];

    public readonly IReadOnlyDictionary<string, string> AvailableLocales;

    public LocalisationProvider()
    {
        string currentDirectory = AppContext.BaseDirectory;

        var localisationDirectory = new DirectoryInfo($"{currentDirectory}/Resources/strings");

        foreach (var file in localisationDirectory.EnumerateFiles())
        {
            var bank = JsonSerializer.Deserialize<LocalisationBank>(File.OpenRead(file.FullName));
            if (bank is null)
                continue;

            banks[Path.GetFileNameWithoutExtension(file.Name)] = bank;
        }

        AvailableLocales = banks.SelectMany(b => b.Value.Values.SelectMany(b2 => b2.Keys))
            .Distinct()
            .ToDictionary(c => CultureInfo.GetCultureInfo(c).NativeName);
    }

    public string? GetLocalisedString(string bankKey, string textKey, string locale, params ReadOnlySpan<object?> args)
    {
        if (!banks.TryGetValue(bankKey, out var bank)) return null;

        if (!bank.TryGetValue(textKey, out var localisationEntry)) return null;

        if (localisationEntry.Count == 0)
            return null;

        if (!localisationEntry.TryGetValue(locale, out string? format))
            format = localisationEntry.First().Value;

        return string.Format(format, args);
    }
}
