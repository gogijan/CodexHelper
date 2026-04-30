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
            ["ClearSelection"] = "Clear",
            ["RefreshToolTip"] = "Reload the session tree from Codex storage.",
            ["ElevateToolTip"] = "Archive and restore selected sessions so they appear in Codex again.",
            ["ArchiveToolTip"] = "Move selected sessions to the Codex archive.",
            ["ClearSelectionToolTip"] = "Clear all checked sessions.",
            ["SelectedSummaryToolTip"] = "Number of checked sessions.",
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
            ["CodexCliMissing"] = "Codex CLI was not found on PATH.",
            ["ArchiveDone"] = "Archived {0} session(s).",
            ["ElevateDone"] = "Elevated {0} session(s).",
            ["RefreshDone"] = "Loaded {0} session(s) across {1} project(s).",
            ["OperationFailed"] = "Operation failed: {0}",
            ["OpenFailed"] = "Could not read this session: {0}",
            ["LoadingSession"] = "Loading session...",
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
            ["ClearSelection"] = "Снять",
            ["RefreshToolTip"] = "Обновить дерево сессий из хранилища Codex.",
            ["ElevateToolTip"] = "Архивировать и восстановить выбранные сессии, чтобы они снова появились в Codex.",
            ["ArchiveToolTip"] = "Переместить выбранные сессии в архив Codex.",
            ["ClearSelectionToolTip"] = "Снять галочки со всех выбранных сессий.",
            ["SelectedSummaryToolTip"] = "Количество отмеченных сессий.",
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
            ["CodexCliMissing"] = "Codex CLI не найден в PATH.",
            ["ArchiveDone"] = "Архивировано сессий: {0}.",
            ["ElevateDone"] = "Поднято сессий: {0}.",
            ["RefreshDone"] = "Загружено сессий: {0}, проектов: {1}.",
            ["OperationFailed"] = "Операция не удалась: {0}",
            ["OpenFailed"] = "Не удалось прочитать сессию: {0}",
            ["LoadingSession"] = "Загружаю сессию...",
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
