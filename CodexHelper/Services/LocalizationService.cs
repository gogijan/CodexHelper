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
            ["ConversationTruncated"] = "Showing the latest {0} of {1} messages.",
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
            ["ArchiveDone"] = "Archived {0} session(s).",
            ["ElevateDone"] = "Elevated {0} session(s).",
            ["RefreshDone"] = "Loaded {0} session(s) across {1} project(s).",
            ["ReadOnlyRefreshDone"] = "Loaded {0} session(s) across {1} project(s) from local rollout files. Read-only mode: archive and elevate are unavailable.",
            ["OperationPartialDone"] = "Completed {0} session(s), failed {1}. Last error: {2}",
            ["ReadOnlyOperationUnavailable"] = "This operation is unavailable in read-only mode because Codex app-server is not running.",
            ["OperationFailed"] = "Operation failed: {0}",
            ["OpenFailed"] = "Could not read this session: {0}",
            ["LoadingSession"] = "Loading session...",
            ["LoadingDiagnostics"] = "Loading diagnostics...",
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
            ["AppTitle"] = "Браузер сессий Codex",
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
            ["ConversationTruncated"] = "Показаны последние {0} из {1} сообщений.",
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
            ["ArchiveDone"] = "Архивировано сессий: {0}.",
            ["ElevateDone"] = "Поднято сессий: {0}.",
            ["RefreshDone"] = "Загружено сессий: {0}, проектов: {1}.",
            ["ReadOnlyRefreshDone"] = "Загружено сессий из локальных rollout-файлов: {0}, проектов: {1}. Режим только чтения: архивация и поднятие недоступны.",
            ["OperationPartialDone"] = "Выполнено сессий: {0}, с ошибкой: {1}. Последняя ошибка: {2}",
            ["ReadOnlyOperationUnavailable"] = "Эта операция недоступна в режиме только чтения, потому что Codex app-server не запущен.",
            ["OperationFailed"] = "Операция не удалась: {0}",
            ["OpenFailed"] = "Не удалось прочитать сессию: {0}",
            ["LoadingSession"] = "Загружаю сессию...",
            ["LoadingDiagnostics"] = "Загружаю диагностику...",
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
}
