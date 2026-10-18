# Сторонние компоненты

## Intel PresentMon

Консольная версия PresentMon встроена в программу и кладётся установщиком в папку `tools`: она записывает FPS по минутам на вкладке «Монитор матча».
PresentMon читает системные события Windows (ETW) о выводе кадров и не обращается к процессу игры.

- Проект: https://github.com/GameTechDev/PresentMon
- Лицензия: MIT, Copyright (C) 2017-2024 Intel Corporation. Полный текст лежит рядом с исполняемым файлом: `tools\PresentMon-LICENSE.txt`.

## .NET

Приложение собрано как self-contained и включает среду выполнения .NET 8 (MIT, © .NET Foundation and Contributors).
Пакеты System.Management, System.Diagnostics.EventLog и System.Diagnostics.PerformanceCounter распространяются под лицензией MIT.
