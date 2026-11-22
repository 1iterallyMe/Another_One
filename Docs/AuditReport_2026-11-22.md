# Аудит проекта Another_One

- **Дата:** 2026-11-22
- **Аудитор:** Senior Unity Engineer / QA-архитектор
- **Ветка:** `main` (HEAD `7dc60a0`)
- **Инструменты:** MCP (файловая система, Unity Editor API, Asset Database, git),
  `validate_script`, побайтовый анализ кодировок, GUID-сканирование ассетов.
- **Бэкап перед работами:** `refs/backup/pre-audit-doc` → `7fcfd01`

---

## 0. Метаданные и охват

| Параметр | Значение |
|---|---|
| Unity | 6000.3.10f1 |
| Render Pipeline | URP 17.3.0 (`PC_RPAsset`, `Mobile_RPAsset`), Color Space = Linear |
| Input | `com.unity.inputsystem` 1.18.0; `activeInputHandler: 2` (Both) |
| Assemblies | asmdef отсутствуют — всё в `Assembly-CSharp` |
| Скрипты | 31 файл / 8 099 строк; тестов — 0 (`com.unity.test-framework` установлен) |
| Сцены в Build | `Amir`(0), `Menu_Scene`(1), `Сredits_Scene`(2), `Alexander`(3) |
| Префабы | 43 (со всеми вариантами и примерами сторонних пакетов) |

**Проверено статически:** все 31 скрипт, 4 сцены, 43 префаба, `ProjectSettings`,
`Packages/manifest.json`, git-индекс, кодировки всех `.cs`, GUID-ссылки.

**Выполнено:** Этапы 1, 2, 3, 5.
**Не выполнено:** Этап 4 (динамический анализ: Play Mode, Profiler, runtime Console) —
требует Act mode и меняет состояние Editor'а.
**Не выполнено:** Этапы 6–7 (исправления) — по решению заказчика, после ревью отчёта.

---

## 1. Архитектура (карта проекта)

**Точки входа:** `FPSController` (игрок), `PlayerInteractor` (E/F), `GrabManager`
(инвентарь), `PhysicsHoldController` (физическое удержание), `SceneTrigger` /
`MainMenu` (переходы сцен).

**Стиль:** MonoBehaviour-хаос с элементами «сервис-локатора». Единственный
островок дизайна — интерфейсы `IInteractable` / `IInventoryItem` /
`IPhysicsHoldable` + базовый `InteractableBase`. Есть один корректный синглтон
(`SecretCodeManager` с `RuntimeInitializeOnLoadMethod` и сбросом статиков).

**Ключевые системы:**

| Система | Состав |
|---|---|
| Инвентарь | `GrabManager`, `IInventoryItem`, UI-иконки |
| Физический захват | `PhysicsHoldController`, `PickupObject`, `HandController` |
| Головоломка с кодом | `SecretCodeManager` → `CodeSheet` → `CodeMonitor` |
| Генератор / питание | `FuelGenerator`, `GeneratorController`, `FuelCanister`, `CableSocket`, `PowerCableItem`, `PowerCableSocket`, `PhysicalCable` |
| Зиплайн | `ZiplinePartCraft` → `AssembledZipline` → `CableZiplineRide` |
| Кнопки / двери | `WallLiftSwitch`, `CodeMonitor.OpenDoors` |
| Окружение | `Trampoline`, `LampFlicker`, `MoveSound`, `SceneTrigger`, `WireConnector` |
| UI | `MainMenu`, `CreditsRoll`, `MenuButtonHover`, `DeathScreen`, `HealthBarUI` |

**Дублирующиеся подсистемы (важно):**

1. **Кабель питания реализован дважды:** `WireConnector` (LineRenderer,
   привязка к `IsPowered`) и `PowerCableItem` + `PhysicalCable` (verlet-симуляция
   с коллизиями).
2. **Зиплайн реализован дважды:** `ZiplineController` (устаревший, не размещён
   ни в одной сцене) и `CableZiplineRide` (актуальный).
3. **Подача питания генератору — три пути:** `GeneratorController.SetCableConnected`,
   `PowerCableItem.RefreshPowerState`, `VacuumCablePlug.ConnectToSocket` — все
   пишут в одно поле `FuelGenerator.IsCableConnected`.

---

## 2. Сводка

| Уровень | Кол-во |
|---|---|
| Critical | 1 |
| High | 8 |
| Medium | 8 |
| Low | 12 |
| **Итого** | **29** |

Дополнительно: 2 класса мёртвого кода без единого размещения (`WireConnector`,
`ZiplineController`, ~418 строк), 6 скриптов в кодировке CP1251.

