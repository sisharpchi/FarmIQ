using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FarmIQ.Application.Abstractions;
using FarmIQ.Infrastructure.Configuration;
using FarmIQ.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FarmIQ.Infrastructure.Services;

public sealed class FarmLanguageService(
    GlmChatClient glmChatClient,
    IHttpClientFactory httpClientFactory,
    IOptions<OpenAIOptions> options,
    ILogger<FarmLanguageService> logger) : ILanguageService
{
    private static readonly string[] UzbekSignals =
    [
        "salom",
        "assalomu",
        "barg",
        "dog",
        "dog'",
        "shira",
        "novda",
        "hosil",
        "yomgir",
        "yomg'ir",
        "ekin",
        "zararkunanda",
        "kasallik",
        "tarqal",
        "sarg'ay"
    ];

    private static readonly string[] RussianSignals =
    [
        "привет",
        "здравств",
        "лист",
        "пятн",
        "тля",
        "ветк",
        "урож",
        "дожд",
        "болезн",
        "вредител",
        "распростран"
    ];

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ToEnglishPhraseMaps =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            [FarmLanguages.Russian] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["листья"] = "leaves",
                ["лист"] = "leaf",
                ["пятна"] = "spots",
                ["пятно"] = "spot",
                ["пятнистые"] = "spotted",
                ["тля"] = "aphids",
                ["ветки"] = "branches",
                ["ветка"] = "branch",
                ["ломаются"] = "breaking",
                ["ломается"] = "breaking",
                ["распространяется"] = "spreading",
                ["распространяются"] = "spreading",
                ["желтые"] = "yellow",
                ["желтеют"] = "yellowing",
                ["гниль"] = "rot",
                ["мучнистая роса"] = "mildew",
                ["помидор"] = "tomato",
                ["томата"] = "tomato",
                ["томаты"] = "tomatoes",
                ["огурец"] = "cucumber",
                ["кукуруза"] = "maize",
                ["болезнь"] = "disease",
                ["вредители"] = "pests",
                ["вредитель"] = "pest",
                ["стебель"] = "stem",
                ["сухие"] = "dry",
                ["увядают"] = "wilting",
                ["черные"] = "black",
                ["коричневые"] = "brown"
            },
            [FarmLanguages.Uzbek] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["barglarida"] = "leaf",
                ["bargida"] = "leaf",
                ["barglar"] = "leaves",
                ["barg"] = "leaf",
                ["dog'lar"] = "spots",
                ["doglar"] = "spots",
                ["dog'"] = "spot",
                ["dog"] = "spot",
                ["shira"] = "aphids",
                ["novdalar"] = "branches",
                ["novda"] = "branch",
                ["sinmoqda"] = "breaking",
                ["sinyapti"] = "breaking",
                ["tarqalmoqda"] = "spreading",
                ["tarqalyapti"] = "spreading",
                ["sariq"] = "yellow",
                ["sarg'aymoqda"] = "yellowing",
                ["sargaymoqda"] = "yellowing",
                ["chirish"] = "rot",
                ["chiriyotgan"] = "rot",
                ["pomidor"] = "tomato",
                ["bodring"] = "cucumber",
                ["makkajo'xori"] = "maize",
                ["makkajoxori"] = "maize",
                ["kasallik"] = "disease",
                ["zararkunanda"] = "pest",
                ["poya"] = "stem",
                ["quruq"] = "dry",
                ["qora"] = "black",
                ["jigarrang"] = "brown"
            }
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> FromEnglishPhraseMaps =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            [FarmLanguages.Russian] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Possible issue:"] = "Возможная проблема:",
                ["Confidence:"] = "Уверенность:",
                ["What to do now:"] = "Что делать сейчас:",
                ["Harvest note:"] = "Примечание по сбору урожая:",
                ["Weather:"] = "Погода:",
                ["Crop impact:"] = "Влияние на культуру:",
                ["Next thing to send:"] = "Что отправить дальше:",
                ["Why this looks likely:"] = "Почему это похоже на проблему:",
                ["Safety:"] = "Безопасность:",
                ["I need one more detail before I can be fully confident."] = "Мне нужна еще одна деталь, чтобы быть уверенным в диагнозе.",
                ["a closer photo of the affected leaves and stem base"] = "более крупное фото пораженных листьев и основания стебля",
                ["your location for rain and spray timing"] = "вашу геолокацию для расчета дождя и времени опрыскивания",
                ["Welcome to FarmIQ. Send a photo of the affected crop and one short sentence about the problem.\nExample: \"My tomato leaves have brown spots and aphids are spreading.\"\nIf you want rain or spray timing, you can also share your location."] =
                    "Добро пожаловать в FarmIQ. Отправьте фото пораженной культуры и одно короткое предложение с описанием проблемы.\nПример: \"На листьях томата коричневые пятна, и распространяется тля.\"\nЕсли нужен совет по дождю или времени опрыскивания, можете также отправить геолокацию.",
                ["Send three things when you can: the crop name, what you see, and a clear photo.\nExample: \"Tomato leaves have yellow spots for 5 days.\"\nShare your location only if you want weather-based advice."] =
                    "По возможности отправьте три вещи: название культуры, что вы видите, и четкое фото.\nПример: \"На листьях томата уже 5 дней желтые пятна.\"\nГеолокацию отправляйте только если хотите получить совет с учетом погоды.",
                ["Tell me the crop and the problem in one sentence, and send a clear photo if possible.\nExample: \"My tomato leaves have spots and the lower branches are weakening.\""] =
                    "Напишите название культуры и проблему одним предложением и, если возможно, приложите четкое фото.\nПример: \"На листьях томата появились пятна, и нижние ветви слабеют.\"",
                ["I need a little more detail before I can help.\nSend the crop name, the main symptom, and a clear photo if you have one."] =
                    "Мне нужно чуть больше деталей, чтобы помочь.\nОтправьте название культуры, основной симптом и четкое фото, если оно у вас есть.",
                ["I received your media.\nNow send one short sentence with the crop name and the main symptom.\nExample: \"Tomato leaves have brown spots\" or \"Aphids are spreading on my peppers.\""] =
                    "Я получил ваш медиафайл.\nТеперь отправьте одно короткое предложение с названием культуры и главным симптомом.\nПример: \"На листьях томата коричневые пятна\" или \"На перце распространяется тля.\"",
                ["Thanks, I saved your location. Your next advisory can include rain and spray timing.\nNow send a photo or describe the crop problem."] =
                    "Спасибо, я сохранил вашу геолокацию. Следующий совет сможет учитывать дождь и время опрыскивания.\nТеперь отправьте фото или опишите проблему культуры.",
                ["Thanks, I saved your location for weather-based advice.\nNow send a crop photo or describe the issue you want checked."] =
                    "Спасибо, я сохранил вашу геолокацию для советов с учетом погоды.\nТеперь отправьте фото культуры или опишите проблему, которую нужно проверить.",
                ["Possible aphid infestation"] = "Возможное заражение тлей",
                ["Possible fungal leaf spot"] = "Возможная грибковая пятнистость листьев",
                ["Nutrient stress or mixed pest pressure"] = "Питательный стресс или смешанное давление вредителей",
                ["Inspect leaf undersides, wash off clustered aphids where possible, and use locally approved targeted control if colonies keep spreading."] =
                    "Проверьте нижнюю сторону листьев, по возможности смойте скопления тли и используйте локально разрешенное целевое средство, если колонии продолжают распространяться.",
                ["Remove heavily infected leaves, improve spacing, and apply a locally approved fungicide if symptoms spread."] =
                    "Удалите сильно пораженные листья, улучшите расстояние между растениями и примените локально разрешенный фунгицид, если симптомы распространяются.",
                ["Inspect the underside of leaves, confirm pest presence, and apply balanced nutrients with targeted pest control only if confirmed."] =
                    "Проверьте нижнюю сторону листьев, подтвердите наличие вредителей и используйте сбалансированное питание с целевой защитой только после подтверждения.",
                ["Avoid harvesting during the next 5-7 days if treatment is applied; recheck plant vigor before harvesting."] =
                    "Если применяется обработка, избегайте сбора урожая в ближайшие 5-7 дней; перед сбором повторно оцените состояние растений.",
                ["Please verify with a local agronomist or extension worker before using expensive inputs."] =
                    "Перед использованием дорогих препаратов уточните рекомендации у местного агронома или консультанта.",
                ["Follow label instructions and protective equipment guidance for any treatment."] =
                    "При любой обработке следуйте инструкции на этикетке и используйте средства защиты.",
                ["The report mentions aphids spreading and leaf damage, which often points to sap-sucking pest pressure."] =
                    "В сообщении упоминается распространение тли и повреждение листьев, что часто указывает на давление сосущих вредителей.",
                ["Spotting or staining on leaves often aligns with early fungal disease patterns."] =
                    "Пятна или окрашивание на листьях часто соответствуют ранним признакам грибковых болезней.",
                ["The description is broad, so the issue may involve several stress factors."] =
                    "Описание слишком общее, поэтому проблема может включать несколько факторов стресса.",
                ["Current weather is"] = "Текущая погода:",
                ["Rain chance in the next"] = "Вероятность дождя в ближайшие",
                ["hours is"] = "часов составляет",
                ["Rain risk in the next"] = "Риск дождя в ближайшие",
                ["looks low."] = "низкий.",
                ["Heat stress risk is elevated. Irrigate early morning if water is available."] =
                    "Риск теплового стресса повышен. Если есть вода, поливайте рано утром.",
                ["Postpone spraying if possible and monitor disease pressure after the wet period."] =
                    "По возможности отложите опрыскивание и следите за давлением болезней после влажного периода.",
                ["No acute weather stress signal. Keep watching field moisture and pest spread."] =
                    "Острого погодного стресса не видно. Продолжайте следить за влажностью поля и распространением вредителей."
            },
            [FarmLanguages.Uzbek] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Possible issue:"] = "Ehtimoliy muammo:",
                ["Confidence:"] = "Ishonchlilik:",
                ["What to do now:"] = "Hozir nima qilish kerak:",
                ["Harvest note:"] = "Hosil bo'yicha eslatma:",
                ["Weather:"] = "Ob-havo:",
                ["Crop impact:"] = "Ekinga ta'siri:",
                ["Next thing to send:"] = "Keyin yuboriladigan narsa:",
                ["Why this looks likely:"] = "Nega bu ehtimoliy ko'rinmoqda:",
                ["Safety:"] = "Xavfsizlik:",
                ["I need one more detail before I can be fully confident."] = "To'liq ishonch hosil qilishim uchun menga yana bitta tafsilot kerak.",
                ["a closer photo of the affected leaves and stem base"] = "zararlangan barglar va poya tagining yaqinroq surati",
                ["your location for rain and spray timing"] = "yomg'ir va purkash vaqtini hisoblash uchun joylashuvingiz",
                ["Welcome to FarmIQ. Send a photo of the affected crop and one short sentence about the problem.\nExample: \"My tomato leaves have brown spots and aphids are spreading.\"\nIf you want rain or spray timing, you can also share your location."] =
                    "FarmIQ'ga xush kelibsiz. Zararlangan ekin suratini va muammo haqida bitta qisqa jumla yuboring.\nMisol: \"Pomidor barglarida jigarrang dog'lar bor va shira tarqalmoqda.\"\nAgar yomg'ir yoki purkash vaqti bo'yicha tavsiya kerak bo'lsa, joylashuvingizni ham yuborishingiz mumkin.",
                ["Send three things when you can: the crop name, what you see, and a clear photo.\nExample: \"Tomato leaves have yellow spots for 5 days.\"\nShare your location only if you want weather-based advice."] =
                    "Imkon bo'lsa uchta narsani yuboring: ekin nomi, ko'rayotgan belgilar va aniq surat.\nMisol: \"Pomidor barglarida 5 kundan beri sariq dog'lar bor.\"\nJoylashuvni faqat ob-havoga asoslangan tavsiya kerak bo'lsa yuboring.",
                ["Tell me the crop and the problem in one sentence, and send a clear photo if possible.\nExample: \"My tomato leaves have spots and the lower branches are weakening.\""] =
                    "Ekin nomi va muammoni bitta gapda yozing, iloji bo'lsa aniq surat ham yuboring.\nMisol: \"Pomidor barglarida dog'lar bor, pastki novdalar zaiflashmoqda.\"",
                ["I need a little more detail before I can help.\nSend the crop name, the main symptom, and a clear photo if you have one."] =
                    "Yordam berishim uchun biroz ko'proq ma'lumot kerak.\nEkin nomini, asosiy simptomni va imkon bo'lsa aniq suratni yuboring.",
                ["I received your media.\nNow send one short sentence with the crop name and the main symptom.\nExample: \"Tomato leaves have brown spots\" or \"Aphids are spreading on my peppers.\""] =
                    "Media faylingizni oldim.\nEndi ekin nomi va asosiy simptom bilan bitta qisqa jumla yuboring.\nMisol: \"Pomidor barglarida jigarrang dog'lar bor\" yoki \"Qalampirda shira tarqalmoqda.\"",
                ["Thanks, I saved your location. Your next advisory can include rain and spray timing.\nNow send a photo or describe the crop problem."] =
                    "Rahmat, joylashuvingizni saqladim. Keyingi tavsiyada yomg'ir va purkash vaqti ham hisobga olinadi.\nEndi surat yuboring yoki ekin muammosini yozib bering.",
                ["Thanks, I saved your location for weather-based advice.\nNow send a crop photo or describe the issue you want checked."] =
                    "Rahmat, ob-havo asosidagi tavsiya uchun joylashuvingiz saqlandi.\nEndi ekin suratini yuboring yoki tekshirilishi kerak bo'lgan muammoni yozing.",
                ["Possible aphid infestation"] = "Ehtimoliy shira zararlanishi",
                ["Possible fungal leaf spot"] = "Ehtimoliy zamburug'li barg dog'i",
                ["Nutrient stress or mixed pest pressure"] = "Oziqa stressi yoki aralash zararkunanda bosimi",
                ["Inspect leaf undersides, wash off clustered aphids where possible, and use locally approved targeted control if colonies keep spreading."] =
                    "Barglarning pastki tomonini tekshiring, imkon bo'lsa to'plangan shirani yuvib tashlang va koloniya tarqalishda davom etsa, mahalliy ruxsat etilgan maqsadli vositadan foydalaning.",
                ["Remove heavily infected leaves, improve spacing, and apply a locally approved fungicide if symptoms spread."] =
                    "Kuchli zararlangan barglarni olib tashlang, oralig'ini yaxshilang va simptomlar tarqalsa, mahalliy ruxsat etilgan fungitsidni qo'llang.",
                ["Inspect the underside of leaves, confirm pest presence, and apply balanced nutrients with targeted pest control only if confirmed."] =
                    "Barglarning pastki tomonini tekshiring, zararkunanda borligini tasdiqlang va faqat tasdiqlansa, muvozanatli oziqlantirish hamda maqsadli kurash choralarini qo'llang.",
                ["Avoid harvesting during the next 5-7 days if treatment is applied; recheck plant vigor before harvesting."] =
                    "Agar ishlov berilsa, keyingi 5-7 kun ichida hosil yig'mang; yig'ishdan oldin o'simlik holatini qayta tekshiring.",
                ["Please verify with a local agronomist or extension worker before using expensive inputs."] =
                    "Qimmat vositalarni ishlatishdan oldin mahalliy agronom yoki maslahatchi bilan tekshirib oling.",
                ["Follow label instructions and protective equipment guidance for any treatment."] =
                    "Har qanday ishlovda yorliq ko'rsatmalariga va himoya vositalari qoidalariga amal qiling.",
                ["The report mentions aphids spreading and leaf damage, which often points to sap-sucking pest pressure."] =
                    "Xabarda shira tarqalishi va barg zarari aytilgan, bu ko'pincha so'ruvchi zararkunandalar bosimini bildiradi.",
                ["Spotting or staining on leaves often aligns with early fungal disease patterns."] =
                    "Bargdagi dog' yoki rang o'zgarishi ko'pincha zamburug' kasalligining erta belgilari bilan mos keladi.",
                ["The description is broad, so the issue may involve several stress factors."] =
                    "Tavsif umumiy bo'lgani uchun muammo bir nechta stress omillariga bog'liq bo'lishi mumkin.",
                ["Current weather is"] = "Hozirgi ob-havo:",
                ["Rain chance in the next"] = "Keyingi",
                ["hours is"] = "soatda yomg'ir ehtimoli",
                ["Rain risk in the next"] = "Keyingi",
                ["looks low."] = "past ko'rinadi.",
                ["Heat stress risk is elevated. Irrigate early morning if water is available."] =
                    "Issiqlik stressi xavfi yuqori. Suv bo'lsa, ertalab erta sug'oring.",
                ["Postpone spraying if possible and monitor disease pressure after the wet period."] =
                    "Iloji bo'lsa purkashni kechiktiring va nam davrdan keyin kasallik bosimini kuzating.",
                ["No acute weather stress signal. Keep watching field moisture and pest spread."] =
                    "Keskin ob-havo stressi ko'rinmayapti. Dala namligi va zararkunanda tarqalishini kuzatishda davom eting."
            }
        };

    public Task<string> DetectLanguageAsync(string text, CancellationToken cancellationToken = default)
    {
        var normalized = text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return Task.FromResult(FarmLanguages.English);
        }

        var lower = normalized.ToLowerInvariant();
        if (UzbekSignals.Any(lower.Contains))
        {
            return Task.FromResult(FarmLanguages.Uzbek);
        }

        if (RussianSignals.Any(lower.Contains))
        {
            return Task.FromResult(FarmLanguages.Russian);
        }

        if (normalized.Any(ch => ch is >= '\u0400' and <= '\u04FF'))
        {
            return Task.FromResult(FarmLanguages.Russian);
        }

        return Task.FromResult(FarmLanguages.English);
    }

    public async Task<string> TranslateToEnglishAsync(string text, string sourceLanguage, CancellationToken cancellationToken = default)
    {
        var source = FarmLanguages.Normalize(sourceLanguage);
        if (source == FarmLanguages.English || string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var glm = await TryTranslateWithGlmAsync(text, source, FarmLanguages.English, cancellationToken);
        if (!string.IsNullOrWhiteSpace(glm))
        {
            return glm;
        }

        var openAi = await TryTranslateWithOpenAiAsync(text, source, FarmLanguages.English, cancellationToken);
        if (!string.IsNullOrWhiteSpace(openAi))
        {
            return openAi;
        }

        return ApplyPhraseMap(text, ToEnglishPhraseMaps.TryGetValue(source, out var replacements) ? replacements : null);
    }

    public async Task<string> TranslateFromEnglishAsync(string text, string targetLanguage, CancellationToken cancellationToken = default)
    {
        var target = FarmLanguages.Normalize(targetLanguage);
        if (target == FarmLanguages.English || string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var glm = await TryTranslateWithGlmAsync(text, FarmLanguages.English, target, cancellationToken);
        if (!string.IsNullOrWhiteSpace(glm))
        {
            return glm;
        }

        var openAi = await TryTranslateWithOpenAiAsync(text, FarmLanguages.English, target, cancellationToken);
        if (!string.IsNullOrWhiteSpace(openAi))
        {
            return openAi;
        }

        return ApplyPhraseMap(text, FromEnglishPhraseMaps.TryGetValue(target, out var replacements) ? replacements : null);
    }

    private async Task<string?> TryTranslateWithGlmAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken cancellationToken)
    {
        if (!glmChatClient.IsConfigured)
        {
            return null;
        }

        var content = await glmChatClient.CompleteAsync(
            [
                new GlmChatMessage(
                    "system",
                    $"You translate FarmIQ agricultural messages from {sourceLanguage} to {targetLanguage}. Return plain text only. Preserve line breaks, field labels, and practical agronomy meaning."),
                new GlmChatMessage("user", text)
            ],
            temperature: 0,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(content))
        {
            return content;
        }

        logger.LogWarning("GLM language translation was unavailable. Falling back to the next translation provider.");
        return null;
    }

    private async Task<string?> TryTranslateWithOpenAiAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled || string.IsNullOrWhiteSpace(options.Value.ApiKey))
        {
            return null;
        }

        try
        {
            var responseBody = new
            {
                model = options.Value.LanguageModel,
                temperature = 0,
                messages = new object[]
                {
                    new
                    {
                        role = "system",
                        content = $"You translate FarmIQ agricultural messages from {sourceLanguage} to {targetLanguage}. Return plain text only. Preserve line breaks, field labels, and practical agronomy meaning."
                    },
                    new
                    {
                        role = "user",
                        content = text
                    }
                }
            };

            var client = httpClientFactory.CreateClient(nameof(FarmLanguageService));
            client.Timeout = TimeSpan.FromSeconds(options.Value.TimeoutSeconds);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.ApiKey);

            using var response = await client.PostAsJsonAsync($"{options.Value.BaseUrl.TrimEnd('/')}/chat/completions", responseBody, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("OpenAI language translation returned status {StatusCode}. Falling back to rule-based translation.", response.StatusCode);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(cancellationToken: cancellationToken);
            return payload?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "OpenAI language translation failed. Falling back to rule-based translation.");
            return null;
        }
    }

    private static string ApplyPhraseMap(string text, IReadOnlyDictionary<string, string>? replacements)
    {
        var normalized = NormalizeMultiline(text);
        if (replacements is null || replacements.Count == 0)
        {
            return normalized;
        }

        if (replacements.TryGetValue(normalized, out var exact))
        {
            return exact;
        }

        var result = normalized;
        foreach (var replacement in replacements.OrderByDescending(x => x.Key.Length))
        {
            result = Regex.Replace(
                result,
                Regex.Escape(replacement.Key),
                replacement.Value,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return result;
    }

    private static string NormalizeMultiline(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

    private sealed class ChatCompletionResponse
    {
        public List<Choice>? Choices { get; set; }
    }

    private sealed class Choice
    {
        public ChatMessage? Message { get; set; }
    }

    private sealed class ChatMessage
    {
        public string? Content { get; set; }
    }
}
