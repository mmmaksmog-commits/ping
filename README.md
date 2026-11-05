# Пинг by mysik

Лагает игра, рвётся созвон, «интернет то есть, то нет»? Программа каждую секунду проверяет связь и показывает, в чём дело.

**Страница программы и скачивание:** https://mysik-portfolio.vercel.app/soft/ping.html

## Что умеет

- **Пинг в реальном времени** — до роутера, Google, Cloudflare, Яндекса и своих серверов — игрового, рабочего, любого.
- **График за 5 минут** — задержка, скачки и потерянные пакеты.
- **Статус одним взглядом** — «Связь стабильная», «Теряются пакеты», «Пинг скачет», «Нет интернета».
- **Подсказка, где сбой** — роутер отвечает, а интернета нет — проблема у провайдера. Не отвечает и роутер — Wi-Fi или кабель.
- **Журнал обрывов** — когда пропадал интернет и на сколько.
- **Отчёт для провайдера** — страница с датами обрывов и общим временем без связи — можно приложить к жалобе.

## Как начать

Нужна только Windows 10 или 11 — компилятор .NET Framework 4.x уже встроен в систему.

1. Скачайте репозиторий: зелёная кнопка **Code → Download ZIP**, и распакуйте.
2. Запустите `build.bat` — через пару секунд рядом появится `Ping.exe`.
3. Откройте `Ping.exe`. Права администратора не нужны.
4. **Включите «Следить постоянно».** Программа будет вести журнал с момента включения компьютера.
5. **Смотрите на значок у часов.** Зелёный, жёлтый или красный. Уведомит, когда интернет пропал и когда вернулся.

Файлы: `MainWindow.xaml`, `Ping.cs`, `README.txt`, `Shell.cs`, `Theme.xaml`, `app.ico`, `app.manifest`, `build.bat`.

## Другие программы by mysik

- [Zapret GUI](https://mysik-zapret.netlify.app) — Возвращает Discord, YouTube и Steam одной кнопкой. Без VPN: скорость и IP остаются вашими.
- [DNS Switch](https://mysik-portfolio.vercel.app/soft/dns-switch.html) — Меняет DNS в один клик и сам проверяет, какой быстрее именно у вас.
- [Передача](https://mysik-portfolio.vercel.app/soft/peredacha.html) — Файлы, фото и тексты между компьютером и телефоном по QR-коду. Без проводов и облаков.
- [Video Loader](https://mysik-portfolio.vercel.app/soft/video-loader.html) — Скачивает видео и музыку с YouTube, VK Видео, RuTube и TikTok: до 4K, плейлисты, MP3 с обложкой.
- [Converter](https://mysik-portfolio.vercel.app/soft/converter.html) — Видео, музыка и картинки в нужный формат, включая HEIC с iPhone. Умеет сжимать перед отправкой.
- [Снимок](https://mysik-portfolio.vercel.app/soft/snimok.html) — Скриншот области по PrintScreen, стрелки, размытие и нумерация. Сразу в буфер.
- [Win Cleaner](https://mysik-portfolio.vercel.app/soft/win-cleaner.html) — Чистит мусор, убирает рекламу и Copilot, наводит порядок в автозагрузке. Всё можно вернуть.
- [Диск](https://mysik-portfolio.vercel.app/soft/disk.html) — Карта диска за минуту: видно, какие папки и файлы съели всё место.
- [Раскладка](https://mysik-portfolio.vercel.app/soft/raskladka.html) — Напечатали «ghbdtn»? Одна клавиша — и слово исправлено, раскладка переключена.
- [Буфер](https://mysik-portfolio.vercel.app/soft/bufer.html) — Помнит всё, что вы копировали: текст, ссылки, картинки. Ctrl+Shift+V — и вставлено.
- [Визитка](https://mysik-portfolio.vercel.app/soft/vizitka.html) — Конструктор сайтов для мастеров: маникюр, барбер, массаж. Сайт под ключ за 15–30 минут.

Все программы — в портфолио: https://mysik-portfolio.vercel.app/#programs  
Сделано [mysik](https://mysik-portfolio.vercel.app).