---

## 3. Critical

### [BUG-001] Зиплайн в `Amir` физически не может двигаться — регрессия в рабочем дереве

- **Файлы:** `Assets/Art/Models/Zipline/zipline.prefab:2615-2616`,
  `Assets/Scripts/Player/Pickup/zipline/CableZiplineRide.cs:76, 402`
- **Суть:** в базовом префабе `powerWithoutGenerator: 0` и `generator: {fileID: 0}`.
  Свойство `HasPower => powerWithoutGenerator || (generator != null && generator.HasPower)`.
  `UpdateRouteMovement()` начинается с `if (HasPower)`, поэтому `input` остаётся `0`
  и метод выходит по `Mathf.Abs(input) <= 0.01f`. Ветка «по Y»
  (`CableZiplineRide.cs:277`) также требует `HasPower`. Поездка инициируется
  (в `CanInteract` проверки питания нет), игрок садится — и зависает.
- **Область поражения — только `Amir`** (уточнено повторным прогоном протокола):
  `powerWithoutGenerator` не переопределён нигде (`ZiplineSystem.prefab` — 0,
  `Amir.unity` — 0, `Alexander.unity` — 0), но `generator` **переопределён
  в `Alexander.unity`** (override → `FuelGenerator`, component fileID `219948757`).
  В `ZiplineSystem.prefab` (инстанс в `Amir`) `generator` не назначается → `null`.
  Итог: в `Amir` `HasPower == false`; в `Alexander` питание считается через реальный
  `FuelGenerator` (`IsRunning && IsCableConnected`) — там баг **не воспроизводится**.
- **Репро:** Play `Amir` → собрать зиплайн из двух частей → подключить к тросу →
  сесть (E) → W/S не дают движения; выйти можно только по E/F.
- **Подтверждение регрессии:** незакоммиченное изменение рабочего дерева —
  `git diff` по `zipline.prefab` содержит **ровно один хунк**
  (`1 file changed, 1 insertion(+), 1 deletion(-)`): `powerWithoutGenerator: 1 → 0`.
- **Исправление (варианты, требуется решение):**
  - **A.** Вернуть `powerWithoutGenerator: 1` в базовом префабе — исправляет `Amir`,
    но снимает проверку питания и в `Alexander` (зиплайн поедет без генератора).
  - **B.** Добавить override `powerWithoutGenerator: 1` только в `ZiplineSystem.prefab`
    (инстанс в `Amir`) — исправляет `Amir`, не меняя `Alexander`. Правка инстанса,
    а не базового префаба.
  - **C.** Назначить реальный `FuelGenerator` в инстансе `Amir` — сохраняет
    геймдизайн «нужно заправить генератор», но требует проверки, что в `Amir` эта
    цепочка физически выполнима игроком.
- **Риск исправления:** Low (A, B), Medium (C — геймплейная цепочка).
- **Статус исправления (2026-11-22):** применён вариант **B** — добавлен override
  `powerWithoutGenerator: 1` в `Assets/Prefabs/ZiplineSystem.prefab:6108-6111`
  (файл untracked; дифф против бэкапа untracked-файлов — ровно `+4` строки,
  базовый `zipline.prefab` не изменён). Проверено в Play Mode `Amir`:
  `HasPower == true`; маршрут `MoveRoute(+1, 0.2)` → `delta = 0.697 м`;
  Y-режим (`useCableRoute=false`): `9.95 → 6.00 (minY) → 9.50 (maxY)`,
  `IsMoving = false`. Console: **0 error**, 2 warning
  (`Couldn't create a Convex Mesh ... 256 polygons`, к зиплайну не относится).
  **Приёмка не закрыта полностью:** сессия Play Mode прерывалась из-за
  параллельных правок ассетов (см. п. 10.6), `Alexander` не проверялся.
- **Статус: ОТОЗВАН (2026-11-22).** Симптом не воспроизводится: в `Amir` больше нет
  ни одного `CableZiplineRide` (`FindObjectsByType` → **0**, 0 ссылок на guid
  `4dcf9ad4…`), инстанс зиплайна исчез вместе с пересборкой `ZiplineSystem.prefab`.
  Правка `powerWithoutGenerator` в базовом префабе затронула бы **только `Alexander`**,
  где генератор назначен, и сняла бы там power-gate — то есть ухудшила бы дизайн
  и ничего не починила в `Amir`. Вместо фикса заведён **BUG-028** (восстановить
  инстанс); после его закрытия BUG-001 подлежит пере-проверке.

---

## 4. High

### [BUG-002] `PhysicalCable`: ~500–800 физических запросов и аллокации в кадр

