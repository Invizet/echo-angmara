# Эхо Ангмара

Русский лаунчер для [LOTRO: Echoes of Angmar](https://www.echoesofangmar.com/).

Работает **поверх официального Echoes of Angmar Launcher**: берёт из него сохранённые логины
и подключается к серверу его же кодом (`EchoesLauncher.Common.dll` загружается из папки
официального лаунчера, в этом репозитории его нет). Сверху — русский интерфейс, установка
и обновление русского перевода, переключение RU/EN перед запуском, новости из
[Telegram-канала](https://t.me/echoesofangmar) и полезные ссылки.

## Структура

- `src/EchoAngmara` — сам лаунчер (C# / .NET 8 / WPF)
- `src/EchoesLauncher.Common.Stub` — заглушка публичного API официального ядра, только для компиляции
- `tools/news` — сборщик новостей из Telegram (запускается GitHub Actions)
- `tools/publish` — сборка манифеста перевода для публикации
- `spike/Stage0` — первый прототип (проверка подхода)

## Сборка

```
dotnet build src/EchoAngmara
```

Для разработки: `ECHO_SITE=<url>` — своё хранилище вместо GitHub Pages,
`ECHO_GAME_DIR=<папка>` — подставная папка игры.
