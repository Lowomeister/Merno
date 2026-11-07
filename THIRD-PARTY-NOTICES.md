# Сторонние компоненты Merno

Merno распространяется по GNU GPL версии 3 или более поздней (GPL-3.0-or-later), текст — в [LICENSE.txt](LICENSE.txt). Эта лицензия относится к коду Merno. Название «Merno» и значок Merno в неё не входят. Она не передаёт прав на сторонние шрифты, товарные знаки и программы, которые устанавливаются через Merno.

## Шрифты

Onest Regular, Medium и SemiBold, версия 2.001. Copyright 2021 The Onest Project Authors (https://github.com/simpals/onest). Лицензия SIL Open Font License 1.1, текст — в [Assets/Fonts/OFL.txt](Assets/Fonts/OFL.txt).

Segoe UI, Segoe Fluent Icons и Segoe MDL2 Assets — системные шрифты Windows. Их файлы Merno не содержит.

## Библиотеки

| Компонент | Лицензия |
|---|---|
| .NET и Windows Desktop Runtime (WPF, Windows Forms) | MIT и сторонние уведомления .NET: [WPF](docs/licenses/wpf-10.0.14-THIRD-PARTY-NOTICES.TXT), [Windows Forms](docs/licenses/winforms-10.0.14-THIRD-PARTY-NOTICES.TXT) |
| System.Management 10.0.14 | MIT |
| Запускатель Merno.exe собран Zig 0.15.2 с MinGW-w64 | Zig — MIT, MinGW-w64 — ZPL-2.1 с исключениями из его файла COPYING |
| Установщик и программа удаления: Inno Setup 6 (Jordan Russell, Martijn Laan) | лицензия Inno Setup, https://jrsoftware.org/files/is/license.txt |

Полные тексты лицензий лежат в папке `licenses`: у установленной Merno — в `Program Files\Merno\versions\<версия>\licenses`, у портативной — в архиве рядом с Merno.exe.

## Значки программ

Merno не содержит значков сторонних программ. Значок загружается на компьютере пользователя из Microsoft Store или через сервис значков Google по адресу сайта программы, только чтобы её было легко узнать. Права на значки принадлежат их владельцам; связь с ними или их одобрение не подразумеваются.

## Изображения Merno

Значок и картинки Merno (`Assets/app.ico`, `Assets/logo.png`, `Assets/logo-pixel.png`, `docs/images`) созданы владельцем проекта вместе с ИИ-помощником (Claude, Anthropic); снимки экрана — снимки самого Merno. Название и значок Merno в лицензию проекта не входят.

## Программы, которые ставит Merno

Сами установщики в Merno не входят: программы скачиваются с сайтов их производителей и распространяются на своих условиях, GPL Merno на них не распространяется. Ссылку на установщик Spotify Merno берёт из стороннего списка LoaderSpot и запускает установщик, только если он подписан Spotify.
