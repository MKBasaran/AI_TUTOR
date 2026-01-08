using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace EDMOFrontend.Components;

public class LocalisationContext(LocalisationProvider localisationProvider, ProtectedSessionStorage sessionStorage)
{
    private string? locale = null;

    public IReadOnlyDictionary<string, string> AvailableLocales => localisationProvider.AvailableLocales;

    public async ValueTask<string?> GetLocalisedString(string bankKey, string textKey,
        params object?[] args)
    {
        string locale = await getLocaleCodeAsync();
        return localisationProvider.GetLocalisedString(bankKey, textKey, locale ?? "nl", args);
    }

    private async ValueTask<string> getLocaleCodeAsync()
    {
        if (locale is not null) return locale;

        var storageLocale = await sessionStorage.GetAsync<string>("locale");
        if (!storageLocale.Success)
            return "nl";

        locale = storageLocale.Value;
        return locale;
    }

    public async ValueTask<string> GetLocaleNameAsync()
    {
        string localeCode = await getLocaleCodeAsync();

        string? localeName = AvailableLocales.FirstOrDefault(l => string.Equals(l.Value, localeCode)).Key;

        return localeName ?? $"{localeCode}";
    }

    private async Task SetLocaleCodeAsync(string newLocale)
    {
        if (string.Equals(locale, newLocale))
            return;

        locale = newLocale;
        await sessionStorage.SetAsync("locale", newLocale);

        LocaleChanged?.Invoke();
    }

    public async Task SetLocaleAsync(string localeName)
    {
        string? localeCode = AvailableLocales.FirstOrDefault(l => string.Equals(l.Key, localeName)).Value;

        if (localeCode is null)
            return;

        await SetLocaleCodeAsync(localeCode);
    }

    public Action? LocaleChanged { get; set; }
}
