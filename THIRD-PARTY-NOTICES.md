# Сторонние компоненты

## Intel PresentMon

Установщик кладёт консольную версию PresentMon в папку `tools` для вкладки «Замер FPS».
PresentMon читает системные события Windows (ETW) о выводе кадров и не обращается к процессу игры.

- Проект: https://github.com/GameTechDev/PresentMon
- Лицензия: MIT, Copyright (C) 2017-2024 Intel Corporation. Полный текст лежит рядом с исполняемым файлом: `tools\PresentMon-LICENSE.txt`.

## .NET

Приложение собрано как self-contained и включает среду выполнения .NET 8 (MIT, © .NET Foundation and Contributors).
Пакеты System.Management, System.Diagnostics.EventLog и System.Diagnostics.PerformanceCounter распространяются под лицензией MIT.
