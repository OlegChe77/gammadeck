using System;
using System.Collections.Generic;
using System.Globalization;

namespace GammaDeck
{
    /// <summary>
    /// Локализация: ключ — английская строка, как она написана в коде; для русского ищется перевод в таблице,
    /// не нашли — остаётся английская. Имена встроенных профилей в конфиге всегда английские, переводятся только на экране.
    /// </summary>
    internal static class Loc
    {
        public const string En = "en", Ru = "ru";

        public static string Lang = En;

        public static bool IsRu { get { return Lang == Ru; } }

        /// <summary>Язык Windows (язык интерфейса): русский → "ru", всё остальное → "en".</summary>
        public static string DetectSystem()
        {
            try { return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? Ru : En; }
            catch { return En; }
        }

        public static string Normalize(string lang)
        {
            return lang == Ru || lang == En ? lang : DetectSystem();
        }

        public static void Set(string lang)
        {
            Lang = Normalize(lang);
        }

        public static string T(string en)
        {
            if (!IsRu || en == null) return en;
            string ru;
            return Table.TryGetValue(en, out ru) ? ru : en;
        }

        public static string F(string en, params object[] args)
        {
            return string.Format(T(en), args);
        }

        /// <summary>Имя профиля для показа: встроенные переводятся, пользовательские остаются как есть.</summary>
        public static string ProfileName(string stored)
        {
            if (!IsRu || stored == null) return stored;
            string ru;
            return Builtin.TryGetValue(stored, out ru) ? ru : stored;
        }

        private static readonly Dictionary<string, string> Builtin = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Default", "Стандарт" }, { "Bright shooter", "Яркий шутер" }, { "Dark games", "Тёмные игры" },
            { "Vivid colors", "Сочные цвета" }, { "Cinema", "Кино" }, { "Warm (evening)", "Тёплый (вечер)" },
            { "Cold", "Холодный" }, { "Black and white", "Чёрно-белый" }
        };