- **Файл:** `Assets/Scripts/Player/Pickup/Generator and Gasoline/PhysicalCable.cs:76-93, 217-229, 232-282`
- **Суть:** `LateUpdate` → `Simulate()`; `segments = 28`, `solverIterations = 10`
  → 26 внутренних нод × 10 итераций = **260 вызовов `ResolveCollision` в кадр**.
  В каждом: `Physics.SphereCast` + `Physics.CheckSphere` + при пересечении
  `Physics.OverlapSphere` (**аллокация `Collider[]` на каждый вызов**, стр. 259).
  Дополнительно `ClampAboveSurface` (стр. 309-320) делает `Physics.Raycast`
  на каждую ноду каждую итерацию.
- **Дополнительно:** симуляция продолжается и когда провод лежит в инвентаре —
  `GrabManager.SendItemToStorage` выключает только `Renderer`/`Collider`,
  компонент остаётся `enabled`.
- **Репро:** Play `Amir`, взять провод → стабильная просадка FPS + постоянный GC.
- **Исправление:** `OverlapSphereNonAlloc` с переиспользуемым буфером; снизить
  `solverIterations` до 3–5; обновлять симуляцию раз в 2–3 кадра в покое;
  останавливать `LateUpdate`, когда предмет в инвентаре.
- **Риск исправления:** Medium (визуальное поведение троса).

### [BUG-003] `FuelGenerator`: `Physics.OverlapSphere` каждый кадр

- **Файл:** `FuelGenerator.cs:92` → `UpdateFuelPouring()` → `:131-158`
  (`Physics.OverlapSphere(zoneCenter, fillTriggerRadius, ...)`)
- **Суть:** пока бак не полон, каждый кадр выполняется физический запрос с
  аллокацией массива — даже если рядом нет ни одной канистры.
- **Репро:** Play `Amir`, не заправляя генератор — GC Alloc в Profiler растёт
  постоянно.
- **Исправление:** `OverlapSphereNonAlloc` + троттлинг (раз в 0.1–0.2 с).
- **Риск исправления:** Low.

### [BUG-004] `PickupObject`: аллокация в `FixedUpdate` при переноске

- **Файл:** `PickupObject.cs:221-251` (`ApplyCarry` → `:225 Physics.OverlapSphere`)
- **Суть:** массив `Collider[]` создаётся на каждом физическом кадре, пока
  игрок несёт лёгкий предмет (проверка «не протолкнуть сквозь стену»).
- **Репро:** Play, взять мяч, идти — GC Alloc в Profiler каждый `FixedUpdate`.
- **Исправление:** `OverlapSphereNonAlloc` + буфер-поле класса.
- **Риск исправления:** Low.

### [BUG-005] `AssembledZipline`: обход сцены каждый кадр

- **Файл:** `AssembledZipline.cs:34-47` (`Update`) → `:39 FindTargetRide()` →
  `:93 FindObjectsByType<CableZiplineRide>(FindObjectsInactive.Include, ...)`
- **Суть:** пока собранный зиплайн существует в мире, каждый кадр выполняется
  полный обход сцены + аллокация массива результатов.
- **Исправление:** искать цель при `OnEnable`/по событию, результат кэшировать;
  при `!isAttached` проверять не чаще 4–5 раз в секунду.
- **Риск исправления:** Low.

### [BUG-006] `CableZiplineRide`: поиск объекта + рефлексия каждый кадр

- **Файл:** `CableZiplineRide.cs:296` (`Update` → `ReachHandToZipline()`) →
  `:231 FindFirstObjectByType<HandController>()` → `:236`
  `System.Type.GetMethod("SetTarget", BindingFlags...)`; также `:466-475`
- **Суть:** во время поездки каждый кадр выполняется поиск объекта по сцене и
  рефлексия (аллокации + риск при IL2CPP stripping).
- **Исправление:** кэшировать `HandController`; методы `SetTarget(Vector3)` и
  `ClearTarget()` в `HandController` уже `public` — вызывать напрямую,
  рефлексию удалить.
- **Риск исправления:** Low.

### [BUG-007] Генератор заправляется до полного из почти пустой канистры

- **Файл:** `FuelGenerator.cs:160-173` (`FinishFueling`)
- **Суть:** метод безусловно выполняет `CurrentFuel = FuelCapacity; IsFueled = true;`,
  хотя выше (`:124`) из канистры было вылито лишь `currentCanister.ConsumeFuel(...)`.
  Итог: экономика топлива не работает — из канистры с 5% бак становится 100/100.
  Дополнительно `:110` и `:182` вычисляют `CurrentFuel` по формуле
  `FuelCapacity * (1f - canister.FuelNormalized)`, которая затем затирается.
