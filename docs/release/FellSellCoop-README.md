# Fell & Sell Coop 0.1.0

Совместная игра для Fell & Sell: походы в подземелье, в лес и на арену орды вдвоём через Steam. Версия ранняя, проверена на двух игроках.

## Что нужно

- MelonLoader 0.7 для Fell & Sell.
- **Fell & Sell Plus 1.1.0. Без него мод не загрузится.**
- Игра в Steam у каждого игрока, игроки в друзьях друг у друга.
- Одинаковые версии модов у всех игроков.

По желанию, у всех игроков сразу:

- Fell & Sell Character 1.0.0: напарник выглядит как его герой и держит своё оружие. Без него напарник показан фигурой жителя.
- Fell & Sell Horde 1.0.1: орда вдвоём.
- Fell & Sell Bows 1.0.1: луки в совместной игре.

## Как играть

Хост:

1. В главном меню нажми «Кооператив» (под «Загрузить»).
2. Нажми «Создать игру».
3. Загрузи своё сохранение как обычно.

Гость:

1. В главном меню нажми «Кооператив».
2. Рядом с именем друга, который создал игру, нажми «Войти».
3. Загрузи своё сохранение.

Хост может нажать «Пригласить» рядом с именем друга: тому придёт приглашение в чат Steam. Если принять его при закрытой игре, игра запустится и подключится сама. Если игра уже открыта, приглашение не сработает: зайди в «Кооператив» и нажми «Войти».

С модом Fell & Sell Horde то же окно открывается в деревне: кнопка «Пригласить друга» в окне статуи жнеца. Ты сразу становишься хостом и видишь список друзей с кнопкой «Пригласить».

Выбор действует до закрытия игры. Чтобы играть одному, ничего не нажимай или выбери «Одиночная игра».

## Как это работает

- Вместе проходятся только походы. Деревня и магазин у каждого свои.
- Когда хост входит в подземелье или в лес, гостя из деревни забирает следом через 5 секунд. Этаж начинается, когда загрузились оба.
- Враги, боссы и события комнат общие, их ведёт игра хоста. Сложность берётся у хоста.
- Каждый играет своим героем со своим сохранением: уровень, навыки, вещи. Добыча у каждого своя, в совместной игре её на 30% меньше.
- Погибший не умирает, пока жив напарник: он лежит, а напарник поднимает его, встав у круга портала. В запертой комнате босса, в лесу и на арене орды надо постоять рядом с лежащим 5 секунд. Если погибли оба, срабатывает обычная смерть.
- Когда напарник подключён, меню и инвентарь не ставят игру на паузу.
- Орда: хост выбирает сложность у статуи жнеца и идёт в подземелье. Врагов и их здоровья в полтора раза больше. Дары и предметы из сундука у каждого свои.

## Двери, ловушки, лес, молитвы

- Дверь, которую открыл один игрок, открыта у всех на этаже.
- Ловушка с нажимной плитой срабатывает у всех, кто бы на неё ни наступил. Вращающиеся лезвия крутятся в такт с хостом. Урон каждый получает только от ловушек своей игры, дважды не бьёт.
- Событие леса (погода, свет, кто там водится) берётся у хоста. Молнии в грозу у каждого свои.
- Молитвы у каждого свои. Шипы бьют общих врагов. Молитвы «больше врагов» работают только у хоста: врагов ведёт его игра.

Отключается в настройках: `ShareDoors`, `ShareTraps`, `ShareForestEvent`.

## Что пока не общее

Шипы, которые поднимаются по таймеру. Молнии грозы. Разрушаемые предметы. Сундуки у каждого свои. Три и четыре игрока запускались по одному разу, по локальной сети.

## Установка

1. Поставь Fell & Sell Plus 1.1.0 (вместе с ним ставится MelonLoader).
2. Положи `FellSellCoop.dll` в папку `Mods` в папке игры, рядом с `FellSellPlus.dll`.

Настройки лежат в `UserData/MelonPreferences.cfg`, раздел `[FellSellPlus_Coop]`. Для игры через Steam менять их не нужно. Там же включается игра по локальной сети (`Transport = "udp"`, адрес и порт хоста).

## Удаление

Удали `FellSellCoop.dll` из `Mods`. Сохранение не сломается.

---

# Fell & Sell Coop 0.1.0 (English)

Co-op for Fell & Sell: dungeon, forest and horde arena runs for two players over Steam. This is an early version, tested with two players.

## Requirements

- MelonLoader 0.7 for Fell & Sell.
- **Fell & Sell Plus 1.1.0. The mod does not load without it.**
- Every player owns the game on Steam, and the players are Steam friends.
- The same mod versions for every player.

Optional, for all players at once:

- Fell & Sell Character 1.0.0: your partner looks like their hero and holds their own weapons. Without it a partner is shown as a villager figure.
- Fell & Sell Horde 1.0.1: horde runs together.
- Fell & Sell Bows 1.0.1: bows in co-op.

## How to play

Host:

1. In the main menu press "Co-op" (under "Load").
2. Press "Host a game".
3. Load your save as usual.

Guest:

1. In the main menu press "Co-op".
2. Press "Join" next to the name of the friend who hosts.
3. Load your save.

The host can press "Invite" next to a friend's name, which sends an invitation to Steam chat. Accepted while the game is closed, it starts the game and joins by itself. While the game is already open an invitation does nothing: open "Co-op" and press "Join".

With the Fell & Sell Horde mod the same window opens in the village: "Invite a friend" in the reaper statue's window. You become the host at once and see your friends with "Invite" next to each.

The choice lasts until you close the game. To play alone, press nothing or pick "Single player".

## How it works

- Only runs are shared. Everyone keeps their own village and shop.
- When the host enters the dungeon or the forest, a guest in the village is taken along 5 seconds later. A floor starts once both have loaded it.
- Enemies, bosses and room events are shared and run by the host's game. Difficulty is the host's.
- Everyone plays their own hero with their own save: level, skills, items. Loot is per player, and there is 30% less of it in co-op.
- A fallen player does not die while the partner lives: they stay down, and the partner revives them by standing at the portal circle. In a locked boss room, in the forest and in the horde arena, stand next to the fallen player for 5 seconds. If both fall, the game's normal death applies.
- With a partner connected, menus and the inventory do not pause the game.
- Horde: the host picks a difficulty at the reaper statue and enters the dungeon. There are 1.5 times more enemies with 1.5 times more health. Gifts and chest items are per player.

## Doors, traps, forest, prayers

- A door one player opened is open for everybody on that floor.
- A pressure-plate trap goes off for everybody, whoever stepped on it. Spinning blades turn in step with the host's. You are hurt only by the traps of your own game, never twice.
- The forest event (weather, light, what lives there) is the host's. Lightning in a storm is each player's own.
- Prayers are per player. Thorns hurts the shared enemies. "More enemies" prayers count only for the host, whose game runs the enemies.

Switches in the settings: `ShareDoors`, `ShareTraps`, `ShareForestEvent`.

## Not shared yet

Spikes that rise on a timer. Storm lightning. Destructible props. Chests are per player. Three and four players ran once each, over LAN.

## Install

1. Install Fell & Sell Plus 1.1.0 (it brings MelonLoader).
2. Put `FellSellCoop.dll` into the game's `Mods` folder, next to `FellSellPlus.dll`.

Settings are in `UserData/MelonPreferences.cfg` under `[FellSellPlus_Coop]`. Steam play needs no changes there. LAN play is switched on there too (`Transport = "udp"`, the host's address and port).

## Uninstall

Delete `FellSellCoop.dll` from `Mods`. Your save stays fine.