        private static readonly Dictionary<string, string> Table = Build(new[]
        {
            // окно и шапка
            "GammaDeck — monitor color for games and streams", "GammaDeck — цвет монитора для игр и стримов",
            "Brightness, contrast, gamma, NVIDIA digital vibrance and hue — one click away", "Яркость, контраст, гамма, цифровая интенсивность и оттенок NVIDIA — по одной кнопке",
            "♥ Help the project", "♥ Помощь проекту",
            "Color and profiles", "Цвет и профили",
            "Hotkeys", "Горячие клавиши",
            "Games (auto-switch)", "Игры (автопереключение)",
            "Settings", "Настройки",
            // страница цвета
            "Profiles", "Профили",
            "Click to apply, right-click for the profile menu", "Клик — применить, правая кнопка — меню профиля",
            "Save to profile", "Сохранить в профиль",
            "Save to '{0}'", "Сохранить в «{0}»",
            "Save as new profile", "Сохранить как новый профиль",
            "+ New profile", "+ Новый профиль",
            "Restore built-ins", "Вернуть встроенные",
            "Desktop color settings", "Настройка цвета рабочего стола",
            "Channel:", "Канал:",
            "All channels", "Все каналы", "Red", "Красный", "Green", "Зелёный", "Blue", "Синий",
            "Brightness", "Яркость", "Contrast", "Контрастность", "Gamma", "Гамма",
            "NVIDIA color enhancements", "Улучшения цвета NVIDIA",
            "Digital vibrance", "Цифровая интенсивность", "Hue", "Оттенок",
            "Reset channel", "Сбросить канал", "Reset all", "Сбросить всё",
            "Double-click a slider to reset it. Mouse wheel and arrow keys fine-tune (hold Shift for steps of 10).\n", "Двойной клик по ползунку — значение по умолчанию. Колесо мыши и стрелки — точная настройка (с Shift — шаг 10).\n",
            "Only you see these changes: OBS and recordings keep the normal picture.", "Изменения видите только вы: в OBS и на записи картинка остаётся обычной.",
            "NVIDIA driver (nvapi64.dll) not found — vibrance and hue are unavailable.", "Драйвер NVIDIA (nvapi64.dll) не найден — интенсивность и оттенок недоступны.",
            "The driver supports vibrance only from 50% and up.", "Драйвер поддерживает интенсивность только от 50% и выше.",
            "— all monitors", "— все мониторы",
            "— monitor {0}", "— монитор {0}",
            // меню профиля
            "Apply", "Применить",
            "Apply to monitor", "Применить на монитор",
            "All monitors", "Все мониторы",
            "Save current settings here", "Записать сюда текущие настройки",
            "Rename…", "Переименовать…",
            "Button color…", "Цвет кнопки…",
            "Hotkey…", "Горячая клавиша…",
            "Hotkey for a specific monitor", "Горячая клавиша для конкретного монитора",
            "Duplicate", "Дублировать",
            "Move earlier", "Переместить раньше",
            "Move later", "Переместить позже",
            "Delete", "Удалить",
            "Monitor {0}", "Монитор {0}",
            "Monitor {0}:  {1}", "Монитор {0}:  {1}",
            "Monitor", "Монитор",
            "Different profiles", "Разные профили",
            "Add monitor", "Добавить монитор",
            "no hotkey", "без клавиши",
            // строка состояния
            "No new monitors found.\n\nConnect the monitor and enable it in Windows (Win+P → Extend), then click + again.\n\nOpen Windows display settings?",
                "Новых мониторов не найдено.\n\nПодключите монитор и включите его в Windows (Win+P → «Расширить»), затем нажмите «+» ещё раз.\n\nОткрыть настройки экрана Windows?",
            "(monitor {0})", "(монитор {0})",
            "Gamma{0}: Windows rejected such strong values — Settings → 'Remove Windows gamma limit'", "Гамма{0}: Windows не принял такие сильные значения — Настройки → «Снять ограничение гаммы»",
            "NVIDIA{0}: monitor is not on an NVIDIA GPU or the driver denied access", "NVIDIA{0}: монитор не на видеокарте NVIDIA или драйвер не дал доступ",
            "Gamma: OK", "Гамма: OK",
            "NVIDIA API not found", "NVIDIA API не найден",
            "Profile: ", "Профиль: ",
            " (modified)", " (изменён)",
            "Profiles: ", "Профили: ",
            "Game: ", "Игра: ",
            // горячие клавиши
            "Click a field and press a key combination. Backspace / Delete clears it.\n", "Кликните по полю и нажмите сочетание. Backspace / Delete — очистить.\n",
            "Hotkeys work globally — in games, in OBS, and while the app is minimized to the tray. Stream Deck buttons work too (the 'Hotkey' action).",
                "Клавиши работают глобально — в игре, в OBS, когда программа свёрнута в трей. Подходят и кнопки Stream Deck (действие «Горячая клавиша»).",
            "General actions", "Общие действия",
            "Reset to default", "Сбросить к стандарту",
            "Next profile", "Следующий профиль",
            "Previous profile", "Предыдущий профиль",
            "Toggle: profile ⇄ default", "Переключить: профиль ⇄ стандарт",
            "Show / hide the app window", "Показать / скрыть окно программы",
            "monitor {0}", "монитор {0}",
            "(not connected)", "(не подключён)",
            "Command line (Stream Deck 'Open', shortcuts, OBS scripts)", "Командная строка (Stream Deck «Открыть», ярлыки, скрипты OBS)",
            "on the monitor selected in the monitor strip", "на мониторе, выбранном в полосе мониторов",
            "on DISPLAY2 (--monitor all — on all)", "на DISPLAY2 (--monitor all — на всех)",
            "also accept --monitor N", "тоже понимают --monitor N",
            "If the app is already running, the command is passed to it instantly (a second copy does not open).", "Если программа уже запущена, команда мгновенно передаётся в неё (вторая копия не открывается).",
            "⚠ taken by another program or invalid", "⚠ занято другой программой или недопустимо",
            "— not set —", "— не задано —",
            "add Ctrl / Alt / Shift", "добавьте Ctrl / Alt / Shift",
            // игры
            "Auto-switch profile by game", "Автопереключение профиля по игре",
            "Switch profile when the game window is active", "Включать профиль, когда окно игры активно",
            "Restore the previous profile when you leave the game (Alt+Tab to OBS, browser, Discord)", "Возвращать прежний профиль, когда уходишь из игры (Alt+Tab в OBS, браузер, Discord)",
            "Active window process:  {0}", "Сейчас активно окно процесса:  {0}",
            "Browse…", "Обзор…",
            "Programs (*.exe)|*.exe", "Программы (*.exe)|*.exe",
            "Add", "Добавить",
            "Game (process)", "Игра (процесс)",
            "Profile", "Профиль",
            "Delete selected", "Удалить выбранное",
            "Works best in windowed / borderless mode.\nIf a fullscreen game resets the gamma, enable 'Keep settings applied'.", "Лучше всего работает в оконном / безрамочном режиме.\nЕсли игра в полноэкранном режиме сбрасывает гамму — включите «Удерживать настройки».",
            "Choose a game process and a profile.", "Выберите процесс игры и профиль.",
            "{0}  (profile deleted)", "{0}  (профиль удалён)",
            // настройки
            "Language", "Язык",
            "Startup", "Запуск",
            "Start with Windows", "Запускать вместе с Windows",
            "Start minimized to tray", "Запускать свёрнутой в трей",
            "Apply the last profile on startup", "Применять последний профиль при запуске",
            "Close button minimizes to tray (hotkeys keep working)", "Крестик сворачивает в трей (горячие клавиши продолжают работать)",
            "Behavior", "Поведение",
            "Show an on-screen notification when the profile changes", "Показывать уведомление на экране при смене профиля",
            "Smooth transition between profiles", "Плавный переход между профилями",
            "Keep settings applied (re-apply if a game resets the gamma)", "Удерживать настройки (заново применять, если игра сбросила гамму)",
            "Reset color to default when the app exits", "Сбрасывать цвет к стандарту при выходе из программы",
            "Hide the app window and notifications from screen capture (OBS, Discord)", "Скрывать окно программы и уведомления от захвата экрана (OBS, Discord)",
            "Export profiles…", "Экспорт профилей…",
            "Import profiles…", "Импорт профилей…",
            "Open settings folder", "Открыть папку настроек",
            "Windows", "Windows",
            "Remove Windows gamma limit…", "Снять ограничение гаммы Windows…",
            "By default Windows refuses very strong gamma/brightness. This button writes GdiIcmGammaRange = 256 to the registry\n", "Windows по умолчанию не даёт ставить слишком сильную гамму/яркость. Кнопка записывает в реестр GdiIcmGammaRange = 256\n",
            "(requires administrator rights, takes effect after a restart). Needed only if the status bar says the gamma was not accepted.", "(нужны права администратора, вступает в силу после перезагрузки). Нужна, только если в строке состояния пишет, что гамма не принята.",
            "Support", "Поддержка",
            "How it works", "Как это работает",
            "• Brightness, contrast and gamma use the GPU gamma ramp (like 'Adjust desktop color settings' in the NVIDIA control panel).\n", "• Яркость, контрастность и гамма — через гамма-кривую видеокарты (как «Настройка цвета рабочего стола» в панели NVIDIA).\n",
            "• Digital vibrance and hue go straight through the NVIDIA driver (NVAPI).\n", "• Цифровая интенсивность и оттенок — напрямую через драйвер NVIDIA (NVAPI).\n",
            "• All of it is applied at the GPU output, so stream viewers and recordings see the normal picture — only your screen changes.\n", "• Всё это применяется на выходе видеокарты, поэтому зрители стрима и запись видят обычную картинку — меняется только у вас.\n",
            "• Settings and profiles are stored in ", "• Настройки и профили хранятся в ",
            // главное окно: сообщения, трей, диалоги
            "Monitor {0} is not connected", "Монитор {0} не подключён",
            "all monitors", "все мониторы",
            "Monitor not found: {0}", "Монитор не найден: {0}",
            "Profile not found: {0}", "Профиль не найден: {0}",
            "Reset all monitors to default", "Сбросить все мониторы к стандарту",
            "Open GammaDeck", "Открыть GammaDeck",
            "Exit", "Выход",
            "GammaDeck is running in the tray", "GammaDeck работает в трее",
            "Hotkeys are active. To exit, right-click the tray icon.", "Горячие клавиши активны. Выход — правой кнопкой по значку.",
            "New profile", "Новый профиль",
            "Profile name (current settings will be saved to it):", "Название профиля (текущие настройки сохранятся в него):",
            "My profile {0}", "Мой профиль {0}",
            "Rename", "Переименовать",
            "New name:", "Новое название:",
            "{0} copy", "{0} копия",
            "Delete profile '{0}'?", "Удалить профиль «{0}»?",
            "All built-in profiles are already present.", "Все встроенные профили уже есть.",
            "Built-in profiles added: {0}", "Добавлено встроенных профилей: {0}",
            "GammaDeck profiles (*.json)|*.json|All files|*.*", "Профили GammaDeck (*.json)|*.json|Все файлы|*.*",
            "GammaDeck profiles (*.json)|*.json", "Профили GammaDeck (*.json)|*.json",
            "GammaDeck-profiles.json", "GammaDeck-профили.json",
            "Could not save: {0}", "Не удалось сохранить: {0}",
            "Import", "Импорт",
            "Profiles imported: {0}", "Импортировано профилей: {0}",
            "Could not read the file: {0}", "Не удалось прочитать файл: {0}",
            "Could not change autostart: {0}", "Не удалось изменить автозапуск: {0}",
            "Cancel", "Отмена",
            // Program.cs
            "An error occurred; colors were reset to default.\n\n", "Произошла ошибка, цвета сброшены к стандарту.\n\n",
            "Gamma limit removed.\nRestart the computer for the change to take effect.", "Ограничение гаммы снято.\nПерезагрузите компьютер, чтобы изменение вступило в силу.",
            "Could not change the registry: {0}", "Не удалось изменить реестр: {0}"
        });

        private static Dictionary<string, string> Build(string[] pairs)
        {
            var d = new Dictionary<string, string>(pairs.Length / 2);
            for (int i = 0; i + 1 < pairs.Length; i += 2) d[pairs[i]] = pairs[i + 1];
            return d;
        }
    }
}