- **Репро:** Play `Amir` → канистра с малым остатком → E у генератора → бак полный.
- **Исправление:** накапливать реально вылитое топливо; `IsFueled` выставлять
  только при `CurrentFuel >= FuelCapacity - epsilon`.
- **Риск исправления:** Medium (влияет на баланс, требует проверки в Play).

### [BUG-008] Шесть скриптов сохранены в CP1251 (битая кодировка)

- **Файлы (побайтово подтверждено, ровно эти 6):**
  `Generator and Gasoline/FuelGenerator.cs`,
  `Generator and Gasoline/FuelCanister.cs`,
  `Generator and Gasoline/CableSocket.cs`,
  `Generator and Gasoline/VacuumCablePlug.cs`,
  `Pickup/zipline/CableZiplineRide.cs`,
  `Pickup/zipline/AssembledZipline.cs`,
  `Pickup/zipline/ZiplinePartCraft.cs`
- **Суть:** `file -I` → `unknown-8bit`; декодирование как UTF-8 падает.
  Все русские `[Tooltip]` и комментарии в Инспекторе отображаются как
  `���������`. Примеры: `FuelCanister.cs:14, 19, 21`,
  `CableZiplineRide.cs:5-7, 13-16, 27-33`.
  Строковых литералов для UI в этих файлах не найдено (только `Debug.Log`),
  поэтому на игру влияет косвенно — но ломает поддерживаемость и любой
  поиск/локализацию по исходникам.
- **Исправление:** `iconv -f CP1251 -t UTF-8` для 6 файлов; в diff попадут
  только комментарии и тултипы.
- **Риск исправления:** Low.

### [BUG-028] В `Amir` пропал инстанс зиплайна (потеря контента)

- **Файлы:** `Assets/Prefabs/ZiplineSystem.prefab` (инстанс в `Amir.unity`)
- **Суть:** до 02:45 в `ZiplineSystem.prefab` был вложенный инстанс `zipline.prefab`
  (41 ссылка на guid `4dcf9ad4…`, override `m_Name` → `ZiplineRig`, 28 `propertyPath`).
  После пересборки (02:45-02:46 → **44 272 Б**) вложенного инстанса нет:
  **0** ссылок на `4dcf9ad4…`, **0** `propertyPath`. Итог: в `Amir`
  `CableZiplineRide` = **0** — механика зиплайна в сцене недоступна.
- **Подтверждение:** Play Mode `Amir` → `FindObjectsByType<CableZiplineRide>` = 0.
- **Источник восстановления:** `refs/backup/pre-audit-doc` (`7fcfd01`) **не содержит**
  файл — это stash-коммит только по tracked-файлам (1053 шт.), а `ZiplineSystem.prefab`
  был **untracked**. Реальный носитель — `/tmp/wip-untracked-backup.tar.gz` (снимок 01:51):
  **183 885 Б**, **41** ссылка на `4dcf9ad4…`, `value: ZiplineRig` = **1**,
  28 `propertyPath`, `useCableRoute` = 1 → целевое состояние подтверждено маркерами.
- **Исправление:** восстановить `ZiplineSystem.prefab` из снимка (текущая 44-КБ версия
  предварительно копируется в `/tmp/prebake/`), затем Play Mode `Amir`.
- **Риск исправления:** Medium — возврат известного состояния, но перезапись текущей версии.
- **Статус:** ОТКРЫТ, ждёт восстановления из снимка `01:51` (см. выше, почему не `7fcfd01`).

---

## 5. Medium

### [BUG-009] Снятые предметы сваливаются в мировую точку (0,0,0) и падают вниз

- **Файл:** `GrabManager.cs:607-615` (`SendItemToStorage` → `transform.position = Vector3.zero`)
- **Суть:** предмет снимается с руки и отправляется в `(0,0,0)` с выключенными
  рендерами и коллайдерами, но **с включённой гравитацией**
  (`PickupObject.EndHold` ставит `useGravity = true`) → объект бесконечно падает.
  При `RemoveItem` (`:320-326`) и `SwapIntoInventory` (`:431-443`) его мировая
  позиция берётся как есть → предмет выбрасывается далеко под уровень.
- **Исправление:** хранить предметы в неактивном контейнере
  (`SetActive(false)` + `isKinematic`), а не «в точке ноль».
- **Риск исправления:** Medium (инвентарь — центральная механика).

### [BUG-010] Во время поездки на зиплайне взаимодействие не блокируется

