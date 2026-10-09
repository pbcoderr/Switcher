using System;
using System.Drawing;
using System.Windows.Forms;

namespace Switcher
{
    public sealed class HelpForm : Form
    {
        public HelpForm() : this("Помощь · Switcher", null) { }
        public HelpForm(string title, string text)
        {
            Text = title; Font = new Font("Segoe UI", 10);
            Size = new Size(680, 470); MinimumSize = new Size(660, 380);
            StartPosition = FormStartPosition.CenterParent;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 2 };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            Control body = text == null ? (Control)CreateTabs() : ReadableText(text);
            var close = new Button { Text = "Закрыть", Size = new Size(100, 32), Anchor = AnchorStyles.Right, DialogResult = DialogResult.Cancel };
            layout.Controls.Add(body, 0, 0); var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty }; footer.RowCount = 1; footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116)); footer.Controls.Add(UiLayout.ButtonRow(close), 1, 0); layout.Controls.Add(footer, 0, 1); Controls.Add(layout);
            close.Click += delegate { Close(); };
            CancelButton = close; AppTheme.Apply(this); AppTheme.Primary(close);
        }
        static TextBox ReadableText(string text)
        {
            return new TextBox { Text = text, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Margin = Padding.Empty };
        }
        static string Section(string heading)
        {
            int start = Instructions.IndexOf(heading + "\r\n", StringComparison.Ordinal);
            if (start < 0) return "";
            int end = Instructions.Length;
            foreach (string next in new[] { "БЫСТРЫЙ СТАРТ", "ПЕРЕКЛЮЧЕНИЕ", "МАРШРУТИЗАЦИЯ", "ОСТАНОВКА", "ПРАВИЛА И ИМПОРТ", "НАСТРОЙКИ ZAPRET", "ЗНАЧКИ И ОШИБКИ", "ПРИ ВЫХОДЕ", "УСТАНОВКА", "О ПРОГРАММЕ" })
            {
                int position = Instructions.IndexOf(next + "\r\n", start + heading.Length, StringComparison.Ordinal);
                if (position >= 0 && position < end) end = position;
            }
            return Instructions.Substring(start, end - start).Trim() + "\r\n\r\n";
        }
        static Control CreateTabs()
        {
            var tabs = new TableLayoutPanel { Name = "helpTabs", Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
            tabs.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); tabs.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            string[] titles = { "Начало", "Маршруты", "Списки", "Ошибки", "О программе" };
            string[] content = {
                Section("БЫСТРЫЙ СТАРТ") + Section("УСТАНОВКА") + Section("ПЕРЕКЛЮЧЕНИЕ") + Section("НАСТРОЙКИ ZAPRET"),
                Section("МАРШРУТИЗАЦИЯ") + Section("ОСТАНОВКА") + "ЖУРНАЛ\r\nПеретащи горизонтальную границу над журналом, чтобы увеличить или уменьшить его высоту. Если правила уже работают, кнопка «Перезапустить» запускает новый сеанс.\r\n\r\n" + Section("ПРИ ВЫХОДЕ"),
                Section("ПРАВИЛА И ИМПОРТ") + "ПРИМЕР TXT\r\nexample.ru\r\nexample.org\r\n203.0.113.0/24\r\n\r\nПРИМЕР JSON\r\n{ \"rules\": [ { \"kind\": \"process\", \"value\": \"ExampleApp.exe\" } ] }",
                Section("ЗНАЧКИ И ОШИБКИ") + "ПРОВЕРКА\r\nУбедись, что Tailscale подключён и выбран доступный exit node. Нажми «Проверить». Ошибка видна в журнале и в «Подробностях».\r\n\r\nКОНФЛИКТ АДРЕСОВ\r\nSwitcher сам выбирает IPv4-подсеть, не пересекающуюся с адресами других адаптеров. Если ошибка повторилась, сохрани её точный текст. Чужие VPN автоматически не отключаются.",
                Section("О ПРОГРАММЕ")
            };
            var text = ReadableText(content[0]); text.Margin = new Padding(0, 8, 0, 0);
            var buttons = new Button[titles.Length];
            Action<int> select = index => {
                text.Text = content[index]; text.Select(0, 0); text.ScrollToCaret();
                for (int j = 0; j < buttons.Length; j++) {
                    buttons[j].BackColor = j == index ? AppTheme.Border : AppTheme.Surface;
                    buttons[j].ForeColor = j == index ? AppTheme.Blue : AppTheme.Muted;
                    buttons[j].AccessibleDescription = j == index ? "Выбранная вкладка" : "Вкладка помощи";
                }
            };
            for (int i = 0; i < titles.Length; i++) {
                int index = i; buttons[i] = new Button { Text = titles[i], Name = "helpTab" + i, AccessibleRole = AccessibleRole.PageTab };
                buttons[i].Click += delegate { select(index); };
            }
            tabs.Controls.Add(UiLayout.ButtonRow(buttons), 0, 0); tabs.Controls.Add(text, 0, 1);
            tabs.HandleCreated += delegate { select(0); };
            return tabs;
        }
        static HelpForm helpWindow;
        public static void Open(IWin32Window owner)
        {
            if (helpWindow == null || helpWindow.IsDisposed)
            {
                helpWindow = new HelpForm();
                helpWindow.FormClosed += delegate { helpWindow = null; };
                helpWindow.Show();
            }
            else
            {
                if (helpWindow.WindowState == FormWindowState.Minimized) helpWindow.WindowState = FormWindowState.Normal;
                helpWindow.BringToFront(); helpWindow.Activate();
            }
        }
        public static void CloseHelp() { if (helpWindow != null) helpWindow.Close(); }
        static readonly string Instructions =
            "БЫСТРЫЙ СТАРТ\r\nВыбери папки Tailscale и zapret и стратегию в настройках. После сохранения Switcher работает в трее.\r\n\r\n" +
            "ПЕРЕКЛЮЧЕНИЕ\r\nДвойной щелчок по значку или Ctrl+Alt+F8 переключает Tailscale и zapret. Ctrl+Alt+F9 закрывает Switcher. Сочетания можно изменить в настройках: нажми на поле и введи клавиши с Ctrl или Alt, при желании с Shift.\r\n\r\n" +
            "МАРШРУТИЗАЦИЯ\r\nВыбери доступный exit node в обычном Tailscale. В окне маршрутизации добавь исключения, сохрани и закрой окно. Сайты и приложения из списка идут напрямую, остальной интернет — через Tailscale. Локальная сеть доступна напрямую. Первое подходящее правило имеет приоритет.\r\n\r\n" +
            "Автозапуск работает при подключении Tailscale и при запуске Switcher с уже подключённым Tailscale. Нужен хотя бы один включённый пункт. Внешние изменения проверяются каждые 5 секунд. При открытом окне редактирования автозапуск ждёт его закрытия. При обрыве программа ждёт сеть и 10 секунд устойчивой связи. После ошибок запуска интервал повторов растёт от 1 до 5 минут.\r\n\r\n" +
            "ОСТАНОВКА\r\nВ меню трея выбери «Выключить маршрутизацию» либо нажми «Остановить» в окне правил. Tailscale останется подключённым. Автозапуск приостановлен до следующего подключения, явного выбора Tailscale в меню или перезапуска Switcher. При переходе на zapret исключения удаляются автоматически.\r\n\r\n" +
            "ПРАВИЛА И ИМПОРТ\r\nСайт: example.ru (включая поддомены). IP: 203.0.113.0/24. Программа: Discord.exe, без пути. Для программы нажми «Выбрать…» справа от ячейки: найди запущенный процесс по имени, выбери его или дважды щёлкни. В правило попадёт имя EXE. Если программы нет, запусти её и обнови список. Для сервиса могут понадобиться несколько доменов. TXT / LIST — один сайт или IP на строку; JSON / CSV — списки или подробные правила. Импорт показывает ошибки и пропускает повторы. После добавления сохрани список. Изменения действуют для новых соединений.\r\n\r\n" +
            "НАСТРОЙКИ ZAPRET\r\nВыбери корневую папку с bin\\winws.exe и файл стратегии .bat. Меню service.bat запускать не нужно. Смена стратегии кратко перезапускает работающий zapret.\r\n\r\n" +
            "ЗНАЧКИ И ОШИБКИ\r\nT — Tailscale, Z — zapret, R — исключения работают, ! — конфликт, ? — ошибка. Статус виден в меню трея, подробности — в пункте «Подробности». Обычные переключения проходят без всплывающих уведомлений.\r\n\r\n" +
            "ПРИ ВЫХОДЕ\r\nВременные исключения, адаптер и дочерний движок удаляются. Обычный Tailscale или zapret остаётся работать. Постоянные маршруты, DNS существующих адаптеров и правила брандмауэра не меняются. Поддерживается IPv4; ресурсы только с IPv6 недоступны.\r\n\r\n" +
            "УСТАНОВКА\r\nЗапусти Install.cmd и подтверди права администратора. Далее используй ярлык Switcher. При обновлении нужен запрос Windows, настройки сохраняются.\r\n\r\n" +
            "О ПРОГРАММЕ\r\nSwitcher " + typeof(HelpForm).Assembly.GetName().Version.ToString() + " · pb_coder\r\n© 2026 pb_coder. Все права защищены.\r\nСпасибо bol-van за zapret (MIT), Flowseal и участникам zapret-discord-youtube (MIT), nekohasekai и SagerNet за sing-box (GPLv3+). Ссылки, лицензии и полная инструкция — в README комплекта и репозитория.";
    }
}
