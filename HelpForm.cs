using System;
using System.Drawing;
using System.Windows.Forms;

namespace Switcher
{
    public sealed class HelpForm : Form
    {
        public HelpForm() : this("Помощь · Switcher", Instructions) { }
        public HelpForm(string title, string text)
        {
            Text = title; Font = new Font("Segoe UI", 10);
            Size = new Size(680, 470); MinimumSize = new Size(520, 350);
            StartPosition = FormStartPosition.CenterParent;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 2 };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            var body = new TextBox { Text = text, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
            var close = new Button { Text = "Закрыть", Size = new Size(100, 32), Anchor = AnchorStyles.Right, DialogResult = DialogResult.Cancel };
            layout.Controls.Add(body, 0, 0); layout.Controls.Add(close, 0, 1); Controls.Add(layout);
            CancelButton = close; AppTheme.Apply(this); AppTheme.Primary(close);
        }
        public static void Open(IWin32Window owner) { using (var form = new HelpForm()) form.ShowDialog(owner); }
        const string Instructions =
            "БЫСТРЫЙ СТАРТ\r\nВыбери папки Tailscale и zapret и стратегию в настройках. После сохранения Switcher работает в трее.\r\n\r\n" +
            "ПЕРЕКЛЮЧЕНИЕ\r\nДвойной щелчок по значку или Ctrl+Alt+F8 переключает Tailscale и zapret. Ctrl+Alt+F9 закрывает Switcher. Сочетания можно изменить в настройках: нажми на поле и введи клавиши с Ctrl или Alt, при желании с Shift.\r\n\r\n" +
            "МАРШРУТИЗАЦИЯ\r\nВыбери доступный exit node в обычном Tailscale. В окне маршрутизации добавь исключения, сохрани и закрой окно. Сайты и приложения из списка идут напрямую, остальной интернет — через Tailscale. Локальная сеть доступна напрямую. Первое подходящее правило имеет приоритет.\r\n\r\n" +
            "Автозапуск работает при подключении Tailscale и при запуске Switcher с уже подключённым Tailscale. Нужен хотя бы один включённый пункт. Внешние изменения проверяются каждые 5 секунд. При открытом окне редактирования автозапуск ждёт его закрытия. При ошибке следующая попытка — через минуту.\r\n\r\n" +
            "ОСТАНОВКА\r\nВ меню трея выбери «Выключить маршрутизацию» либо нажми «Остановить» в окне правил. Tailscale останется подключённым. Автозапуск приостановлен до следующего подключения, явного выбора Tailscale в меню или перезапуска Switcher. При переходе на zapret исключения удаляются автоматически.\r\n\r\n" +
            "ПРАВИЛА И ИМПОРТ\r\nСайт: example.ru (включая поддомены). IP: 203.0.113.0/24. Программа: Discord.exe, без пути. Для сервиса могут понадобиться несколько доменов. TXT / LIST — один сайт или IP на строку; JSON / CSV — списки или подробные правила. Импорт показывает ошибки и пропускает повторы. После добавления сохрани список. Изменения действуют для новых соединений.\r\n\r\n" +
            "НАСТРОЙКИ ZAPRET\r\nВыбери корневую папку с bin\\winws.exe и файл стратегии .bat. Меню service.bat запускать не нужно. Смена стратегии кратко перезапускает работающий zapret.\r\n\r\n" +
            "ЗНАЧКИ И ОШИБКИ\r\nT — Tailscale, Z — zapret, R — исключения работают, ! — конфликт, ? — ошибка. Статус виден в меню трея, подробности — в пункте «Подробности». Обычные переключения проходят без всплывающих уведомлений.\r\n\r\n" +
            "ПРИ ВЫХОДЕ\r\nВременные исключения, адаптер и дочерний движок удаляются. Обычный Tailscale или zapret остаётся работать. Постоянные маршруты, DNS существующих адаптеров и правила брандмауэра не меняются. Поддерживается IPv4; ресурсы только с IPv6 недоступны.\r\n\r\n" +
            "УСТАНОВКА\r\nЗапусти Install.cmd и подтверди права администратора. Далее используй ярлык Tailscale - zapret. При обновлении нужен запрос Windows, настройки сохраняются.\r\n\r\n" +
            "О ПРОГРАММЕ\r\nSwitcher 1.1.0.0 · pb_coder\r\n© 2026 pb_coder. Все права защищены.\r\nСпасибо bol-van за zapret (MIT), Flowseal и участникам zapret-discord-youtube (MIT), nekohasekai и SagerNet за sing-box (GPLv3+). Ссылки, лицензии и полная инструкция — в README комплекта и репозитория.";
    }
}