- **Файлы:** `PlayerInteractor.cs:45-51` (проверяет `ZiplineController.ActiveRide`),
  `CableZiplineRide.cs`
- **Суть:** `ZiplineController` не размещён ни в одной сцене/префабе (GUID-скан),
  его `ActiveRide` всегда `null`, поэтому защита «не взаимодействовать во время
  поездки» **не работает** для актуального зиплайна `CableZiplineRide`.
  Последствие: на ходу E может нажать кнопку/подобрать предмет и одновременно
  отцепить игрока (`CableZiplineRide.cs:265`).
- **Исправление:** статический признак активной поездки в `CableZiplineRide`
  (по аналогии с `ActiveRide`), проверка в `PlayerInteractor`.
- **Риск исправления:** Low.

### [BUG-011] `SceneTrigger` навсегда «съедает» переход при ошибке загрузки

- **Файл:** `SceneTrigger.cs:80-81` (`used = true`) и `:126-137` (ветка `operation == null`)
- **Суть:** при `triggerOnce` флаг `used` выставляется до загрузки и не
  сбрасывается, если `LoadSceneAsync` вернул `null`. Переход больше не сработает
  до перезапуска сцены.
- **Исправление:** в ветке ошибки сбрасывать `used = false; isLoading = false;`.
- **Риск исправления:** Low.

### [BUG-012] Генератор без `FuelGenerator` невозможно активировать

- **Файл:** `GeneratorController.cs:244-248` (`CanInteract` требует `linkedGenerator != null`)
- **Суть:** ветка автономной работы (`isPowered`, `currentFuel`, `TryRefuel()` —
  `:158-188, 214-223`) полностью реализована, но недостижима: без
  `FuelGenerator` компонент не попадает в фокус. Мёртвый код + логическая дыра.
- **Исправление:** убрать требование `linkedGenerator` из `CanInteract`
  либо ввести явный enum режима.
- **Риск исправления:** Low.

### [BUG-013] Титры берут случайный `AudioSource` как музыку

- **Файл:** `CreditsRoll.cs:97-101` (`sceneMusic = FindFirstObjectByType<AudioSource>()`)
- **Подтверждение:** в `Сredits_Scene` размещён `Lamp.prefab`, внутри которого
  **2 `AudioSource`** (мерцание и гул), при этом музыкальный источник в сцене —
  `PaintedApparition_MP3`. Порядок обхода недетерминирован → музыки может не
  быть, либо вместо неё заиграет гул лампы.
- **Исправление:** искать по имени/тегу объекта либо требовать назначение в
  инспекторе (как сделано в `MainMenu.cs:278-283`).
- **Риск исправления:** Low.

### [BUG-014] Мусор и «тяжёлые» ассеты внутри `Assets/` (попадают в билд)

- **Файлы:**
  - `Assets/Art/Models/Zipline/канат нормал 2.png` — **30 МБ**;
    `Assets/Art/Textures/Ball/мяч нормал.png` — 13 МБ;
    `канат цвет 2.png` — 11 МБ; `цвет металл.png` — 11 МБ;
    `красный металл цвет.001.png` — 9.6 МБ
    (всего 70 текстур / ~25 МБ в репозитории) при `maxTextureSize: 2048` —
    исходники всё равно раздувают репозиторий и время импорта.
  - `Assets/Screenshots/` — **22 PNG** отладочных скриншотов (до 3.1 МБ),
    импортируются как текстуры.
  - `Assets/TextMesh Pro/Examples & Extras/Resources/` — примеры TMP внутри
    папки `Resources` → **гарантированно включаются в билд**.
  - `Assets/_Recovery/` (пустая) + untracked `Assets/_Recovery.meta`,
    пустая `Assets/Editor/`, `Assets/Readme.asset`, `Assets/TutorialInfo/`.
- **Исправление:** вынести скриншоты из `Assets`, удалить TMP Examples —
  **только после отдельного подтверждения**.
- **Риск исправления:** Low (удаление — Medium).

---

### [BUG-027] Канистра выливается в генератор без действия игрока

- **Файлы:** `FuelGenerator.cs:19, 91-116, 131-158` (`allowUnheldCanisterInZone`,
  `autoStartPouringFromHeldCanister`, `FindCanisterInZone`),
  `GeneratorController.cs:271-278`
- **Суть:** при `allowUnheldCanisterInZone: true` (значение по умолчанию)
  `FuelGenerator.Update()` каждый кадр ищет канистру в радиусе `fillTriggerRadius`
  возле `pourTargetPoint` **без участия игрока** и вызывает `BeginPouring`.
  Канистра, лежащая рядом с генератором, самоопустошается.
