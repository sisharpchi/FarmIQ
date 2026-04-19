using System.Globalization;
using FarmIQ.Shared;
using Microsoft.JSInterop;

namespace FarmIQ.Admin.Services;

public sealed class BrowserPreferenceStore(IJSRuntime jsRuntime)
{
    public ValueTask SaveStringAsync(string key, string value) =>
        jsRuntime.InvokeVoidAsync("farmiqAuth.saveValue", key, value);

    public ValueTask<string?> LoadStringAsync(string key) =>
        jsRuntime.InvokeAsync<string?>("farmiqAuth.loadValue", key);

    public ValueTask RemoveAsync(string key) =>
        jsRuntime.InvokeVoidAsync("farmiqAuth.removeValue", key);

    public ValueTask SetDocumentLanguageAsync(string languageCode) =>
        jsRuntime.InvokeVoidAsync("farmiqAuth.setDocumentLanguage", languageCode);
}

public sealed class AdminLocalizer(BrowserPreferenceStore preferenceStore)
{
    private const string LanguageKey = "farmiq.admin.language";

    private static readonly IReadOnlyDictionary<string, string> RussianTranslations =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Language"] = "Язык",
            ["Loading..."] = "Загрузка...",
            ["Nothing to show."] = "Нет данных.",
            ["Yes"] = "Да",
            ["No"] = "Нет",
            ["Saved"] = "Сохранено",
            ["Missing"] = "Отсутствует",
            ["Not available"] = "Недоступно",
            ["None"] = "Нет",
            ["Refresh"] = "Обновить",
            ["Apply"] = "Применить",
            ["View"] = "Открыть",
            ["Manage"] = "Управлять",
            ["Retry"] = "Повторить",
            ["Create"] = "Создать",
            ["Enable"] = "Включить",
            ["Disable"] = "Отключить",
            ["Save"] = "Сохранить",
            ["Previous"] = "Назад",
            ["Next"] = "Вперёд",
            ["All"] = "Все",
            ["Dashboard"] = "Панель",
            ["Conversations"] = "Диалоги",
            ["Jobs"] = "Задачи",
            ["Deliveries"] = "Доставки",
            ["Advisories"] = "Консультации",
            ["System"] = "Система",
            ["Users"] = "Пользователи",
            ["Sign out"] = "Выйти",
            ["Login"] = "Вход",
            ["FarmIQ Admin"] = "FarmIQ Admin",
            ["Crop advisory control center"] = "Центр управления агроконсультациями",
            ["Operations dashboard"] = "Операционная панель",
            ["Rural advisory monitoring, diagnostics, and response"] = "Мониторинг, диагностика и реакция по сельским консультациям",
            ["Restoring your operations session"] = "Восстановление вашей операционной сессии",
            ["Loading the control center and validating the local admin session."] = "Загружаем центр управления и проверяем локальную админ-сессию.",
            ["Access denied"] = "Доступ запрещён",
            ["Your current FarmIQ role does not allow access to this area."] = "Текущая роль FarmIQ не позволяет открыть этот раздел.",
            ["Nothing matched that route."] = "Маршрут не найден.",
            ["Sign in to the field operations console"] = "Войдите в консоль полевых операций",
            ["Monitor farmer conversations, troubleshoot delivery issues, and recover advisory jobs from one place."] = "Отслеживайте диалоги с фермерами, разбирайте проблемы доставки и восстанавливайте advisory-задачи из одного места.",
            ["Sign in"] = "Войти",
            ["Signing in..."] = "Вход...",
            ["Need an account?"] = "Нет аккаунта?",
            ["Create one here"] = "Создать здесь",
            ["Invite-only access is enabled. Ask an existing admin to provision your account."] = "Доступ только по приглашению. Попросите существующего администратора создать вам аккаунт.",
            ["Create your operations account"] = "Создайте операционный аккаунт",
            ["Invite-only access is active"] = "Доступ только по приглашению",
            ["Set up a FarmIQ admin account for analytics review, advisory quality checks, and daily operations."] = "Создайте аккаунт FarmIQ Admin для аналитики, проверки качества advisory и ежедневных операций.",
            ["Public signup is disabled in this environment. Ask an existing admin to create your account from the Users area."] = "Публичная регистрация в этой среде отключена. Попросите администратора создать аккаунт через раздел Пользователи.",
            ["Display name"] = "Отображаемое имя",
            ["Email"] = "Email",
            ["Password"] = "Пароль",
            ["Confirm password"] = "Подтвердите пароль",
            ["Create account"] = "Создать аккаунт",
            ["Creating account..."] = "Создание аккаунта...",
            ["Already have an account?"] = "Уже есть аккаунт?",
            ["This deployment accepts new users through the internal Admin Users screen only."] = "В этом развёртывании новые пользователи добавляются только через внутренний экран Пользователи.",
            ["System readiness"] = "Готовность системы",
            ["Current focus"] = "Текущий фокус",
            ["B2B insight snapshot"] = "B2B-срез",
            ["Loading dashboard..."] = "Загрузка панели...",
            ["Farmers"] = "Фермеры",
            ["Failed jobs"] = "Ошибки задач",
            ["Duplicate deliveries"] = "Дубликаты доставок",
            ["Completed advisories"] = "Завершённые advisory",
            ["Stuck jobs"] = "Зависшие задачи",
            ["Command messages"] = "Командные сообщения",
            ["Greetings / vague"] = "Приветствия / неопределённые",
            ["Follow-up advisories"] = "Advisory с уточнением",
            ["Service health"] = "Здоровье сервисов",
            ["Channel readiness"] = "Готовность каналов",
            ["Operational guidance"] = "Операционные рекомендации",
            ["Conversation detail"] = "Детали диалога",
            ["Select a conversation to inspect the message timeline."] = "Выберите диалог, чтобы посмотреть ленту сообщений.",
            ["Loading conversations..."] = "Загрузка диалогов...",
            ["No farmer conversations are available yet."] = "Диалоги с фермерами пока отсутствуют.",
            ["Advisory detail"] = "Детали advisory",
            ["Select an advisory to view diagnosis, treatment, and weather context."] = "Выберите advisory, чтобы посмотреть диагноз, рекомендации и погодный контекст.",
            ["Loading advisories..."] = "Загрузка advisory...",
            ["No advisories available yet."] = "Консультации пока отсутствуют.",
            ["Disease"] = "Болезнь",
            ["Confidence"] = "Уверенность",
            ["Source"] = "Источник",
            ["Follow-up"] = "Уточнение",
            ["Loading jobs..."] = "Загрузка задач...",
            ["No jobs matched this filter."] = "По этому фильтру задачи не найдены.",
            ["Loading stuck jobs..."] = "Загрузка зависших задач...",
            ["No stuck jobs right now."] = "Сейчас зависших задач нет.",
            ["Loading delivery issues..."] = "Загрузка проблем доставки...",
            ["No duplicate or problematic deliveries found."] = "Проблемных или дублирующихся доставок не найдено.",
            ["External message"] = "Внешнее сообщение",
            ["Duplicate"] = "Дубликат",
            ["Linked inbound"] = "Связанное входящее",
            ["Created"] = "Создано",
            ["Loading system status..."] = "Загрузка состояния системы...",
            ["Database"] = "База данных",
            ["Storage"] = "Хранилище",
            ["Weather"] = "Погода",
            ["Loading admin users..."] = "Загрузка админ-пользователей...",
            ["No admin users were found."] = "Админ-пользователи не найдены.",
            ["Name"] = "Имя",
            ["Status"] = "Статус",
            ["Roles"] = "Роли",
            ["Initial password"] = "Начальный пароль",
            ["Attempts"] = "Попытки",
            ["Scheduled"] = "Запланировано",
            ["Next attempt"] = "Следующая попытка",
            ["Last message"] = "Последнее сообщение",
            ["Location"] = "Локация",
            ["Channel"] = "Канал",
            ["Healthy"] = "Исправно",
            ["Unavailable"] = "Недоступно",
            ["No text payload was captured for this event."] = "Для этого события текстовый payload не был сохранён.",
            ["Create user"] = "Создать пользователя",
            ["Creating..."] = "Создание...",
            ["User controls"] = "Управление пользователем",
            ["Save roles"] = "Сохранить роли",
            ["Disable access"] = "Отключить доступ",
            ["Enable access"] = "Включить доступ",
            ["Reset password"] = "Сбросить пароль",
            ["Apply new password"] = "Применить новый пароль",
            ["Error"] = "Ошибка",
            ["An error occurred while processing your request."] = "Во время обработки запроса произошла ошибка.",
            ["Request ID"] = "ID запроса",
            ["Development Mode"] = "Режим разработки",
            ["FarmIQ API is unavailable. Confirm the API is running and the admin BaseUrl is correct."] = "FarmIQ API недоступен. Проверьте, что API запущен и BaseUrl в админке указан правильно.",
            ["Token response was empty."] = "Ответ с токеном был пустым.",
            ["Unable to load admin session."] = "Не удалось загрузить админ-сессию.",
            ["Unable to read admin session."] = "Не удалось прочитать админ-сессию.",
            ["Your admin session expired. Please sign in again."] = "Срок вашей админ-сессии истёк. Войдите снова.",
            ["The request could not be completed."] = "Не удалось выполнить запрос.",
            ["Email or password was invalid."] = "Неверный email или пароль.",
            ["This FarmIQ admin account is disabled or locked."] = "Этот FarmIQ admin аккаунт отключён или заблокирован."
        };

    private static readonly IReadOnlyDictionary<string, string> UzbekTranslations =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Language"] = "Til",
            ["Loading..."] = "Yuklanmoqda...",
            ["Nothing to show."] = "Ko'rsatish uchun ma'lumot yo'q.",
            ["Yes"] = "Ha",
            ["No"] = "Yo'q",
            ["Saved"] = "Saqlangan",
            ["Missing"] = "Yo'q",
            ["Not available"] = "Mavjud emas",
            ["None"] = "Yo'q",
            ["Refresh"] = "Yangilash",
            ["Apply"] = "Qo'llash",
            ["View"] = "Ko'rish",
            ["Manage"] = "Boshqarish",
            ["Retry"] = "Qayta urinish",
            ["Create"] = "Yaratish",
            ["Enable"] = "Yoqish",
            ["Disable"] = "O'chirish",
            ["Save"] = "Saqlash",
            ["Previous"] = "Oldingi",
            ["Next"] = "Keyingi",
            ["All"] = "Barchasi",
            ["Dashboard"] = "Dashboard",
            ["Conversations"] = "Suhbatlar",
            ["Jobs"] = "Ishlar",
            ["Deliveries"] = "Yetkazishlar",
            ["Advisories"] = "Maslahatlar",
            ["System"] = "Tizim",
            ["Users"] = "Foydalanuvchilar",
            ["Sign out"] = "Chiqish",
            ["Login"] = "Kirish",
            ["FarmIQ Admin"] = "FarmIQ Admin",
            ["Crop advisory control center"] = "Crop advisory boshqaruv markazi",
            ["Operations dashboard"] = "Operatsion panel",
            ["Rural advisory monitoring, diagnostics, and response"] = "Qishloq advisory monitoringi, diagnostika va javob",
            ["Restoring your operations session"] = "Operatsion sessiya tiklanmoqda",
            ["Loading the control center and validating the local admin session."] = "Control center yuklanmoqda va lokal admin sessiya tekshirilmoqda.",
            ["Access denied"] = "Kirish rad etildi",
            ["Your current FarmIQ role does not allow access to this area."] = "Sizning joriy FarmIQ rolingiz bu bo'limga kirishga ruxsat bermaydi.",
            ["Nothing matched that route."] = "Bu route topilmadi.",
            ["Sign in to the field operations console"] = "Dala operatsiyalari konsoliga kiring",
            ["Monitor farmer conversations, troubleshoot delivery issues, and recover advisory jobs from one place."] = "Fermer suhbatlarini kuzating, delivery muammolarini tekshiring va advisory ishlarini bir joydan tiklang.",
            ["Sign in"] = "Kirish",
            ["Signing in..."] = "Kirilmoqda...",
            ["Need an account?"] = "Hisob yo'qmi?",
            ["Create one here"] = "Shu yerda yarating",
            ["Invite-only access is enabled. Ask an existing admin to provision your account."] = "Faqat taklif orqali kirish yoqilgan. Hisob ochish uchun mavjud adminga murojaat qiling.",
            ["Create your operations account"] = "Operatsion hisob yarating",
            ["Invite-only access is active"] = "Faqat taklif orqali kirish faol",
            ["Set up a FarmIQ admin account for analytics review, advisory quality checks, and daily operations."] = "Analitika, advisory sifat nazorati va kundalik operatsiyalar uchun FarmIQ admin hisobini yarating.",
            ["Public signup is disabled in this environment. Ask an existing admin to create your account from the Users area."] = "Bu muhitda public signup o'chirilgan. Hisob ochish uchun mavjud adminga Users bo'limi orqali murojaat qiling.",
            ["Display name"] = "Ko'rinadigan ism",
            ["Email"] = "Email",
            ["Password"] = "Parol",
            ["Confirm password"] = "Parolni tasdiqlang",
            ["Create account"] = "Hisob yaratish",
            ["Creating account..."] = "Hisob yaratilmoqda...",
            ["Already have an account?"] = "Hisobingiz bormi?",
            ["This deployment accepts new users through the internal Admin Users screen only."] = "Bu deployda yangi foydalanuvchilar faqat ichki Admin Users oynasi orqali qo'shiladi.",
            ["System readiness"] = "Tizim tayyorligi",
            ["Current focus"] = "Joriy fokus",
            ["B2B insight snapshot"] = "B2B snapshot",
            ["Loading dashboard..."] = "Dashboard yuklanmoqda...",
            ["Farmers"] = "Fermerlar",
            ["Failed jobs"] = "Xatoli ishlar",
            ["Duplicate deliveries"] = "Takror delivery",
            ["Completed advisories"] = "Tugallangan maslahatlar",
            ["Stuck jobs"] = "Tiqilib qolgan ishlar",
            ["Command messages"] = "Buyruq xabarlari",
            ["Greetings / vague"] = "Salom / noaniq xabarlar",
            ["Follow-up advisories"] = "Qo'shimcha savolli advisory",
            ["Service health"] = "Servislar holati",
            ["Channel readiness"] = "Kanallar tayyorligi",
            ["Operational guidance"] = "Operatsion ko'rsatmalar",
            ["Conversation detail"] = "Suhbat tafsiloti",
            ["Select a conversation to inspect the message timeline."] = "Xabarlar vaqt chizig'ini ko'rish uchun suhbatni tanlang.",
            ["Loading conversations..."] = "Suhbatlar yuklanmoqda...",
            ["No farmer conversations are available yet."] = "Hozircha fermer suhbatlari yo'q.",
            ["Advisory detail"] = "Maslahat tafsiloti",
            ["Select an advisory to view diagnosis, treatment, and weather context."] = "Diagnoz, tavsiya va ob-havo kontekstini ko'rish uchun advisory tanlang.",
            ["Loading advisories..."] = "Maslahatlar yuklanmoqda...",
            ["No advisories available yet."] = "Hozircha maslahatlar yo'q.",
            ["Disease"] = "Kasallik",
            ["Confidence"] = "Ishonchlilik",
            ["Source"] = "Manba",
            ["Follow-up"] = "Qo'shimcha savol",
            ["Loading jobs..."] = "Ishlar yuklanmoqda...",
            ["No jobs matched this filter."] = "Bu filter bo'yicha ish topilmadi.",
            ["Loading stuck jobs..."] = "Stuck joblar yuklanmoqda...",
            ["No stuck jobs right now."] = "Hozir stuck job yo'q.",
            ["Loading delivery issues..."] = "Delivery issue'lar yuklanmoqda...",
            ["No duplicate or problematic deliveries found."] = "Duplicate yoki muammoli delivery topilmadi.",
            ["External message"] = "Tashqi xabar",
            ["Duplicate"] = "Takror",
            ["Linked inbound"] = "Bog'langan inbound",
            ["Created"] = "Yaratilgan",
            ["Loading system status..."] = "Tizim holati yuklanmoqda...",
            ["Database"] = "Baza",
            ["Storage"] = "Storage",
            ["Weather"] = "Ob-havo",
            ["Loading admin users..."] = "Admin foydalanuvchilar yuklanmoqda...",
            ["No admin users were found."] = "Admin foydalanuvchilar topilmadi.",
            ["Name"] = "Nomi",
            ["Status"] = "Holat",
            ["Roles"] = "Rollar",
            ["Initial password"] = "Boshlang'ich parol",
            ["Attempts"] = "Urinishlar",
            ["Scheduled"] = "Rejalashtirilgan",
            ["Next attempt"] = "Keyingi urinish",
            ["Last message"] = "Oxirgi xabar",
            ["Location"] = "Joylashuv",
            ["Channel"] = "Kanal",
            ["Healthy"] = "Sog'lom",
            ["Unavailable"] = "Mavjud emas",
            ["No text payload was captured for this event."] = "Bu event uchun text payload saqlanmagan.",
            ["Create user"] = "Foydalanuvchi yaratish",
            ["Creating..."] = "Yaratilmoqda...",
            ["User controls"] = "Foydalanuvchi boshqaruvi",
            ["Save roles"] = "Rollarni saqlash",
            ["Disable access"] = "Kirishni o'chirish",
            ["Enable access"] = "Kirishni yoqish",
            ["Reset password"] = "Parolni yangilash",
            ["Apply new password"] = "Yangi parolni qo'llash",
            ["Error"] = "Xato",
            ["An error occurred while processing your request."] = "So'rovni qayta ishlash vaqtida xatolik yuz berdi.",
            ["Request ID"] = "So'rov ID",
            ["Development Mode"] = "Development rejimi",
            ["FarmIQ API is unavailable. Confirm the API is running and the admin BaseUrl is correct."] = "FarmIQ API ishlamayapti. API ishga tushganini va admin BaseUrl to'g'ri ekanini tekshiring.",
            ["Token response was empty."] = "Token javobi bo'sh qaytdi.",
            ["Unable to load admin session."] = "Admin sessiyani yuklab bo'lmadi.",
            ["Unable to read admin session."] = "Admin sessiyani o'qib bo'lmadi.",
            ["Your admin session expired. Please sign in again."] = "Admin sessiyangiz tugadi. Qayta kiring.",
            ["The request could not be completed."] = "So'rovni bajarib bo'lmadi.",
            ["Email or password was invalid."] = "Email yoki parol noto'g'ri.",
            ["This FarmIQ admin account is disabled or locked."] = "Bu FarmIQ admin hisobi o'chirilgan yoki bloklangan."
        };

    public bool IsInitialized { get; private set; }
    public string CurrentLanguage { get; private set; } = FarmLanguages.English;
    public event Action? Changed;

    public string this[string englishText] => Translate(englishText);

    public async Task RestoreAsync()
    {
        if (IsInitialized)
        {
            return;
        }

        var savedLanguage = await preferenceStore.LoadStringAsync(LanguageKey);
        CurrentLanguage = FarmLanguages.Normalize(string.IsNullOrWhiteSpace(savedLanguage)
            ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
            : savedLanguage);
        await preferenceStore.SetDocumentLanguageAsync(CurrentLanguage);
        IsInitialized = true;
        Changed?.Invoke();
    }

    public async Task SetLanguageAsync(string language)
    {
        var normalized = FarmLanguages.Normalize(language);
        if (IsInitialized && string.Equals(normalized, CurrentLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        CurrentLanguage = normalized;
        await preferenceStore.SaveStringAsync(LanguageKey, normalized);
        await preferenceStore.SetDocumentLanguageAsync(normalized);
        IsInitialized = true;
        Changed?.Invoke();
    }

    public string Translate(string englishText)
    {
        if (string.IsNullOrWhiteSpace(englishText))
        {
            return englishText;
        }

        var source = CurrentLanguage switch
        {
            FarmLanguages.Russian => RussianTranslations,
            FarmLanguages.Uzbek => UzbekTranslations,
            _ => null
        };

        if (source is null || !source.TryGetValue(englishText, out var translated))
        {
            return englishText;
        }

        return translated;
    }

    public string Format(string englishFormat, params object[] args) =>
        string.Format(GetCulture(), Translate(englishFormat), args);

    public string Bool(bool value) => Translate(value ? "Yes" : "No");

    public string Role(string role) => CurrentLanguage switch
    {
        FarmLanguages.Russian => role switch
        {
            "Admin" => "Админ",
            "Ops" => "Операции",
            "Analyst" => "Аналитик",
            _ => role
        },
        FarmLanguages.Uzbek => role switch
        {
            "Admin" => "Admin",
            "Ops" => "Operatsiya",
            "Analyst" => "Analitik",
            _ => role
        },
        _ => role
    };

    public string Channel(ChannelType channel) => CurrentLanguage switch
    {
        FarmLanguages.Russian => channel switch
        {
            ChannelType.Sms => "SMS",
            _ => channel.ToString()
        },
        FarmLanguages.Uzbek => channel switch
        {
            ChannelType.Sms => "SMS",
            _ => channel.ToString()
        },
        _ => channel.ToString()
    };

    public string Intent(InboundIntentType? intent) => intent switch
    {
        null => Translate("Not available"),
        InboundIntentType.Unknown => Translate("Unknown"),
        InboundIntentType.StartCommand => CurrentLanguage == FarmLanguages.Russian ? "Команда старта" : CurrentLanguage == FarmLanguages.Uzbek ? "Start buyrug'i" : "Start command",
        InboundIntentType.HelpCommand => CurrentLanguage == FarmLanguages.Russian ? "Команда помощи" : CurrentLanguage == FarmLanguages.Uzbek ? "Yordam buyrug'i" : "Help command",
        InboundIntentType.Greeting => CurrentLanguage == FarmLanguages.Russian ? "Приветствие" : CurrentLanguage == FarmLanguages.Uzbek ? "Salomlashuv" : "Greeting",
        InboundIntentType.SmallTalk => CurrentLanguage == FarmLanguages.Russian ? "Обычный разговор" : CurrentLanguage == FarmLanguages.Uzbek ? "Oddiy suhbat" : "Small talk",
        InboundIntentType.SymptomReport => CurrentLanguage == FarmLanguages.Russian ? "Описание симптомов" : CurrentLanguage == FarmLanguages.Uzbek ? "Simptom tavsifi" : "Symptom report",
        InboundIntentType.MediaOnly => CurrentLanguage == FarmLanguages.Russian ? "Только медиа" : CurrentLanguage == FarmLanguages.Uzbek ? "Faqat media" : "Media only",
        InboundIntentType.LocationShare => CurrentLanguage == FarmLanguages.Russian ? "Отправка геолокации" : CurrentLanguage == FarmLanguages.Uzbek ? "Joylashuv yuborildi" : "Location share",
        InboundIntentType.Unsupported => CurrentLanguage == FarmLanguages.Russian ? "Не поддерживается" : CurrentLanguage == FarmLanguages.Uzbek ? "Qo'llab-quvvatlanmaydi" : "Unsupported",
        _ => intent?.ToString() ?? Translate("Not available")
    };

    public string AssistantState(ConversationAssistantState? state) => state switch
    {
        null => Translate("Not available"),
        ConversationAssistantState.Idle => CurrentLanguage == FarmLanguages.Russian ? "Ожидание" : CurrentLanguage == FarmLanguages.Uzbek ? "Kutishda" : "Idle",
        ConversationAssistantState.AwaitingProblemDetails => CurrentLanguage == FarmLanguages.Russian ? "Ожидание деталей проблемы" : CurrentLanguage == FarmLanguages.Uzbek ? "Muammo tafsilotlarini kutmoqda" : "Awaiting problem details",
        ConversationAssistantState.AwaitingPhoto => CurrentLanguage == FarmLanguages.Russian ? "Ожидание фото" : CurrentLanguage == FarmLanguages.Uzbek ? "Rasm kutmoqda" : "Awaiting photo",
        ConversationAssistantState.AwaitingLocation => CurrentLanguage == FarmLanguages.Russian ? "Ожидание геолокации" : CurrentLanguage == FarmLanguages.Uzbek ? "Joylashuv kutmoqda" : "Awaiting location",
        ConversationAssistantState.AdvisorySent => CurrentLanguage == FarmLanguages.Russian ? "Совет отправлен" : CurrentLanguage == FarmLanguages.Uzbek ? "Maslahat yuborilgan" : "Advisory sent",
        _ => state?.ToString() ?? Translate("Not available")
    };

    public string JobStatus(ProcessingJobStatus? status) => status switch
    {
        null => Translate("Not available"),
        ProcessingJobStatus.Pending => CurrentLanguage == FarmLanguages.Russian ? "В очереди" : CurrentLanguage == FarmLanguages.Uzbek ? "Navbatda" : "Pending",
        ProcessingJobStatus.InProgress => CurrentLanguage == FarmLanguages.Russian ? "В работе" : CurrentLanguage == FarmLanguages.Uzbek ? "Jarayonda" : "In progress",
        ProcessingJobStatus.Completed => CurrentLanguage == FarmLanguages.Russian ? "Завершено" : CurrentLanguage == FarmLanguages.Uzbek ? "Tugallangan" : "Completed",
        ProcessingJobStatus.Failed => CurrentLanguage == FarmLanguages.Russian ? "Ошибка" : CurrentLanguage == FarmLanguages.Uzbek ? "Xatolik" : "Failed",
        ProcessingJobStatus.Retrying => CurrentLanguage == FarmLanguages.Russian ? "Повтор" : CurrentLanguage == FarmLanguages.Uzbek ? "Qayta urinish" : "Retrying",
        _ => status?.ToString() ?? Translate("Not available")
    };

    public string AnalysisSource(AdvisoryAnalysisSource? source) => source switch
    {
        null => Translate("Not available"),
        AdvisoryAnalysisSource.Fallback => CurrentLanguage == FarmLanguages.Russian ? "Резервный режим" : CurrentLanguage == FarmLanguages.Uzbek ? "Fallback" : "Fallback",
        AdvisoryAnalysisSource.OpenAi => "OpenAI",
        AdvisoryAnalysisSource.Glm => "GLM-5.1",
        _ => source?.ToString() ?? Translate("Not available")
    };

    public string Direction(string? direction) => direction switch
    {
        null => Translate("Not available"),
        "Inbound" => CurrentLanguage == FarmLanguages.Russian ? "Входящее" : CurrentLanguage == FarmLanguages.Uzbek ? "Kiruvchi" : "Inbound",
        "Outbound" => CurrentLanguage == FarmLanguages.Russian ? "Исходящее" : CurrentLanguage == FarmLanguages.Uzbek ? "Chiquvchi" : "Outbound",
        _ => direction
    };

    public string LanguageName(string languageCode) => FarmLanguages.Normalize(languageCode) switch
    {
        FarmLanguages.Russian => "Русский",
        FarmLanguages.Uzbek => "O'zbekcha",
        _ => "English"
    };

    public string FormatDateTime(DateTime value) => value.ToString("g", GetCulture());

    public string FormatDateTime(DateTimeOffset value) => value.ToString("g", GetCulture());

    public string FormatNullableDateTime(DateTime? value, string fallbackEnglish = "Not available") =>
        value.HasValue ? FormatDateTime(value.Value) : Translate(fallbackEnglish);

    public string FormatNullableDateTime(DateTimeOffset? value, string fallbackEnglish = "Not available") =>
        value.HasValue ? FormatDateTime(value.Value) : Translate(fallbackEnglish);

    public string FormatPercent(decimal value) => value.ToString("P0", GetCulture());

    public string FormatNumber(int value) => value.ToString("N0", GetCulture());

    public string NormalizeApiMessage(string? errorCode, string? message)
    {
        if (string.Equals(errorCode, "public_signup_disabled", StringComparison.OrdinalIgnoreCase))
        {
            return Translate("Public signup is disabled. Ask an existing admin to create your account.");
        }

        if (string.Equals(errorCode, "account_disabled", StringComparison.OrdinalIgnoreCase))
        {
            return Translate("This FarmIQ admin account is disabled or locked.");
        }

        return message switch
        {
            null or "" => Translate("The request could not be completed."),
            "Token response was empty." => Translate("Token response was empty."),
            "Unable to load admin session." => Translate("Unable to load admin session."),
            "Unable to read admin session." => Translate("Unable to read admin session."),
            "Your admin session expired. Please sign in again." => Translate("Your admin session expired. Please sign in again."),
            "Email or password was invalid." => Translate("Email or password was invalid."),
            "An account with that email already exists." => Translate("An account with that email already exists."),
            "Public signup is disabled. Ask an existing admin to create your account." => Translate("Public signup is disabled. Ask an existing admin to create your account."),
            "This FarmIQ admin account is disabled or locked." => Translate("This FarmIQ admin account is disabled or locked."),
            _ => message.Trim('"')
        };
    }

    private CultureInfo GetCulture() => CurrentLanguage switch
    {
        FarmLanguages.Russian => CultureInfo.GetCultureInfo("ru-RU"),
        FarmLanguages.Uzbek => CultureInfo.GetCultureInfo("uz-Latn-UZ"),
        _ => CultureInfo.GetCultureInfo("en-US")
    };
}
