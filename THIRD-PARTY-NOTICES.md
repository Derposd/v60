# Сторонние компоненты

## Intel PresentMon

Консольная версия PresentMon встроена в программу и кладётся установщиком в папку `tools`: она записывает FPS по минутам на вкладке «Монитор матча».
PresentMon читает системные события Windows (ETW) о выводе кадров и не обращается к процессу игры.

- Проект: https://github.com/GameTechDev/PresentMon
- Лицензия: MIT, Copyright (C) 2017-2024 Intel Corporation. Полный текст лежит рядом с исполняемым файлом: `tools\PresentMon-LICENSE.txt`.

## .NET

Приложение собрано как self-contained и включает среду выполнения .NET 8 (MIT, © .NET Foundation and Contributors).
Пакеты System.Management, System.Diagnostics.EventLog и System.Diagnostics.PerformanceCounter распространяются под лицензией MIT.

## Magpie и OptiScaler — не входят в FrameLab

Эти программы распространяются под лицензией **GNU GPL v3** и **не встроены** ни в FrameLab.exe, ни в установщик. FrameLab не содержит их кода.
По вашей кнопке программа скачивает официальный релиз с их страниц на GitHub и запускает или копирует его как отдельную программу:

- **Magpie** — https://github.com/Blinue/Magpie (© Blinue и участники). Версия и ссылка берутся из официального канала обновлений Magpie (`version.json`), архив проверяется по MD5. Отдельная копия лежит в `%LOCALAPPDATA%\Dota2Optimizer\tools\Magpie`; FrameLab меняет только её `config\config.json`. Шейдеры FSR 1, CAS (AMD), NIS (NVIDIA), Anime4K и другие входят в Magpie под их собственными лицензиями.
- **OptiScaler** — https://github.com/optiscaler/OptiScaler. Скачивается из последнего релиза GitHub; файлы копируются в папку выбранной игры с резервной копией всего, что заменяется.

Исходный код и полный текст лицензии каждой программы — на её странице.