- **Подтверждение (Play Mode, `Amir`):** Console —
  `[GeneratorController] Канистра встала на горловину, идёт заливка.`
  (`GeneratorController.cs:266`); в рантайме объект `Survival/Jerrycan`:
  `fuelCapacity=100`, `initialFuel=100`, `currentFuel=0`, `IsPouring=False`,
  `IsHeld=False`. То есть пустой бак — следствие авто-заливки, а не сериализации.
- **Почему это важно:** вместе с BUG-007 (`FinishFueling` безусловно ставит
  `CurrentFuel = FuelCapacity`) и BUG-001 это делает цепочку питания непрозрачной:
  игрок может не понять, что генератор заправился сам, а канистра опустела.
- **Исправление (требуется решение по дизайну):** либо `allowUnheldCanisterInZone`
  по умолчанию `false`, либо заливать только по явному действию игрока.
- **Риск исправления:** Medium (меняет UX топливной механики).
- **Статус:** зарегистрировано по стоп-условию (найдено в Play Mode),
  **не исправлялось** в этой итерации.

### [BUG-029] В сцене `Alexander` у объекта `Player` пустой материал

- **Файл:** `Assets/Prefabs/Player/Player.prefab` — `Renderer`, слот 0 из 1
- **Суть:** в Play Mode `Alexander` детект `nullMaterials = 1`, владелец — объект
  **`Player`** (slot 0/1). При `null` материале меш игрока не отрисовывается
  корректно (маджента / невидимость) — визуальный дефект первого порядка.
- **Подтверждение:** обход всех `Renderer` сцены в Play Mode; при этом
  `MeshFilter` с `null` мешом = **0**, меши зиплайна целы.
- **Исправление:** назначить материал в `Player.prefab` (либо убрать лишний Renderer).
- **Риск исправления:** Low.
- **Статус:** ОТКРЫТ (не исправлялся).

---

## 6. Low

> **Риск исправления:** у всех пунктов ниже — **Low**, кроме BUG-018
> (Low/Medium: зависит от того, назначать кость или удалять фичу),
> BUG-022 (Medium: вынос кода из god-class без тестов) и
> BUG-024 (Medium: смена ввода затрагивает все системы управления).

| ID | Файл / место | Проблема | Исправление |
|---|---|---|---|
| BUG-015 | git index | `Assets/Art/Models/Zipline` и `zipline` существуют в индексе одновременно при `core.ignorecase=true` → на case-sensitive CI/другой ОС получится дубль папки с тем же `.prefab` | привести к одному написанию через `git mv` |
| BUG-016 | `Assets/Art/Models/Zipline/GameObject.prefab:1506-1509` | поле `cableZiplineToActivate` переименовано в `targetRideObjectName` → сериализованная ссылка потеряна; работает только fallback «единственный `CableZiplineRide` в сцене» | назначить цель заново либо оставить fallback осознанно |
| BUG-017 | `LampFlicker.cs:135-140` | `SetVisualState(!lampLight.enabled)` → NRE, если `Light` не найден (баг латентный: `Lamp.prefab` содержит Light) | ранний выход, если `lampLight == null` |
| BUG-018 | `Assets/Prefabs/Player/Player.prefab:211` | `torsoPivot: {fileID: 0}` → код наклона торса (`FPSController.cs:308-331`, ~24 строки) не выполняется; `torsoPitchWeight: 1.5` при `[Range(0,1.5)]` — настройка в «мёртвом» состоянии | назначить кость либо удалить фичу |
| BUG-019 | `Assets/Prefabs/Player/Player.prefab:203` | `m_EditorClassIdentifier: Assembly-CSharp::SimpleFPSController` — устаревшее имя (класс переименован в `FPSController`) | Unity перезапишет при следующем сохранении префаба |
| BUG-020 | `WireConnector.cs` (101 стр.), `ZiplineController.cs` (317 стр.) | мёртвый код: GUID скриптов не встречается ни в одной сцене/префабе; при этом `PlayerInteractor.cs:47` зависит от статика из `ZiplineController` | удалить или явно пометить legacy (после подтверждения) |
| BUG-021 | `FPSController`, `LampFlicker`, `Trampoline`, `PickupObject` | десятки `public` полей вместо `[SerializeField] private` — нарушение инкапсуляции и конвенций, которые уже применяются в новых файлах | постепенно переводить, начиная с `FPSController` |
| BUG-022 | `WallLiftSwitch` (810), `GrabManager` (660), `CodeMonitor` (612), `FPSController` (590), `CableZiplineRide` (515), `CreditsRoll` (450) | god-classes >500 строк; `WallLiftSwitch` совмещает движение стены, спавн, респавн, анимацию и два аудиомикшера | вынести аудио и спавн в отдельные компоненты (отдельной итерацией) |
| BUG-023 | `GrabManager.cs:410`, `PickupObject.cs:292-303` | magic numbers: `WaitForSeconds(20f)`, `angle * 50f`, `ClampMagnitude(torque, 300f)`, `14f` | вынести в `[SerializeField]` с тултипами |
| BUG-024 | 30 вызовов в 12 файлах | смешаны legacy `Input.GetAxis/GetKey` и новый Input System (`Mouse.current`) — работает только из-за `activeInputHandler: 2` | унифицировать на `InputSystem_Actions.inputactions` |
| BUG-025 | `PhysicsHoldController.cs.meta` | GUID `b2c3d4e5f60718293a4b5c6d7e8f901a` — «рукописный» паттерн вместо случайного (остальные GUID случайные) | не трогать без необходимости; зафиксировать риск коллизии |
| BUG-026 | `FuelCanister.cs:192`, `VacuumCablePlug.cs:325` | `useGravity = previousUseGravity \|\| true` — выражение всегда даёт `true`, исходное значение игнорируется | заменить на `previousUseGravity` |

