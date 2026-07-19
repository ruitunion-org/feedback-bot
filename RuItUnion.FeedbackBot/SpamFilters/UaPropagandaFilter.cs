using System.Text.RegularExpressions;
using Microsoft.FeatureManagement.Mvc;

namespace RuItUnion.FeedbackBot.SpamFilters;

[FeatureGate("UaPropagandaFilter")]
public partial class UaPropagandaFilter : ISpamFilter
{
    private const int MIN_COUNT = 2;

    private static readonly Regex[] _allRegexes =
    [
        ServicemanRegex,
        UnitNamesRegex,
        CasualtyNumbersRegex,
        InUkraineRegex,
        NamesEstablishedRegex,
        ContractorsMobilizedRegex,
        MaterialGainRegex,
        ExchangeRegex,
        BodyReturnRegex,
    ];

    /// <summary>
    ///     Слово "военнослужащие" во всех падежах
    /// </summary>
    [GeneratedRegex(@"\bвоеннослужащ\w*\b", RegexOptions.IgnoreCase, "ru-UA")]
    private static partial Regex ServicemanRegex { get; }

    /// <summary>
    ///     Точные наименования подразделений, номера в/ч, аббревиатуры
    /// </summary>
    [GeneratedRegex(
        @"(в/ч\s?\d{4,5})|(\d{1,3}-[йяе]\s.*?(полк|бригада|батальон|дивизия))|(Центр специальной подготовки|Кубинка|Сенеж)|\b(ССО|ГРУ|ФСБ|МО|МО РФ)\b",
        RegexOptions.IgnoreCase, "ru-UA")]
    private static partial Regex UnitNamesRegex { get; }

    /// <summary>
    ///     Числовые данные о потерях, погибших, ушедших в СОЧ
    /// </summary>
    [GeneratedRegex(@"(как минимум|минимум|подтверждена гибель|имена)\s*\d{2,}\s*(погибших|военнослужащих|ушли в СОЧ)",
        RegexOptions.IgnoreCase, "ru-UA")]
    private static partial Regex CasualtyNumbersRegex { get; }

    /// <summary>
    ///     Фраза "в Украине"
    /// </summary>
    [GeneratedRegex(@"\bв\s+украине\b", RegexOptions.IgnoreCase, "ru-UA")]
    private static partial Regex InUkraineRegex { get; }

    /// <summary>
    ///     "Установлены имена" / "стали известны имена" / "подтверждена гибель"
    /// </summary>
    [GeneratedRegex(@"(установлены имена|стали известны имена|подтверждена гибель)", RegexOptions.IgnoreCase, "ru-UA")]
    private static partial Regex NamesEstablishedRegex { get; }

    /// <summary>
    ///     "Контрактники" и "мобилизованные"
    /// </summary>
    [GeneratedRegex(@"\bконтрактник\w*\b|\bмобилизованн\w*\b", RegexOptions.IgnoreCase, "ru-UA")]
    private static partial Regex ContractorsMobilizedRegex { get; }

    /// <summary>
    ///     Акцент на материальной выгоде / ресурсных затратах
    /// </summary>
    [GeneratedRegex(@"(материальное вознаграждение|значительных (временных и ресурсных|ресурсных) затрат)",
        RegexOptions.IgnoreCase, "ru-UA")]
    private static partial Regex MaterialGainRegex { get; }

    /// <summary>
    ///     Любые формы обмена (телами, пленными), обменный фонд
    /// </summary>
    [GeneratedRegex(@"\bобмен\w*\s+(телами|военнопленными|пленными)\b|\bобменный фонд\b", RegexOptions.IgnoreCase,
        "ru-UA")]
    private static partial Regex ExchangeRegex { get; }

    /// <summary>
    ///     Возвращение тел / останков
    /// </summary>
    [GeneratedRegex(@"\b(тел\w*|останк\w*)\s*(погибш\w*|военнослужащ\w*|парн\w*)\b|\bвернули\s+тел\w*\b",
        RegexOptions.IgnoreCase, "ru-UA")]
    private static partial Regex BodyReturnRegex { get; }

    public bool IsSpam(string? text) => !string.IsNullOrEmpty(text) && _allRegexes.Sum(x => x.Count(text)) >= MIN_COUNT;
}