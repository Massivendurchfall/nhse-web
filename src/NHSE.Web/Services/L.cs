namespace NHSE.Web.Services;

/// <summary>UI language: German when the browser prefers German, English otherwise.</summary>
public static class L
{
    public static bool German { get; private set; }

    public static void Use(string browserLanguage) =>
        German = browserLanguage.StartsWith("de", StringComparison.OrdinalIgnoreCase);

    public static string T(string de, string en) => German ? de : en;
}