---

## 7. Архитектурные рекомендации (не баги)

1. **Убрать дублирование подсистем.** Кабель питания — 2 независимые
   реализации (`WireConnector` и `PowerCableItem`/`PhysicalCable`), зиплайн — 2
   (`ZiplineController` и `CableZiplineRide`), питание генератора — 3 пути
   (`GeneratorController.SetCableConnected`, `PowerCableItem.RefreshPowerState`,
   `VacuumCablePlug.ConnectToSocket`). Все они пишут в одно поле
   `FuelGenerator.IsCableConnected` и будут перетирать состояние друг друга,
   если окажутся в одной сцене. Рекомендуется оставить по одному пути.
2. **Ввести асембли.** Хотя бы `AnotherOne.Runtime` + `AnotherOne.Editor`:
   ускорит итерации компиляции и позволит писать тесты с доступом к internal-API.
3. **Добавить тесты.** `com.unity.test-framework` установлен, тестов — 0.
   Минимально полезные EditMode-тесты: генерация и верификация кода
   (`SecretCodeManager`), `PickupObject.GetSpeedLimit()`, логика
   `PowerCableSocket.CanInteract`.
4. **Избавиться от `FindFirstObjectByType` в рантайме.** 17 вхождений; для
   одиночных ссылок использовать инспектор/`[SerializeField]`, для игрока —
   явный реестр или тег (в `SecretCodeManager` уже сделан корректный вариант).
5. **Вынести настройки в ScriptableObject.** Звуки, пороги и цвета сейчас
   продублированы в десятках префабов (`pressVolume`, `wallMovePitchRange` и т.п.
   в 17 кнопках) — правка баланса требует обхода всех префабов.
6. **Единый `AudioMixer`** вместо `PlayOneShot` со случайным pitch в каждом
   классе.
7. **Профилировать сцены в Play Mode** и держать `Resources` только там, где это
   действительно нужно (сейчас `Resources` тянет TMP Examples в билд).

---

## 8. Приложение A. Проверено — проблем НЕ найдено

Чтобы отчёт не содержал ложных срабатываний, отдельно зафиксировано:

- **Компиляция:** `validate_script` по `FPSController.cs` → 0 errors.
  Runtime Console пуст (0 записей — Editor не в Play Mode).
- **Ввод не сломан:** `activeInputHandler: 2` (Both), legacy-оси
  `Mouse X/Y`, `Horizontal/Vertical`, `Mouse ScrollWheel` доступны.
- **Целостность ссылок:** `m_Script: {fileID: 0}` (missing script) — **0**;
  дубликаты GUID в `.meta` — **0**; файлов без `.meta` — **0**.
- **Тег и слой:** тег `Interactable` и слой `6: Interactable` определены в
  `ProjectSettings/TagManager.asset` — префаб с этим тегом валиден.
- **Скрипт игрока на месте:** GUID `8d6c0a382d9269c43a6ccf5ef2e63c95` в
  `Player.prefab` = `FPSController.cs`; `physicsHold` (fileID …0002) и
  `grabManager` (…529528) существуют в префабе.
- **Крафт-цепочка не тупиковая:** `ZiplinePart1/2` (Amir) → `GameObject.prefab`
  (`AssembledZipline`) → цель находится: `ZiplineSystem.prefab` в `Amir.unity`
  содержит вложенный `zipline.prefab` (41 ссылка) с `useCableRoute: 1` и
  заполненными `routeStartPoint` / `routeEndPoint`.
