using System.Globalization;
using System.Windows;

namespace OpenCodeWidget.Services;

public static class Loc
{
    public const string Russian = "ru";
    public const string English = "en";

    private static string _language = Russian;

    public static string Language => _language;

    public static CultureInfo Culture => _language == English
        ? CultureInfo.GetCultureInfo("en-US")
        : CultureInfo.GetCultureInfo("ru-RU");

    public static event Action? Changed;

    public static void Apply(string? language, bool notify = true)
    {
        _language = string.Equals(language, English, StringComparison.OrdinalIgnoreCase)
            ? English
            : Russian;

        if (Application.Current is { } app)
        {
            var dictionaries = app.Resources.MergedDictionaries;
            var old = dictionaries.FirstOrDefault(d =>
                d.Source?.OriginalString.Contains("Strings.", StringComparison.OrdinalIgnoreCase) == true);

            if (old is not null)
                dictionaries.Remove(old);

            dictionaries.Insert(0, new ResourceDictionary
            {
                Source = new Uri($"Resources/Strings.{_language}.xaml", UriKind.Relative),
            });
        }

        if (notify)
            Changed?.Invoke();
    }

    public static string Get(string key)
    {
        return Application.Current?.TryFindResource(key) as string ?? key;
    }

    public static string Format(string key, params object?[] args)
    {
        return string.Format(Culture, Get(key), args);
    }
}
