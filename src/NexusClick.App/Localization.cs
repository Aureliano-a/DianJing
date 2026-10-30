using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using System.Text.RegularExpressions;

namespace NexusClick.App;

public sealed class Localization : INotifyPropertyChanged
{
    public static Localization Instance { get; } = new();
    private int _language;
    private static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DianJing", "language.txt");
    private static bool Persist => Environment.GetEnvironmentVariable("DIANJING_DISABLE_LANGUAGE_PERSISTENCE") != "1";
    private Localization()
    {
        try
        {
            if (Persist && File.Exists(SettingsPath) && int.TryParse(File.ReadAllText(SettingsPath), out var language) && language is >= 0 and <= 4)
                _language = language;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    public int Language
    {
        get => _language;
        set
        {
            if (value is < 0 or > 4 || value == _language) return;
            _language = value;
            if (Persist)
            {
                try { Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!); File.WriteAllText(SettingsPath, value.ToString(CultureInfo.InvariantCulture)); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            PropertyChanged?.Invoke(this, new(nameof(Language)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private readonly Dictionary<string, string[]> _strings = Build();
    public string Translate(string value)
    {
        foreach (var (source, translations) in TechnicalErrors)
            value = value.Replace(source, translations[_language], StringComparison.Ordinal);
        if (_language == 0) return value;
        if (_strings.TryGetValue(value, out var exact)) return exact[_language - 1];
        var macro = Regex.Match(value, @"^(\d+\s+·\s+)(按键|鼠标左键|鼠标右键|鼠标中键|等待)(.*?)(\s+·\s+\d+) 毫秒$");
        if (macro.Success)
        {
            string detail = macro.Groups[3].Value;
            if (!string.IsNullOrWhiteSpace(detail)) detail = " " + Translate(detail.Trim());
            return macro.Groups[1].Value + Translate(macro.Groups[2].Value) + detail + macro.Groups[4].Value + " " + Translate("毫秒");
        }
        var clicks = Regex.Match(value, @"^测试区收到 (.+) 次点击$");
        if (clicks.Success)
            return string.Format(new[] { "Test clicks: {0}", "テストクリック数：{0}", "Clics de prueba: {0}", "Тестовых кликов: {0}" }[_language - 1], clicks.Groups[1].Value);
        var hotkey = Regex.Match(value, @"^全局快捷键：(.+?) 启动 / 暂停；Ctrl \+ Shift \+ F11 紧急停止。F12 仅在本窗口有效。$");
        if (hotkey.Success)
            return string.Format(new[] { "Global: {0} start / pause; Ctrl + Shift + F11 emergency stop. F12 works in this window.", "グローバル：{0} 開始 / 一時停止、Ctrl + Shift + F11 緊急停止。F12 はこのウィンドウ内のみ。", "Global: {0} iniciar / pausar; Ctrl + Shift + F11 parada de emergencia. F12 solo en esta ventana.", "Глобально: {0} старт / пауза; Ctrl + Shift + F11 аварийный стоп. F12 только в окне." }[_language - 1], hotkey.Groups[1].Value);
        foreach (var (pattern, template) in DynamicPatterns)
        {
            var match = Regex.Match(value, pattern);
            if (match.Success)
            {
                var arguments = match.Groups.Cast<Group>().Skip(1).Select(group => (object)group.Value).ToArray();
                if (template == "已设置按键：{0}。") arguments[0] = Translate((string)arguments[0]);
                return string.Format(CultureInfo.CurrentCulture, Translate(template), arguments);
            }
        }
        var units = new[] { " / sec", " / 秒", " / s", " / с" };
        value = value.Replace(" / 秒", units[_language - 1], StringComparison.Ordinal);
        value = value.Replace("   开始 / 暂停", new[] { "   Start / pause", "   開始 / 一時停止", "   Iniciar / pausar", "   Старт / пауза" }[_language - 1], StringComparison.Ordinal);
        // Composite status messages may contain a translated prefix and a translated detail.
        foreach (var pair in _strings.Where(p => p.Key.Length >= 7).OrderByDescending(p => p.Key.Length))
            value = value.Replace(pair.Key, pair.Value[_language - 1], StringComparison.Ordinal);
        return value;
    }
    private static readonly (string Source, string[] Translations)[] TechnicalErrors =
    [
        ("A previous input release remains unresolved.", ["先前的模拟输入尚未释放。", "A previous simulated input has not been released.", "前回の入力がまだ解放されていません。", "Una entrada simulada anterior aún no se ha liberado.", "Предыдущее имитированное нажатие ещё не отпущено."]),
        ("SendInput failed; target may have a higher integrity level.", ["发送输入失败；目标程序可能具有更高权限。", "Input failed; the target may have higher permissions.", "入力に失敗しました。対象の権限が高い可能性があります。", "Falló la entrada; el destino puede tener permisos superiores.", "Ошибка ввода; целевое приложение может иметь более высокие права."]),
        ("Point is not on an attached monitor.", ["坐标不在已连接的显示器上。", "The position is not on an attached monitor.", "座標が接続中のモニター上にありません。", "La posición no está en un monitor conectado.", "Позиция не находится на подключённом мониторе."]),
        ("SendInput could not move the pointer.", ["无法移动鼠标指针。", "Could not move the pointer.", "ポインターを移動できませんでした。", "No se pudo mover el cursor.", "Не удалось переместить указатель."]),
        ("Unable to register the emergency hotkey.", ["无法注册紧急停止快捷键。", "Could not register the emergency hotkey.", "緊急停止キーを登録できませんでした。", "No se pudo registrar la tecla de emergencia.", "Не удалось зарегистрировать аварийную клавишу."]),
        ("Unable to register the start/pause hotkey.", ["无法注册开始或暂停快捷键。", "Could not register the start/pause hotkey.", "開始／一時停止キーを登録できませんでした。", "No se pudo registrar la tecla de inicio/pausa.", "Не удалось зарегистрировать клавишу запуска/паузы."]),
        ("Stop the active task first.", ["请先停止正在运行的任务。", "Stop the active task first.", "先に実行中のタスクを停止してください。", "Detén primero la tarea activa.", "Сначала остановите активную задачу."]),
        ("Another task is active or stopping.", ["另一任务正在运行或停止。", "Another task is active or stopping.", "別のタスクが実行中または停止中です。", "Hay otra tarea activa o deteniéndose.", "Другая задача выполняется или останавливается."]),
        ("An input task is already active.", ["输入任务已经在运行。", "An input task is already running.", "入力タスクは既に実行中です。", "Ya hay una tarea de entrada activa.", "Задача ввода уже выполняется."]),
        ("A macro is already running.", ["宏已经在运行。", "A macro is already running.", "マクロは既に実行中です。", "Ya hay una macro en ejecución.", "Макрос уже выполняется."]),
        ("Wait for the task to finish before retrying release.", ["请等待任务结束后再重试释放。", "Wait for the task to finish before retrying release.", "タスクの終了後に解放を再試行してください。", "Espera a que termine la tarea antes de reintentar la liberación.", "Дождитесь завершения задачи перед повторным отпусканием."]),
        ("Wait for the macro to finish before retrying release.", ["请等待宏结束后再重试释放。", "Wait for the macro to finish before retrying release.", "マクロの終了後に解放を再試行してください。", "Espera a que termine la macro antes de reintentar la liberación.", "Дождитесь завершения макроса перед повторным отпусканием."]),
        ("Release does not match the pending input.", ["释放操作与待处理输入不匹配。", "The release does not match the pending input.", "解放操作が保留中の入力と一致しません。", "La liberación no coincide con la entrada pendiente.", "Отпускание не соответствует ожидающему вводу."]),
        ("Could not set DPI context.", ["无法设置 DPI 上下文。", "Could not set the DPI context.", "DPI コンテキストを設定できません。", "No se pudo configurar el contexto de DPI.", "Не удалось установить контекст DPI."]),
        ("Hotkey message loop failed.", ["快捷键消息循环失败。", "The hotkey message loop failed.", "ホットキーのメッセージループに失敗しました。", "Falló el bucle de mensajes de teclas.", "Ошибка цикла сообщений горячих клавиш."]),
        ("Could not update the hotkey.", ["无法更新快捷键。", "Could not update the hotkey.", "ホットキーを更新できません。", "No se pudo actualizar la tecla.", "Не удалось изменить горячую клавишу."]),
        ("Could not stop the hotkey message loop.", ["无法停止快捷键消息循环。", "Could not stop the hotkey message loop.", "ホットキーのメッセージループを停止できません。", "No se pudo detener el bucle de mensajes de teclas.", "Не удалось остановить цикл сообщений горячих клавиш."])
    ];
    private static readonly (string Pattern, string Template)[] DynamicPatterns =
    [
        (@"^第 (\d+) 轮 · 步骤 (\d+)$", "第 {0} 轮 · 步骤 {1}"),
        (@"^请将鼠标移到目标位置，(\d+) 秒后拾取；再次点击可取消。$", "请将鼠标移到目标位置，{0} 秒后拾取；再次点击可取消。"),
        (@"^已拾取屏幕物理坐标：X (-?\d+)，Y (-?\d+)。请确认位置后再启动。$", "已拾取屏幕物理坐标：X {0}，Y {1}。请确认位置后再启动。"),
        (@"^启动快捷键已设置为 (F\d+)。$", "启动快捷键已设置为 {0}。"),
        (@"^按住 (F\d+) 运行；松开后停止。$", "按住 {0} 运行；松开后停止。"),
        (@"^正在录制本窗口内的顺序单键 · 已记录 (\d+) 步。$", "正在录制本窗口内的顺序单键 · 已记录 {0} 步。"),
        (@"^已设置按键：(.+)。$", "已设置按键：{0}。")
    ];
    private static Dictionary<string, string[]> Build()
    {
        const string rows = """
基础设置|Basic|基本設定|Básico|Основное
语言 / LANGUAGE|LANGUAGE|言語|IDIOMA|ЯЗЫК
SEQUENCE|SEQUENCE|シーケンス|SECUENCIA|ПОСЛЕДОВАТЕЛЬНОСТЬ
✦  DEEP SPACE|✦  DEEP SPACE|✦  深宇宙|✦  ESPACIO PROFUNDO|✦  КОСМОС
快捷操作|Shortcuts|ショートカット|Atajos|Клавиши
运行限制|Limits|実行制限|Límites|Ограничения
点击位置|Position|クリック位置|Posición|Позиция
步骤编辑|Steps|ステップ編集|Pasos|Шаги
运行与文件|Run & files|実行とファイル|Ejecución y archivos|Запуск и файлы
连点参数|Click settings|連打設定|Ajustes de clic|Настройки кликов
运行中修改的参数将在下一次启动时生效。|Changes apply the next time you start.|変更は次回の開始時に適用されます。|Los cambios se aplican al iniciar de nuevo.|Изменения применятся при следующем запуске.
触发方式（快捷键）|Hotkey trigger|ホットキーの動作|Activación por tecla|Режим горячей клавиши
启动快捷键（F1–F10）|Start hotkey (F1–F10)|開始キー（F1–F10）|Tecla de inicio (F1–F10)|Клавиша запуска (F1–F10)
按住触发仅适用于连点；松开快捷键后停止。|Hold mode is for clicking only; release the hotkey to stop.|長押しは連打のみ。キーを離すと停止します。|Mantener solo funciona para clics; suelta la tecla para detener.|Удержание работает для кликов; отпустите клавишу для остановки.
输入类型|Input type|入力種類|Tipo de entrada|Тип ввода
鼠标按钮（鼠标模式）|Mouse button|マウスボタン|Botón del ratón|Кнопка мыши
连按按键（键盘模式）|Key to repeat|連打するキー|Tecla a repetir|Повторяемая клавиша
频率（次 / 秒）|Rate (per second)|頻度（回／秒）|Frecuencia (por segundo)|Частота (в секунду)
保持时间（毫秒）|Hold time (ms)|押下時間（ミリ秒）|Pulsación (ms)|Удержание (мс)
随机间隔（0–50%）|Random interval (0–50%)|間隔の変動（0–50%）|Intervalo aleatorio (0–50%)|Случайный интервал (0–50%)
0 为固定周期；最短周期限制为 20 毫秒。|0 is fixed; the minimum interval is 20 ms.|0 は固定。最短間隔は 20 ミリ秒です。|0 es fijo; el intervalo mínimo es de 20 ms.|0 — фиксированный; минимум 20 мс.
爆发次数（0 关闭）|Burst count (0 disables)|バースト回数（0 で無効）|Clics por ráfaga (0 desactiva)|Размер серии (0 — выкл.)
每轮休息（毫秒）|Rest per burst (ms)|バースト間の休止（ミリ秒）|Pausa entre ráfagas (ms)|Пауза между сериями (мс)
每完成一轮后休息；暂停期间也会经过休息时间。|Rest after each burst; rest time continues while paused.|各バースト後に休止します。一時停止中も休止時間は経過します。|Pausa tras cada ráfaga; el tiempo sigue durante la pausa.|Пауза после серии отсчитывается и во время приостановки.
执行次数（0 表示无限）|Count (0 = unlimited)|実行回数（0 は無制限）|Repeticiones (0 = ilimitadas)|Повторы (0 — без ограничений)
最长时间（秒，0 为无限）|Max time in seconds (0 = unlimited)|最長時間（秒、0 は無制限）|Tiempo máximo en segundos (0 = ilimitado)|Макс. время в секундах (0 — без ограничений)
最长时间包含暂停；任一限制达到后自动停止。|Max time includes pauses; either limit stops the task.|最長時間は一時停止を含み、いずれかの制限で停止します。|El tiempo incluye pausas; cualquier límite detiene la tarea.|Макс. время включает паузы; при любом лимите задача остановится.
固定屏幕坐标（物理像素）|Fixed screen position (physical pixels)|画面位置を固定（物理ピクセル）|Posición fija (píxeles físicos)|Фиксированная позиция (физические пиксели)
步骤编排|Sequence editor|ステップ編集|Editor de secuencia|Редактор шагов
录制按键会追加到当前步骤；导入不会自动执行。|Recording appends steps; importing does not run them.|記録はステップに追加され、読み込み時に自動実行されません。|La grabación añade pasos; importar no los ejecuta.|Запись добавляет шаги; импорт не запускает их.
宏名称|Macro name|マクロ名|Nombre de macro|Имя макроса
按键步骤使用的目标键|Key for key steps|キーステップの対象キー|Tecla para pasos de teclado|Клавиша для шага
步骤类型|Step type|ステップの種類|Tipo de paso|Тип шага
步骤时长（毫秒）|Step duration (ms)|ステップ時間（ミリ秒）|Duración del paso (ms)|Длительность шага (мс)
＋ 添加|＋ Add|＋ 追加|＋ Añadir|＋ Добавить
替换|Replace|置換|Sustituir|Заменить
复制|Duplicate|複製|Duplicar|Дублировать
删除|Delete|削除|Eliminar|Удалить
执行轮数|Cycles|実行回数|Ciclos|Циклы
最长时间（秒，含暂停）|Max seconds (includes pauses)|最長時間（秒、一時停止を含む）|Máximo en segundos (incluye pausas)|Макс. секунды (включая паузы)
新建|New|新規|Nuevo|Создать
保存 JSON|Save JSON|JSON を保存|Guardar JSON|Сохранить JSON
加载 JSON|Load JSON|JSON を読み込む|Cargar JSON|Загрузить JSON
▶  开始 / 继续|▶  Start / resume|▶  開始 / 再開|▶  Iniciar / continuar|▶  Начать / продолжить
▶  运行 / 继续宏|▶  Run / resume macro|▶  マクロ実行 / 再開|▶  Ejecutar / continuar macro|▶  Запустить / продолжить макрос
Ⅱ  暂停|Ⅱ  Pause|Ⅱ  一時停止|Ⅱ  Pausar|Ⅱ  Пауза
■  停止|■  Stop|■  停止|■  Detener|■  Стоп
重试释放|Retry release|解放を再試行|Reintentar liberación|Повторить отпускание
运行监控|Live monitor|実行モニター|Monitor en vivo|Монитор
LIVE  /  当前或上次任务|LIVE  /  Current or last task|LIVE  /  現在または前回|LIVE  /  Tarea actual o anterior|LIVE  /  Текущая или последняя задача
已完成操作（宏为步骤数）|Completed (macro: steps)|完了数（マクロはステップ数）|Completados (macro: pasos)|Выполнено (для макроса — шаги)
宏运行位置|Macro progress|マクロの進捗|Progreso de macro|Ход макроса
活动时长|Active time|稼働時間|Tiempo activo|Активное время
实际平均频率|Actual average rate|実際の平均頻度|Frecuencia media real|Средняя частота
近 2 秒实际频率|Rate over last 2 seconds|直近 2 秒の頻度|Frecuencia en los últimos 2 s|Частота за 2 секунды
点击测试区|Click test|クリックテスト|Prueba de clic|Проверка кликов
此处统计测试按钮实际收到的点击。|Counts clicks actually received by the test button.|テストボタンが受け取ったクリックを数えます。|Cuenta los clics recibidos por el botón.|Считает клики, полученные кнопкой.
清零测试|Reset test|テストをリセット|Reiniciar prueba|Сбросить счётчик
速度受 Windows 调度及目标程序影响。|Speed depends on Windows scheduling and the target app.|速度は Windows と対象アプリの影響を受けます。|La velocidad depende de Windows y la aplicación de destino.|Скорость зависит от Windows и целевого приложения.
动态星空与地球|Animate stars and Earth|星空と地球を動かす|Animar estrellas y Tierra|Анимация звёзд и Земли
关闭后停止星空、地球和按钮脉冲动画|Turn off star, Earth and button pulse animations|星、地球、ボタンのアニメーションを停止します|Desactiva las animaciones de estrellas, Tierra y botones|Отключает анимацию звёзд, Земли и кнопок
WORKSPACE  /  工作区|WORKSPACE|ワークスペース|ESPACIO DE TRABAJO|РАБОЧАЯ ОБЛАСТЬ
◉   连点引擎|◉   Click engine|◉   連打エンジン|◉   Motor de clics|◉   Автокликер
⌘   宏引擎|⌘   Macro engine|⌘   マクロエンジン|⌘   Motor de macros|⌘   Макросы
LOCAL  /  本地运行|LOCAL  /  Offline|LOCAL  /  ローカル|LOCAL  /  Sin conexión|LOCAL  /  Локально
基础功能版本|Preview version|プレビュー版|Versión preliminar|Предварительная версия
快捷控制|QUICK CONTROL|クイック操作|CONTROL RÁPIDO|БЫСТРОЕ УПРАВЛЕНИЕ
全局紧急停止|Global emergency stop|グローバル緊急停止|Parada de emergencia global|Глобальная аварийная остановка
F12   窗口内停止|F12   Stop in window|F12   ウィンドウ内で停止|F12   Detener en ventana|F12   Стоп в окне
点境|DianJing|DianJing|DianJing|DianJing
点境  /  DEEP SPACE CONTROL|DIANJING  /  DEEP SPACE CONTROL|DIANJING  /  DEEP SPACE CONTROL|DIANJING  /  DEEP SPACE CONTROL|DIANJING  /  DEEP SPACE CONTROL
连点引擎|Click engine|連打エンジン|Motor de clics|Автокликер
宏引擎|Macro engine|マクロエンジン|Motor de macros|Макросы
设定鼠标或键盘操作，实时掌控每一次执行。|Configure mouse or keyboard actions and monitor execution.|マウスやキー操作を設定し、実行状況を確認します。|Configura acciones de ratón o teclado y supervisa su ejecución.|Настройте действия мыши или клавиатуры и следите за выполнением.
编排顺序操作，让重复任务有序运行。|Arrange sequential steps for repeatable tasks.|順番に実行するステップを編集します。|Organiza pasos secuenciales para tareas repetitivas.|Настройте последовательность шагов для повторяющихся задач.
鼠标|Mouse|マウス|Ratón|Мышь
键盘|Keyboard|キーボード|Teclado|Клавиатура
左键|Left|左ボタン|Izquierdo|Левая
右键|Right|右ボタン|Derecho|Правая
中键|Middle|中ボタン|Central|Средняя
切换触发|Toggle|切り替え|Alternar|Переключение
按住触发|Hold|長押し|Mantener|Удержание
按键|Key|キー|Tecla|Клавиша
鼠标左键|Mouse left|マウス左|Ratón izquierdo|Левая кнопка
鼠标右键|Mouse right|マウス右|Ratón derecho|Правая кнопка
鼠标中键|Mouse middle|マウス中|Ratón central|Средняя кнопка
等待|Wait|待機|Espera|Ожидание
设置快捷键|Set hotkey|ホットキー設定|Configurar tecla|Назначить клавишу
设置按键|Set key|キー設定|Configurar tecla|Назначить клавишу
取消设置|Cancel|キャンセル|Cancelar|Отмена
拾取位置|Pick position|位置を取得|Elegir posición|Выбрать позицию
取消拾取|Cancel pick|取得をキャンセル|Cancelar selección|Отменить выбор
●  录制按键|●  Record keys|●  キーを記録|●  Grabar teclas|●  Запись клавиш
■  停止录制|■  Stop recording|■  記録を停止|■  Detener grabación|■  Остановить запись
仅本窗口录制，切换窗口自动停止|Records only in this window; stops on focus loss|このウィンドウのみ記録し、切替時に停止|Graba solo en esta ventana; se detiene al cambiar|Запись только в этом окне; остановка при потере фокуса
●  正在录制 · 仅本窗口|●  Recording · this window only|●  記録中 · このウィンドウのみ|●  Grabando · solo esta ventana|●  Запись · только в этом окне
待机|Idle|待機中|En espera|Ожидание
已到时|Time limit reached|時間切れ|Tiempo agotado|Время истекло
运行中|Running|実行中|En ejecución|Выполняется
暂停中|Pausing|一時停止中|Pausando|Приостановка
已暂停|Paused|一時停止|En pausa|Пауза
停止中|Stopping|停止中|Deteniendo|Остановка
故障|Error|エラー|Error|Ошибка
快捷键已被占用，原快捷键仍然有效。|The hotkey is in use; the previous hotkey remains active.|キーは使用中です。以前のキーは引き続き有効です。|La tecla está en uso; la anterior sigue activa.|Клавиша занята; прежняя клавиша остаётся активной.
无法释放旧快捷键。|Could not release the previous hotkey.|以前のホットキーを解放できません。|No se pudo liberar la tecla anterior.|Не удалось освободить прежнюю клавишу.
快捷键服务已停止。|The hotkey service has stopped.|ホットキーサービスは停止しました。|El servicio de teclas se ha detenido.|Служба горячих клавиш остановлена.
点境 · 键鼠自动化|DianJing · Input automation|DianJing · 入力自動化|DianJing · Automatización|DianJing · Автоматизация ввода
毫秒|ms|ミリ秒|ms|мс
空格|Space|スペース|Espacio|Пробел
方向上|Up|上矢印|Arriba|Стрелка вверх
方向下|Down|下矢印|Abajo|Стрелка вниз
方向左|Left arrow|左矢印|Flecha izquierda|Стрелка влево
方向右|Right arrow|右矢印|Flecha derecha|Стрелка вправо
宏文件|Macro file|マクロファイル|Archivo de macro|Файл макроса
未命名宏|Untitled macro|無題のマクロ|Macro sin título|Макрос без названия
关闭点境|Close DianJing|DianJing を閉じる|Cerrar DianJing|Закрыть DianJing
停止或释放输入时发生错误：|An error occurred while stopping or releasing input: |入力の停止または解放中にエラーが発生しました：|Error al detener o liberar la entrada: |Ошибка остановки или отпускания ввода: 
请检查键盘和鼠标状态。|Check the keyboard and mouse state.|キーボードとマウスの状態を確認してください。|Comprueba el estado del teclado y el ratón.|Проверьте состояние клавиатуры и мыши.
全局快捷键注册失败；已禁止启动：|Global hotkey registration failed; starting is disabled: |ホットキーの登録に失敗したため開始できません：|Falló el registro de teclas globales; el inicio está desactivado: |Не удалось зарегистрировать глобальные клавиши; запуск запрещён: 
已设置按键：{0}。|Key set to {0}.|キーを {0} に設定しました。|Tecla configurada: {0}.|Назначена клавиша: {0}.
无法识别此按键，请切换到直接键盘输入后重试。|Key not recognized; switch to direct keyboard input and retry.|キーを認識できません。直接入力に切り替えて再試行してください。|Tecla no reconocida; cambia a entrada directa y vuelve a intentarlo.|Клавиша не распознана; включите прямой ввод и повторите.
第 {0} 轮 · 步骤 {1}|Cycle {0} · Step {1}|{0} 周目 · ステップ {1}|Ciclo {0} · Paso {1}|Цикл {0} · Шаг {1}
启动快捷键不能与当前宏步骤中的按键相同。|The start hotkey cannot match a key used in the macro.|開始キーはマクロのキーと重複できません。|La tecla de inicio no puede coincidir con una tecla de la macro.|Клавиша запуска не может совпадать с клавишей макроса.
启动快捷键不能与当前连按按键相同。|The start hotkey cannot match the repeated key.|開始キーは連打キーと重複できません。|La tecla de inicio no puede coincidir con la tecla repetida.|Клавиша запуска не может совпадать с повторяемой клавишей.
启动快捷键已设置为 {0}。|Start hotkey set to {0}.|開始キーを {0} に設定しました。|Tecla de inicio configurada: {0}.|Клавиша запуска: {0}.
按住 {0} 运行；松开后停止。|Hold {0} to run; release it to stop.|{0} を押して実行し、離すと停止します。|Mantén {0} para ejecutar; suéltala para detener.|Удерживайте {0} для запуска; отпустите для остановки.
按住触发仅适用于连点引擎。|Hold mode is available only for the click engine.|長押しは連打エンジンのみで利用できます。|El modo mantener solo está disponible para clics.|Режим удержания доступен только для автокликера.
按住触发异常，任务已请求停止：|Hold trigger failed; the task was asked to stop: |長押しでエラーが発生し、停止を要求しました：|Falló la activación por pulsación; se solicitó detener la tarea: |Ошибка удержания; отправлен запрос остановки: 
紧急停止：录制已结束。|Emergency stop: recording ended.|緊急停止：記録を終了しました。|Parada de emergencia: grabación finalizada.|Аварийная остановка: запись завершена.
请先停止任务，并结束录制、按键设置和位置拾取。|Stop the task and finish recording, key setup and position selection.|停止し、記録、キー設定、位置取得を終了してください。|Detén la tarea y finaliza la grabación y la selección de tecla y posición.|Остановите задачу и завершите запись, настройку клавиш и выбор позиции.
请在本窗口按 F1–F10 设置启动快捷键；F11 和 F12 保留给停止功能。|Press F1–F10 in this window to set the start hotkey; F11 and F12 are reserved for stopping.|このウィンドウで F1–F10 を押してください。F11 と F12 は停止用です。|Pulsa F1–F10 aquí para configurar el inicio; F11 y F12 están reservadas para detener.|Нажмите F1–F10 в этом окне; F11 и F12 зарезервированы для остановки.
请选择不带修饰键的 F1–F10。|Choose F1–F10 without modifiers.|修飾キーなしの F1–F10 を選択してください。|Elige F1–F10 sin modificadores.|Выберите F1–F10 без модификаторов.
任务或录制已启动，未替换当前宏。|A task or recording has started; the current macro was not replaced.|タスクまたは記録が始まったため、現在のマクロは変更されません。|Se inició una tarea o grabación; la macro actual no se reemplazó.|Началась задача или запись; текущий макрос не заменён.
保存失败：|Save failed: |保存に失敗：|Error al guardar: |Ошибка сохранения: 
加载失败：|Load failed: |読み込みに失敗：|Error al cargar: |Ошибка загрузки: 
加载失败：宏文件格式或参数无效，当前宏未更改。|Load failed: invalid macro format or parameters; the current macro is unchanged.|読み込み失敗：形式または設定が無効です。現在のマクロは変更されません。|Error de carga: formato o parámetros inválidos; la macro actual no cambió.|Ошибка загрузки: неверный формат или параметры; текущий макрос не изменён.
宏不能使用当前启动快捷键。|The macro cannot use the current start hotkey.|マクロには現在の開始キーを使用できません。|La macro no puede usar la tecla de inicio actual.|Макрос не может использовать текущую клавишу запуска.
宏中不能使用保留键 F8、F11 或 F12。|The macro cannot use reserved keys F8, F11 or F12.|マクロでは予約キー F8、F11、F12 を使用できません。|La macro no puede usar las teclas reservadas F8, F11 o F12.|Макрос не может использовать зарезервированные F8, F11 или F12.
宏名称须为 1–80 个非控制字符。|The macro name must contain 1–80 non-control characters.|マクロ名は制御文字を除く 1–80 文字にしてください。|El nombre debe tener 1–80 caracteres no de control.|Имя макроса должно содержать 1–80 символов, кроме управляющих.
宏含保留键 F8、F11 或 F12。|The macro contains reserved keys F8, F11 or F12.|マクロに予約キー F8、F11、F12 が含まれます。|La macro contiene las teclas reservadas F8, F11 o F12.|Макрос содержит зарезервированные F8, F11 или F12.
宏执行次数和最长时长必须为整数。|Macro cycles and maximum time must be integers.|実行回数と最長時間は整数で指定してください。|Los ciclos y el tiempo máximo deben ser enteros.|Количество циклов и макс. время должны быть целыми.
宏最长时间须为 1–86400 秒。|Maximum macro time must be 1–86400 seconds.|マクロの最長時間は 1–86400 秒です。|El tiempo máximo debe ser de 1–86400 segundos.|Макс. время макроса: 1–86400 секунд.
宏轮数须为 1–10000，且总执行步骤不能超过 100000。|Use 1–10000 cycles and at most 100000 total steps.|実行回数は 1–10000、総ステップ数は 100000 以下です。|Usa 1–10000 ciclos y como máximo 100000 pasos.|Допустимо 1–10000 циклов и до 100000 шагов.
按键或鼠标保持时间须为 1–5000 毫秒。|Key or mouse hold time must be 1–5000 ms.|キーまたはマウスの押下時間は 1–5000 ミリ秒です。|La pulsación debe durar 1–5000 ms.|Удержание клавиши или кнопки: 1–5000 мс.
无法编辑宏：请选择有效步骤，并检查是否已到列表边界。|Cannot edit: select a valid step and check the list boundary.|編集できません。有効なステップとリストの端を確認してください。|No se puede editar: selecciona un paso válido y comprueba los límites.|Не удалось изменить: выберите шаг и проверьте границы списка.
步骤时长请输入整数毫秒。|Enter the step duration as integer milliseconds.|ステップ時間を整数のミリ秒で入力してください。|Introduce la duración en milisegundos enteros.|Введите длительность шага целым числом миллисекунд.
等待时间须为 1–60000 毫秒。|Wait time must be 1–60000 ms.|待機時間は 1–60000 ミリ秒です。|La espera debe ser de 1–60000 ms.|Время ожидания: 1–60000 мс.
请先停止任务或录制，再加载宏。|Stop the task or recording before loading a macro.|読み込み前にタスクまたは記録を停止してください。|Detén la tarea o grabación antes de cargar una macro.|Остановите задачу или запись перед загрузкой макроса.
请先停止录制再保存。|Stop recording before saving.|保存前に記録を停止してください。|Detén la grabación antes de guardar.|Остановите запись перед сохранением.
请选择步骤类型。|Choose a step type.|ステップの種類を選択してください。|Selecciona un tipo de paso.|Выберите тип шага.
请先停止任务并结束按键设置或录制。|Stop the task and finish key setup or recording first.|停止してキー設定または記録を終了してください。|Detén la tarea y termina la selección de tecla o la grabación.|Остановите задачу и завершите настройку клавиши или запись.
请将鼠标移到目标位置，{0} 秒后拾取；再次点击可取消。|Move the pointer to the target; picking in {0} seconds. Click again to cancel.|ポインターを対象に移動してください。{0} 秒後に取得します。再クリックでキャンセル。|Mueve el cursor al destino; se seleccionará en {0} segundos. Pulsa de nuevo para cancelar.|Наведите курсор на цель; выбор через {0} с. Нажмите ещё раз для отмены.
已拾取屏幕物理坐标：X {0}，Y {1}。请确认位置后再启动。|Selected physical screen coordinates: X {0}, Y {1}. Check them before starting.|物理座標 X {0}、Y {1} を取得しました。開始前に確認してください。|Coordenadas físicas: X {0}, Y {1}. Compruébalas antes de iniciar.|Физические координаты: X {0}, Y {1}. Проверьте их перед запуском.
宏已达到 1000 步上限。|The macro has reached the 1000-step limit.|マクロは 1000 ステップの上限に達しました。|La macro alcanzó el límite de 1000 pasos.|Достигнут предел в 1000 шагов макроса.
宏已达到 1000 步上限，未保存此按键。|The macro reached 1000 steps; this key was not saved.|1000 ステップの上限に達したため、このキーは保存されません。|La macro alcanzó 1000 pasos; esta tecla no se guardó.|Достигнут предел в 1000 шагов; клавиша не сохранена.
录制已停止；已完成的步骤保留在列表中。|Recording stopped; completed steps remain in the list.|記録を停止しました。完了したステップは残ります。|Grabación detenida; los pasos completados permanecen en la lista.|Запись остановлена; завершённые шаги остались в списке.
录制达到 60 秒上限；未释放的按键未保存。|Recording reached 60 seconds; unreleased keys were not saved.|記録が 60 秒に達しました。離されていないキーは保存されません。|La grabación llegó a 60 segundos; las teclas no liberadas no se guardaron.|Достигнут предел записи в 60 секунд; неотпущенные клавиши не сохранены.
按键或等待时间超过支持范围，未保存此按键。|The key hold or wait exceeded the supported range; this key was not saved.|押下または待機時間が範囲外のため、このキーは保存されません。|La pulsación o espera excedió el límite; esta tecla no se guardó.|Удержание или ожидание превысило предел; клавиша не сохранена.
无法识别此按键，未录入。|Key not recognized; it was not recorded.|キーを認識できず、記録されませんでした。|Tecla no reconocida; no se grabó.|Клавиша не распознана и не записана.
检测到按键重叠；录制已停止，未完成按键未保存。|Overlapping keys detected; recording stopped and the unfinished key was not saved.|キーの重なりを検出したため停止しました。未完了のキーは保存されません。|Se detectaron teclas solapadas; se detuvo la grabación y no se guardó la tecla incompleta.|Обнаружены перекрывающиеся нажатия; запись остановлена, незавершённая клавиша не сохранена.
检测到组合键：当前只录制不含修饰键的单键。|Combination detected: only single keys without modifiers are recorded.|組み合わせを検出しました。修飾キーを含まない単一キーのみ記録します。|Se detectó una combinación: solo se graban teclas individuales sin modificadores.|Обнаружено сочетание: записываются только отдельные клавиши без модификаторов.
检测到组合键；录制已停止，未完成按键未保存。|Combination detected; recording stopped and the unfinished key was not saved.|組み合わせを検出し停止しました。未完了のキーは保存されません。|Se detectó una combinación; la grabación se detuvo y la tecla incompleta no se guardó.|Обнаружено сочетание; запись остановлена, незавершённая клавиша не сохранена.
正在录制本窗口内的顺序单键 · 已记录 {0} 步。|Recording single keys in this window · {0} steps recorded.|このウィンドウで単一キーを記録中 · {0} ステップ記録済み。|Grabando teclas individuales en esta ventana · {0} pasos grabados.|Запись отдельных клавиш в этом окне · записано шагов: {0}.
正在录制本窗口内的顺序单键；切换窗口将停止，修饰键和组合键不录制。|Recording single keys in this window; changing windows stops recording. Modifiers and combinations are excluded.|このウィンドウで単一キーを記録中。ウィンドウ切替で停止し、修飾キーや組み合わせは記録しません。|Grabando teclas individuales en esta ventana; cambiar de ventana detiene la grabación. Se excluyen modificadores y combinaciones.|Запись отдельных клавиш в этом окне; смена окна останавливает запись. Модификаторы и сочетания исключены.
请先停止任务或完成按键设置和位置拾取，再开始录制。|Stop the task or finish key and position selection before recording.|記録前に停止するか、キーと位置の設定を完了してください。|Detén la tarea o termina de elegir tecla y posición antes de grabar.|Перед записью остановите задачу или завершите выбор клавиши и позиции.
窗口失去焦点，录制已停止；未完成的按键不会保存。|Window lost focus; recording stopped and unfinished keys were not saved.|フォーカスが外れたため記録を停止しました。未完了のキーは保存されません。|La ventana perdió el foco; se detuvo la grabación y no se guardaron teclas incompletas.|Окно потеряло фокус; запись остановлена, незавершённые нажатия не сохранены.
无法启动：|Cannot start: |開始できません：|No se puede iniciar: |Не удалось запустить: 
停止或释放失败：|Stop or release failed: |停止または解放に失敗：|Error al detener o liberar: |Ошибка остановки или отпускания: 
重试释放失败：|Retry release failed: |解放の再試行に失敗：|Error al reintentar la liberación: |Ошибка повторного отпускания: 
无法编辑宏：|Cannot edit macro: |マクロを編集できません：|No se puede editar la macro: |Не удалось изменить макрос: 
位置拾取失败：|Position selection failed: |位置の取得に失敗：|Error al seleccionar posición: |Ошибка выбора позиции: 
快捷键设置失败：|Hotkey setup failed: |ホットキーの設定に失敗：|Error al configurar la tecla: |Ошибка настройки клавиши: 
任务异常：|Task error: |タスクエラー：|Error de tarea: |Ошибка задачи: 
；请点击重试释放。|; click Retry release.|。「解放を再試行」を押してください。|; pulsa Reintentar liberación.|; нажмите «Повторить отпускание».
就绪。开始前请确认目标窗口和紧急停止快捷键。|Ready. Check the target window and emergency hotkey before starting.|準備完了。開始前に対象ウィンドウと緊急停止キーを確認してください。|Listo. Comprueba la ventana de destino y la tecla de emergencia.|Готово. Проверьте целевое окно и аварийную клавишу перед запуском.
正在注册全局快捷键…|Registering global hotkeys…|グローバルキーを登録中…|Registrando atajos globales…|Регистрация глобальных клавиш…
请先停止任务并松开触发键，再切换触发方式。|Stop the task and release the trigger before changing modes.|停止し、キーを離してから動作を変更してください。|Detén la tarea y suelta la tecla antes de cambiar el modo.|Остановите задачу и отпустите клавишу перед сменой режима.
请先停止当前宏任务。|Stop the current macro first.|先に実行中のマクロを停止してください。|Detén primero la macro actual.|Сначала остановите текущий макрос.
请先停止当前连点任务。|Stop the current click task first.|先に実行中の連打を停止してください。|Detén primero la tarea de clic.|Сначала остановите текущую задачу кликов.
频率请输入数字。|Enter a numeric rate.|頻度に数値を入力してください。|Introduce una frecuencia numérica.|Введите числовую частоту.
频率须为 0.1–50 次每秒。|Rate must be 0.1–50 per second.|頻度は毎秒 0.1–50 回です。|La frecuencia debe ser de 0,1–50 por segundo.|Частота должна быть 0,1–50 в секунду.
保持时间须为 1–5000 毫秒。|Hold time must be 1–5000 ms.|押下時間は 1–5000 ミリ秒です。|La pulsación debe durar 1–5000 ms.|Удержание должно составлять 1–5000 мс.
执行次数不能超过 10000000。|The count cannot exceed 10000000.|実行回数は 10000000 以下にしてください。|El número no puede superar 10000000.|Количество не может превышать 10000000.
保持时间和次数请输入有效整数。|Enter valid integers for hold time and count.|押下時間と回数に整数を入力してください。|Introduce enteros válidos para duración y cantidad.|Введите целые значения времени и количества.
最长时间请输入 0 到 86400 秒的整数。|Enter a maximum time from 0 to 86400 seconds.|最長時間は 0〜86400 秒の整数で入力してください。|Introduce un tiempo máximo entre 0 y 86400 segundos.|Введите макс. время от 0 до 86400 секунд.
随机间隔请输入 0 到 50 的整数百分比。|Enter a random percentage from 0 to 50.|変動率は 0〜50 の整数で入力してください。|Introduce un porcentaje aleatorio de 0 a 50.|Введите случайный процент от 0 до 50.
爆发次数请输入 0–1000，休息时间请输入 1–60000 毫秒。|Enter a burst count of 0–1000 and a rest of 1–60000 ms.|バースト回数は 0–1000、休止は 1–60000 ミリ秒で入力してください。|Introduce una ráfaga de 0–1000 y una pausa de 1–60000 ms.|Введите размер серии 0–1000 и паузу 1–60000 мс.
请选择鼠标按钮。|Select a mouse button.|マウスボタンを選択してください。|Selecciona un botón del ratón.|Выберите кнопку мыши.
目标按键不能与开始快捷键相同。|The target key cannot be the start hotkey.|対象キーと開始キーは別にしてください。|La tecla de destino no puede ser la de inicio.|Целевая клавиша не может совпадать с клавишей запуска.
坐标请输入整数。|Enter integer coordinates.|座標に整数を入力してください。|Introduce coordenadas enteras.|Введите целые координаты.
请先完成或取消按键设置、位置拾取，并停止录制。|Finish or cancel key and position selection and stop recording first.|キーと位置の設定を終了またはキャンセルし、記録を停止してください。|Finaliza o cancela la selección de tecla y posición y detén la grabación.|Завершите выбор клавиши и позиции и остановите запись.
请先松开按住触发的快捷键。|Release the held hotkey first.|押しているキーを離してください。|Suelta primero la tecla mantenida.|Сначала отпустите удерживаемую клавишу.
全局停止快捷键不可用，不能启动任务。|The global stop hotkey is unavailable; the task cannot start.|停止キーが使えないため開始できません。|La tecla global de parada no está disponible; no se puede iniciar.|Глобальная клавиша остановки недоступна; запуск запрещён.
任务已恢复。|Task resumed.|タスクを再開しました。|Tarea reanudada.|Задача продолжена.
任务已在运行；请先暂停或停止。|The task is already running; pause or stop it first.|タスクは実行中です。先に一時停止または停止してください。|La tarea ya está en ejecución; pausa o detén primero.|Задача уже выполняется; сначала приостановите или остановите её.
任务已启动；修改后的参数将在下次启动时生效。|Task started; changes apply on the next start.|開始しました。変更は次回の開始時に適用されます。|Tarea iniciada; los cambios se aplican al iniciar de nuevo.|Задача запущена; изменения применятся при следующем запуске.
已请求暂停；正在保持的输入会先释放。|Pausing; held inputs will be released first.|一時停止中。押下中の入力を先に解放します。|Pausando; primero se liberarán las entradas mantenidas.|Пауза запрошена; удерживаемый ввод будет отпущен.
请先停止录制，再单独设置按键。|Stop recording before setting a key.|記録を停止してからキーを設定してください。|Detén la grabación antes de configurar una tecla.|Остановите запись перед настройкой клавиши.
请先完成快捷键设置。|Finish setting the hotkey first.|先にホットキーを設定してください。|Termina de configurar la tecla de acceso.|Сначала завершите настройку горячей клавиши.
请先完成或取消位置拾取。|Finish or cancel position selection first.|位置の取得を完了またはキャンセルしてください。|Finaliza o cancela la selección de posición.|Завершите или отмените выбор позиции.
请先停止任务，再设置按键。|Stop the task before setting a key.|停止してからキーを設定してください。|Detén la tarea antes de configurar una tecla.|Остановите задачу перед настройкой клавиши.
请在本窗口按下一个键。F8、F11、F12 保留给控制功能；再次点击按钮可取消。|Press a key in this window. F8, F11 and F12 are reserved; click again to cancel.|このウィンドウでキーを押してください。F8、F11、F12 は予約済みです。再クリックでキャンセル。|Pulsa una tecla aquí. F8, F11 y F12 están reservadas; vuelve a pulsar para cancelar.|Нажмите клавишу в этом окне. F8, F11 и F12 зарезервированы; повторное нажатие отменяет выбор.
按键设置已取消。|Key selection cancelled.|キー設定をキャンセルしました。|Selección de tecla cancelada.|Выбор клавиши отменён.
当前启动快捷键以及 F8、F11、F12 保留给控制功能，请选择其他按键。|The start hotkey and F8, F11 and F12 are reserved. Choose another key.|開始キーと F8、F11、F12 は予約済みです。別のキーを選択してください。|La tecla de inicio y F8, F11 y F12 están reservadas. Elige otra.|Клавиша запуска и F8, F11, F12 зарезервированы. Выберите другую.
当前只支持单键，请松开修饰键后重新按下目标键。|Only single keys are supported. Release modifiers and try again.|単一キーのみ対応しています。修飾キーを離して再試行してください。|Solo se admiten teclas individuales. Suelta las teclas modificadoras.|Поддерживаются отдельные клавиши. Отпустите модификаторы и повторите.
无法识别此按键，请选择其他按键。|Key not recognized. Choose another.|キーを認識できません。別のキーを選択してください。|Tecla no reconocida. Elige otra.|Клавиша не распознана. Выберите другую.
已停止。|Stopped.|停止しました。|Detenido.|Остановлено.
录制已停止。|Recording stopped.|記録を停止しました。|Grabación detenida.|Запись остановлена.
已尝试释放待处理的输入。|Attempted to release pending input.|保留中の入力の解放を試みました。|Se intentó liberar la entrada pendiente.|Выполнена попытка отпустить ожидающий ввод.
快捷键服务异常，任务正在停止。|Hotkey service failed; stopping the task.|ホットキーサービスに異常が発生し、停止中です。|Falló el servicio de teclas; deteniendo la tarea.|Ошибка службы горячих клавиш; задача останавливается.
请先停止任务或录制，再编辑宏。|Stop the task or recording before editing the macro.|タスクまたは記録を停止してから編集してください。|Detén la tarea o grabación antes de editar la macro.|Остановите задачу или запись перед редактированием макроса.
宏已更新；导入不会自动执行。|Macro updated; importing does not run it.|マクロを更新しました。読み込み時に自動実行されません。|Macro actualizada; importar no la ejecuta.|Макрос обновлён; импорт не запускает его.
请先添加至少一个宏步骤。|Add at least one macro step first.|マクロにステップを追加してください。|Añade al menos un paso a la macro.|Добавьте хотя бы один шаг макроса.
宏已保存。|Macro saved.|マクロを保存しました。|Macro guardada.|Макрос сохранён.
宏已加载；不会自动执行。|Macro loaded; it will not run automatically.|マクロを読み込みました。自動実行されません。|Macro cargada; no se ejecutará automáticamente.|Макрос загружен; автоматический запуск отключён.
已取消位置拾取。|Position selection cancelled.|位置の取得をキャンセルしました。|Selección de posición cancelada.|Выбор позиции отменён.
请先停止当前任务。|Stop the current task first.|先にタスクを停止してください。|Detén primero la tarea actual.|Сначала остановите текущую задачу.
已松开快捷键，任务已停止。|Hotkey released; task stopped.|キーを離したため停止しました。|Tecla liberada; tarea detenida.|Клавиша отпущена; задача остановлена.
控制快捷键不会录入宏。|Control hotkeys are not recorded.|制御キーは記録されません。|Las teclas de control no se graban.|Управляющие клавиши не записываются.
暂不录制修饰键或组合键。|Modifier keys and combinations are not recorded.|修飾キーや組み合わせは記録できません。|No se graban modificadores ni combinaciones.|Модификаторы и сочетания не записываются.
""";
        var result = new Dictionary<string, string[]>();
        foreach (var line in rows.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.TrimEnd('\r').Split('|');
            if (parts.Length != 5) throw new InvalidOperationException("Invalid translation row.");
            result.Add(parts[0], parts[1..]);
        }
        return result;
    }
}

public sealed class TranslationConverter : IMultiValueConverter
{
    public static TranslationConverter Instance { get; } = new();
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values.Length > 0 && values[0] is string text ? Localization.Instance.Translate(text) : values.FirstOrDefault() ?? "";
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

[MarkupExtensionReturnType(typeof(object))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension(string text) => Text = text;
    public string Text { get; }
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new MultiBinding { Converter = TranslationConverter.Instance };
        binding.Bindings.Add(new Binding { Source = Text });
        binding.Bindings.Add(new Binding(nameof(Localization.Language)) { Source = Localization.Instance });
        return binding.ProvideValue(serviceProvider);
    }
}

[MarkupExtensionReturnType(typeof(object))]
public sealed class LocBindingExtension : MarkupExtension
{
    public LocBindingExtension() { }
    public LocBindingExtension(string path) => Path = path;
    public string Path { get; set; } = ".";
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new MultiBinding { Converter = TranslationConverter.Instance };
        binding.Bindings.Add(new Binding(Path));
        binding.Bindings.Add(new Binding(nameof(Localization.Language)) { Source = Localization.Instance });
        return binding.ProvideValue(serviceProvider);
    }
}
