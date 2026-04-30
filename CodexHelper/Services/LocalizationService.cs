using System.Globalization;
using CodexHelper.Infrastructure;

namespace CodexHelper.Services;

public sealed class LocalizationService : ObservableObject
{
    private readonly Dictionary<string, Dictionary<string, string>> _strings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["AppTitle"] = "CodexHelper",
            ["Refresh"] = "Refresh",
            ["Elevate"] = "Elevate",
            ["Archive"] = "Archive",
            ["Diagnostics"] = "Diagnostics",
            ["ClearSelection"] = "Clear",
            ["RefreshToolTip"] = "Reload the session tree from Codex storage.",
            ["ElevateToolTip"] = "Archive and restore selected sessions so they appear in Codex again.",
            ["ArchiveToolTip"] = "Move selected sessions to the Codex archive.",
            ["DiagnosticsToolTip"] = "Show CodexHelper diagnostics.",
            ["ClearSelectionToolTip"] = "Clear all checked sessions.",
            ["SelectedSummaryToolTip"] = "Number of checked sessions.",
            ["ReadOnlyMode"] = "Read-only",
            ["ReadOnlyModeToolTip"] = "Codex app-server is unavailable. Sessions are loaded from local rollout files, so archive and elevate are disabled.",
            ["ThreadFilterToolTip"] = "Choose which sessions are shown in the tree.",
            ["LanguageToolTip"] = "Switch the interface language.",
            ["Copy"] = "Copy",
            ["AllThreadsFilter"] = "All threads",
            ["ActiveThreadsFilter"] = "Active threads",
            ["ArchivedThreadsFilter"] = "Archived threads",
            ["Search"] = "Search",
            ["Projects"] = "Projects",
            ["Conversation"] = "Conversation",
            ["Details"] = "Details",
            ["Language"] = "Language",
            ["AllProjects"] = "All projects",
            ["Chats"] = "Chats",
            ["UnknownProject"] = "Unknown",
            ["UntitledThread"] = "Untitled",
            ["NoSession"] = "Select a session to preview the conversation.",
            ["NoMessages"] = "No displayable messages were returned for this session.",
            ["ConversationTruncated"] = "Showing the latest {0:N0} of {1:N0} messages.",
            ["Loading"] = "Loading...",
            ["Ready"] = "Ready",
            ["Active"] = "Active",
            ["Archived"] = "Archived",
            ["OpenInCodex"] = "Open in Codex",
            ["ThreadOpenInCodex"] = "This session is open in Codex. Waiting for the rollout file to be released.",
            ["Selected"] = "selected",
            ["Updated"] = "Updated",
            ["Timestamp"] = "Created",
            ["ModelEffort"] = "Model",
            ["Project"] = "Project",
            ["ThreadId"] = "Thread ID",
            ["State"] = "State",
            ["DialogTab"] = "Dialog",
            ["DeveloperInstructionsTab"] = "OpenAI instructions",
            ["UserInstructionsTab"] = "User instructions",
            ["ParametersTab"] = "Parameters",
            ["NoInstructions"] = "No instructions were found for this session.",
            ["NoParameters"] = "No parameters were found for this session.",
            ["CodexCliMissing"] = "Codex CLI was not found on PATH. Install Codex and make sure the `codex` command is available from a terminal.",
            ["CodexAppServerUnsupported"] = "Codex CLI is installed, but this version does not support `codex app-server`. Update Codex CLI or install a version with app-server support.",
            ["ArchiveDone"] = "Archived {0:N0} session(s).",
            ["ElevateDone"] = "Elevated {0:N0} session(s).",
            ["RefreshDone"] = "Loaded {0:N0} session(s) across {1:N0} project(s).",
            ["ReadOnlyRefreshDone"] = "Loaded {0:N0} session(s) across {1:N0} project(s) from local rollout files. Read-only mode: archive and elevate are unavailable.",
            ["OperationPartialDone"] = "Completed {0:N0} session(s), failed {1:N0}. Last error: {2}",
            ["ReadOnlyOperationUnavailable"] = "This operation is unavailable in read-only mode because Codex app-server is not running.",
            ["OperationFailed"] = "Operation failed: {0}",
            ["OpenFailed"] = "Could not read this session: {0}",
            ["LoadingSession"] = "Loading session...",
            ["LoadingDiagnostics"] = "Loading diagnostics...",
            ["DiagnosticsTitle"] = "CodexHelper diagnostics",
            ["DiagnosticsMode"] = "Mode",
            ["DiagnosticsModeReadOnly"] = "Read-only",
            ["DiagnosticsModeAppServer"] = "App-server",
            ["DiagnosticsCodexProbe"] = "Codex probe",
            ["DiagnosticsSelectedCodex"] = "Selected Codex",
            ["DiagnosticsCodexCandidates"] = "Codex candidates",
            ["DiagnosticsCodexHome"] = "Codex home",
            ["DiagnosticsSessions"] = "Sessions",
            ["DiagnosticsArchivedSessions"] = "Archived sessions",
            ["DiagnosticsActiveRolloutFiles"] = "Active rollout files",
            ["DiagnosticsArchivedRolloutFiles"] = "Archived rollout files",
            ["DiagnosticsSettings"] = "Settings",
            ["DiagnosticsRolloutIndexCache"] = "Rollout index cache",
            ["DiagnosticsLog"] = "Log",
            ["DiagnosticsNone"] = "(none)",
            ["DiagnosticsNotChecked"] = "(not checked)",
            ["DiagnosticsCopyAction"] = "Yes: copy diagnostics",
            ["DiagnosticsOpenLogAction"] = "No: open log file",
            ["DiagnosticsCloseAction"] = "Cancel: close",
            ["RelativeMinutesSuffix"] = "m",
            ["RelativeHoursSuffix"] = "h",
            ["RelativeDaysSuffix"] = "d",
            ["RelativeWeeksSuffix"] = "w",
            ["RelativeMonthsSuffix"] = "mo",
            ["RelativeYearsSuffix"] = "y",
            ["NoSelection"] = "No sessions selected.",
            ["User"] = "User",
            ["Assistant"] = "Assistant",
            ["System"] = "System",
            ["Tool"] = "Tool",
            ["Reasoning"] = "Reasoning",
            ["Error"] = "Error",
            ["Unknown"] = "Unknown"
        },
        ["ru"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["AppTitle"] = "CodexHelper",
            ["Refresh"] = "Обновить",
            ["Elevate"] = "Поднять",
            ["Archive"] = "В архив",
            ["Diagnostics"] = "Диагностика",
            ["ClearSelection"] = "Снять",
            ["RefreshToolTip"] = "Обновить дерево сессий из хранилища Codex.",
            ["ElevateToolTip"] = "Архивировать и восстановить выбранные сессии, чтобы они снова появились в Codex.",
            ["ArchiveToolTip"] = "Переместить выбранные сессии в архив Codex.",
            ["DiagnosticsToolTip"] = "Показать диагностику CodexHelper.",
            ["ClearSelectionToolTip"] = "Снять галочки со всех выбранных сессий.",
            ["SelectedSummaryToolTip"] = "Количество отмеченных сессий.",
            ["ReadOnlyMode"] = "Только чтение",
            ["ReadOnlyModeToolTip"] = "Codex app-server недоступен. Сессии загружены из локальных rollout-файлов, поэтому архивация и поднятие отключены.",
            ["ThreadFilterToolTip"] = "Выбрать, какие сессии показывать в дереве.",
            ["LanguageToolTip"] = "Переключить язык интерфейса.",
            ["Copy"] = "Копировать",
            ["AllThreadsFilter"] = "Все треды",
            ["ActiveThreadsFilter"] = "Активные треды",
            ["ArchivedThreadsFilter"] = "Архивные треды",
            ["Search"] = "Поиск",
            ["Projects"] = "Проекты",
            ["Conversation"] = "Диалог",
            ["Details"] = "Детали",
            ["Language"] = "Язык",
            ["AllProjects"] = "Все проекты",
            ["Chats"] = "Чаты",
            ["UnknownProject"] = "Неизвестно",
            ["UntitledThread"] = "Без названия",
            ["NoSession"] = "Выбери сессию, чтобы увидеть диалог.",
            ["NoMessages"] = "Для этой сессии не удалось получить сообщения для отображения.",
            ["ConversationTruncated"] = "Показаны последние {0:N0} из {1:N0} сообщений.",
            ["Loading"] = "Загрузка...",
            ["Ready"] = "Готово",
            ["Active"] = "Активна",
            ["Archived"] = "В архиве",
            ["OpenInCodex"] = "Открыт в Codex",
            ["ThreadOpenInCodex"] = "Эта сессия открыта в Codex. Жду освобождения rollout-файла.",
            ["Selected"] = "выбрано",
            ["Updated"] = "Обновлено",
            ["Timestamp"] = "Создано",
            ["ModelEffort"] = "Модель",
            ["Project"] = "Проект",
            ["ThreadId"] = "ID треда",
            ["State"] = "Состояние",
            ["DialogTab"] = "Диалог",
            ["DeveloperInstructionsTab"] = "Инструкции OpenAI",
            ["UserInstructionsTab"] = "Инструкции пользователя",
            ["ParametersTab"] = "Параметры",
            ["NoInstructions"] = "Инструкции для этой сессии не найдены.",
            ["NoParameters"] = "Параметры для этой сессии не найдены.",
            ["CodexCliMissing"] = "Codex CLI не найден в PATH. Установи Codex и проверь, что команда `codex` доступна из терминала.",
            ["CodexAppServerUnsupported"] = "Codex CLI установлен, но эта версия не поддерживает `codex app-server`. Обнови Codex CLI или установи версию с поддержкой app-server.",
            ["ArchiveDone"] = "Архивировано сессий: {0:N0}.",
            ["ElevateDone"] = "Поднято сессий: {0:N0}.",
            ["RefreshDone"] = "Загружено сессий: {0:N0}, проектов: {1:N0}.",
            ["ReadOnlyRefreshDone"] = "Загружено сессий из локальных rollout-файлов: {0:N0}, проектов: {1:N0}. Режим только чтения: архивация и поднятие недоступны.",
            ["OperationPartialDone"] = "Выполнено сессий: {0:N0}, с ошибкой: {1:N0}. Последняя ошибка: {2}",
            ["ReadOnlyOperationUnavailable"] = "Эта операция недоступна в режиме только чтения, потому что Codex app-server не запущен.",
            ["OperationFailed"] = "Операция не удалась: {0}",
            ["OpenFailed"] = "Не удалось прочитать сессию: {0}",
            ["LoadingSession"] = "Загружаю сессию...",
            ["LoadingDiagnostics"] = "Загружаю диагностику...",
            ["DiagnosticsTitle"] = "Диагностика CodexHelper",
            ["DiagnosticsMode"] = "Режим",
            ["DiagnosticsModeReadOnly"] = "Только чтение",
            ["DiagnosticsModeAppServer"] = "App-server",
            ["DiagnosticsCodexProbe"] = "Проверка Codex",
            ["DiagnosticsSelectedCodex"] = "Выбранный Codex",
            ["DiagnosticsCodexCandidates"] = "Кандидаты Codex",
            ["DiagnosticsCodexHome"] = "Домашняя папка Codex",
            ["DiagnosticsSessions"] = "Сессии",
            ["DiagnosticsArchivedSessions"] = "Архивные сессии",
            ["DiagnosticsActiveRolloutFiles"] = "Активные rollout-файлы",
            ["DiagnosticsArchivedRolloutFiles"] = "Архивные rollout-файлы",
            ["DiagnosticsSettings"] = "Настройки",
            ["DiagnosticsRolloutIndexCache"] = "Кэш индекса rollout",
            ["DiagnosticsLog"] = "Лог",
            ["DiagnosticsNone"] = "(нет)",
            ["DiagnosticsNotChecked"] = "(не проверялось)",
            ["DiagnosticsCopyAction"] = "Да: скопировать диагностику",
            ["DiagnosticsOpenLogAction"] = "Нет: открыть лог-файл",
            ["DiagnosticsCloseAction"] = "Отмена: закрыть",
            ["RelativeMinutesSuffix"] = "мин",
            ["RelativeHoursSuffix"] = "ч",
            ["RelativeDaysSuffix"] = "д",
            ["RelativeWeeksSuffix"] = "н",
            ["RelativeMonthsSuffix"] = "мес",
            ["RelativeYearsSuffix"] = "г",
            ["NoSelection"] = "Сессии не выбраны.",
            ["User"] = "Пользователь",
            ["Assistant"] = "Ассистент",
            ["System"] = "Система",
            ["Tool"] = "Инструмент",
            ["Reasoning"] = "Рассуждение",
            ["Error"] = "Ошибка",
            ["Unknown"] = "Неизвестно"
        }
    };

    private string _language = "en";

    public event EventHandler? LanguageChanged;

    public CultureInfo Culture => CultureInfo.CurrentCulture;

    public string Language
    {
        get => _language;
        set
        {
            var normalized = _strings.ContainsKey(value) ? value : "en";
            if (SetProperty(ref _language, normalized))
            {
                LanguageChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public string this[string key]
    {
        get
        {
            if (_strings.TryGetValue(Language, out var languageStrings) &&
                languageStrings.TryGetValue(key, out var value))
            {
                return value;
            }

            return _strings["en"].TryGetValue(key, out var fallback) ? fallback : key;
        }
    }

    public string Format(string format, params object?[] args)
    {
        return string.Format(Culture, format, args);
    }

    public string FormatShortDateTime(DateTimeOffset value)
    {
        return value.LocalDateTime.ToString("g", Culture);
    }

    public string FormatShortDateTimeWithAge(DateTimeOffset value)
    {
        return $"{FormatShortDateTime(value)} ({FormatElapsedSince(value, DateTimeOffset.Now)})";
    }

    public string FormatLongDateTime(DateTimeOffset value)
    {
        return value.LocalDateTime.ToString("G", Culture);
    }

    public string FormatNumber(long value)
    {
        return value.ToString("N0", Culture);
    }

    public string FormatElapsedSince(DateTimeOffset value, DateTimeOffset now)
    {
        var elapsed = now - value;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        if (elapsed.TotalMinutes < 60)
        {
            return FormatRelativeAge((long)Math.Floor(elapsed.TotalMinutes), "RelativeMinutesSuffix");
        }

        if (elapsed.TotalHours < 24)
        {
            return FormatRelativeAge((long)Math.Floor(elapsed.TotalHours), "RelativeHoursSuffix");
        }

        if (elapsed.TotalDays < 7)
        {
            return FormatRelativeAge((long)Math.Floor(elapsed.TotalDays), "RelativeDaysSuffix");
        }

        if (elapsed.TotalDays < 30)
        {
            return FormatRelativeAge(Math.Max(1, (long)Math.Floor(elapsed.TotalDays / 7)), "RelativeWeeksSuffix");
        }

        if (elapsed.TotalDays < 365)
        {
            return FormatRelativeAge(Math.Max(1, (long)Math.Floor(elapsed.TotalDays / 30.436875)), "RelativeMonthsSuffix");
        }

        return FormatRelativeAge(Math.Max(1, (long)Math.Floor(elapsed.TotalDays / 365.2425)), "RelativeYearsSuffix");
    }

    private string FormatRelativeAge(long value, string suffixKey)
    {
        return $"{value.ToString("N0", Culture)}{this[suffixKey]}";
    }
}