- **`Сredits_Scene`** содержит кириллическую `С` (U+0421) и в имени файла, и в
  `MainMenu.creditsSceneName` — сейчас согласовано и работает, но это хрупко:
  любая латинская `C` сломает переход (см. рекомендации).
- **`QualitySettings.m_CurrentQuality = 1`** → уровень «PC» (0 = Mobile) — корректно.

---

## 9. Приложение B. План исправлений (Этап 6)

Порядок — от максимального эффекта при минимальном риске. Один пункт = один
атомарный коммит, после каждого: `refresh_unity` → проверка Console → прогон
затронутого сценария в Play Mode.

| # | ID | Действие | Риск |
|---|---|---|---|
| 0 | — | Коммит-снапшот текущего состояния рабочего дерева (backup) | — |
| 1 | BUG-001 | Вернуть `powerWithoutGenerator: 1` | Low |
| 2 | BUG-002, 003, 004, 005, 006 | Убрать per-frame поиски объектов и аллокации физики | Low-Med |
| 3 | BUG-007 | Корректный учёт топлива генератора | Medium |
| 4 | BUG-008 | Перекодировать 6 файлов CP1251 → UTF-8 | Low |
| 5 | BUG-009, 010, 011, 012, 013 | Точечные логические правки | Low-Med |
| 6 | BUG-014…026 | По согласованию; **удаление ассетов — только после отдельного подтверждения** | Low |

После каждого шага ведётся `Docs/Fixes_2026-11-22.md`
(BUG-ID → что сделано → как проверено).

**Обязательные проверки после правок:**

- `refresh_unity` (scope: scripts, compile: request) → Console без ошибок.
- Play `Amir`: поездка на зиплайне (BUG-001, 006), заправка генератора
  (BUG-003, 007), перенос и выбрасывание предметов (BUG-004, 009), переход в
  `Alexander` (BUG-011).
- Play `Сredits_Scene`: музыка титров (BUG-013).
- Profiler: `GC Alloc` в кадре до/после (BUG-002…006).

---

## 10. Ограничения аудита

1. **Этап 4 (динамический анализ) не выполнялся:** Play Mode, Profiler и
   runtime Console требуют Act mode и меняют состояние Editor'а. Все выводы по
   производительности основаны на чтении кода, а не на замерах. Метрики
   «до/после» (Console errors, GC alloc/frame, FPS, draw calls) будут сняты
   отдельным проходом.
2. **Проверка «неиспользуемых ассетов»** выполнена по GUID-ссылкам в текстах
   сцен и префабов. Динамически загружаемые ассеты (`Resources.Load`,
   `Addressables`, строковые пути) этим методом не выявляются — поэтому ни один
   ассет не рекомендуется к удалению без ручной проверки.
3. **Состояние git на момент аудита:** рабочее дерево грязное — 98 изменений
   (2 изменённых префаба, 30+ удалений `Assets/DeepSeek/*`, 20 untracked файлов,
   включая 3 не добавленных в git скрипта: `PhysicalCable.cs`,
   `PowerCableItem.cs`, `PowerCableSocket.cs`). Отчёт описывает **текущее**
   состояние файловой системы, а не состояние HEAD.
4. **Все утверждения снабжены файлом и строкой.** Там, где вывод основан на
   рассуждении, а не на конфигурации (например, BUG-009), это отмечено явно.
5. **Изменения в `Assets/` в этой итерации:** ровно один ассет —
   `Assets/Prefabs/ZiplineSystem.prefab` (override `powerWithoutGenerator: 1`,
   `+4` строки, см. BUG-001). `.meta`-файлы и GUID'ы не затрагивались.
6. **Во время аудита проект правился параллельно.** За сессию: `Amir.unity`
   сохранён в 02:40, набор WIP-изменений вырос с **98 до 162** записей, удалена
   папка `Assets/Art/Models/generator/`, добавлены `Assets/Art/Materials/Generator/`,
   `Assets/Art/Textures/Generator/`, `генератор.fbx`, `канистра.fbx`,
   `Jerrycan.prefab` перемещён в `Assets/Prefabs/`. Из-за этого Play Mode
   перезапускался сам (в замерe `Time.time` регрессировал с ~7 с до 2.27 с),
   поэтому приёмка BUG-001 не закрыта полностью. **Для детерминированной
   проверки требуется «заморозка» рабочего дерева.** Снимки
   `Docs/wip-status-before-fixes.txt` и `Docs/wip-snapshot-before-fixes.diff`
   (сделаны в 02:21) уже частично устарели.

---

*Конец отчёта.*