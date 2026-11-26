#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Генератор схем для презентации «Архитектура.pdf» (блок «Программирование»).

Рисует три схемы, требуемые критериями:
  1. architecture_graph.png — архитектурный граф модулей;
  2. algorithm_flowchart.png — блок-схема алгоритма с ветвлениями;
  3. data_schema.png — схема данных (что где хранится, кто читает/пишет).

Схемы рисуются на PIL — без внешних зависимостей, чтобы их можно было
пересобрать на любой машине (python3 generate_schemas.py).
"""

import os
from PIL import Image, ImageDraw, ImageFont

OUT = os.path.dirname(os.path.abspath(__file__))

BG = (17, 20, 26)
FG = (232, 236, 240)
MUTED = (150, 160, 172)
ACCENT = (79, 222, 252)
ACCENT2 = (255, 196, 61)
GREEN = (86, 214, 122)
RED = (235, 96, 96)
BOX = (32, 38, 48)
BOX2 = (26, 31, 40)
LINE = (86, 98, 115)

FONT_B = "/System/Library/Fonts/Supplemental/Arial Bold.ttf"
FONT_R = "/System/Library/Fonts/Supplemental/Arial.ttf"


def font(size, bold=False):
    try:
        return ImageFont.truetype(FONT_B if bold else FONT_R, size)
    except Exception:
        return ImageFont.load_default()


def rounded(draw, box, fill=BOX, outline=LINE, width=2, radius=10):
    draw.rounded_rectangle(box, radius=radius, fill=fill, outline=outline, width=width)


def center_text(draw, box, text, f, fill=FG, spacing=4):
    x0, y0, x1, y1 = box
    lines = text.split("\n")
    heights = []
    for line in lines:
        bb = draw.textbbox((0, 0), line, font=f)
        heights.append(bb[3] - bb[1])
    total = sum(heights) + spacing * (len(lines) - 1)
    y = (y0 + y1) / 2 - total / 2
    for line, h in zip(lines, heights):
        bb = draw.textbbox((0, 0), line, font=f)
        w = bb[2] - bb[0]
        draw.text(((x0 + x1) / 2 - w / 2, y), line, font=f, fill=fill)
        y += h + spacing


def arrow(draw, p0, p1, color=LINE, width=2, head=9):
    import math
    draw.line([p0, p1], fill=color, width=width)
    ang = math.atan2(p1[1] - p0[1], p1[0] - p0[0])
    for a in (ang + 2.6, ang - 2.6):
        draw.line([p1, (p1[0] + head * math.cos(a), p1[1] + head * math.sin(a))],
                  fill=color, width=width)


def elbow(draw, p0, p1, color=LINE, width=2, vertical_first=True, label=None, lf=None):
    """Г-образный коннектор: удобен для схем слоёв."""
    if vertical_first:
        mid = (p0[0], p1[1])
        draw.line([p0, mid], fill=color, width=width)
        arrow(draw, mid, p1, color, width)
    else:
        mid = (p1[0], p0[1])
        draw.line([p0, mid], fill=color, width=width)
        arrow(draw, mid, p1, color, width)

    if label:
        lx = (p0[0] + p1[0]) / 2
        ly = (p0[1] + p1[1]) / 2 - 9
        bb = draw.textbbox((0, 0), label, font=lf or font(13))
        w = bb[2] - bb[0]
        draw.rectangle([lx - w / 2 - 4, ly - 3, lx + w / 2 + 4, ly + 15], fill=BG)
        draw.text((lx - w / 2, ly), label, font=lf or font(13), fill=MUTED)


# ============================ 1. Архитектурный граф ============================

def architecture_graph():
    W, H = 1600, 900
    img = Image.new("RGB", (W, H), BG)
    d = ImageDraw.Draw(img)

    f_title = font(30, True)
    f_layer = font(17, True)
    f_box = font(17, True)
    f_small = font(13)
    f_tiny = font(12)

    d.text((36, 22), "Архитектурный граф модулей", font=f_title, fill=FG)
    d.text((36, 60), "Данные (JSON) отделены от логики; каждый модуль имеет одну ответственность",
           font=f_small, fill=MUTED)

    layers = [
        ("ДАННЫЕ (Assets/StreamingAssets)", ACCENT, 110),
        ("МОДУЛИ ДАННЫХ", ACCENT, 225),
        ("ЯДРО ЦИКЛА", ACCENT2, 340),
        ("МЕХАНИКИ", GREEN, 490),
        ("ПРЕДСТАВЛЕНИЕ / UI", ACCENT, 700),
    ]

    for name, color, y in layers:
        d.rounded_rectangle([36, y, 1564, y + 28], radius=6, fill=(26, 32, 42), outline=color, width=1)
        d.text((50, y + 6), name, font=f_layer, fill=color)

    def box(x, y, w, h, title, sub, color=MUTED, fill=BOX, tsize=17):
        rounded(d, [x, y, x + w, y + h], fill=fill, outline=color, width=2)
        # Заголовок — сверху, подпись — под ним (иначе текст налезает).
        tb = d.textbbox((0, 0), title, font=font(tsize, True))
        d.text((x + (w - (tb[2] - tb[0])) / 2, y + 9), title, font=font(tsize, True), fill=FG)
        if sub:
            lines = sub.split("\n")
            yy = y + 9 + (tb[3] - tb[1]) + 8
            for line in lines:
                lb = d.textbbox((0, 0), line, font=f_tiny)
                d.text((x + (w - (lb[2] - lb[0])) / 2, yy), line, font=f_tiny, fill=MUTED)
                yy += 17

    # --- Слой данных ---
    box(90, 148, 250, 62, "props_config.json", "исходный набор: 6 предметов", ACCENT)
    box(380, 148, 250, 62, "props_config.extended.json", "изменённый набор: 7", ACCENT)
    box(690, 148, 290, 62, "SecretCodeManager", "код двери (PlayerPrefs)", ACCENT)
    box(1030, 148, 280, 62, "PlayerHealth", "текущее HP", ACCENT)

    # --- Слой модулей данных ---
    box(90, 263, 250, 62, "PropsConfig", "формат: PropEntry, Prefab", ACCENT)
    box(380, 263, 250, 62, "PropPrefab[]", "реестр: имя -> префаб", ACCENT)

    # --- Ядро цикла ---
    box(120, 378, 300, 70, "PropsSpawner", "загрузка -> валидация -> Instantiate\nF2: переключение набора", ACCENT2, (38, 33, 24))
    box(500, 378, 300, 70, "PlayerInteractor", "луч -> поиск цели -> действие\nклавиша E", ACCENT2, (38, 33, 24))
    box(880, 378, 300, 70, "FPSController", "движение, обзор, прыжок\nWASD / мышь", ACCENT2, (38, 33, 24))
    box(1230, 378, 290, 70, "IInteractable", "контракт: CanInteract / Interact\nSetFocused", ACCENT2, (38, 33, 24))

    # --- Механики ---
    box(90, 528, 240, 62, "PickupObject", "поднять / бросить", GREEN)
    box(370, 528, 240, 62, "GeneratorController", "заправить генератор", GREEN)
    box(650, 528, 240, 62, "WallLiftSwitch (x15)", "стены и двери", GREEN)
    box(930, 528, 240, 62, "ZiplinePartCraft", "сборка зиплайна", GREEN)
    box(1210, 528, 240, 62, "CodeMonitor", "ввод кода мышью", GREEN)

    # --- UI ---
    box(380, 738, 250, 62, "GrabManager / InventoryUI", "инвентарь, предметы", ACCENT)
    box(670, 738, 250, 62, "HealthBarUI / DeathScreen", "HP, «ВЫ УМЕРЛИ»", ACCENT)
    box(960, 738, 250, 62, "CodeMonitor", "ввод кода", ACCENT)

    # --- Связи ---
    elbow(d, (215, 210), (215, 263), ACCENT)
    elbow(d, (505, 210), (505, 263), ACCENT)
    elbow(d, (835, 210), (835, 320), ACCENT, label="создаёт", lf=f_tiny)
    elbow(d, (1115, 210), (1115, 320), ACCENT, label="изменяет", lf=f_tiny)

    elbow(d, (215, 325), (215, 378), ACCENT, label="читает", lf=f_tiny)
    elbow(d, (505, 325), (505, 378), ACCENT, label="читает", lf=f_tiny)

    elbow(d, (270, 413), (500, 413), ACCENT2)
    elbow(d, (800, 413), (880, 413), ACCENT2)
    elbow(d, (1180, 413), (1230, 413), ACCENT2)

    for x in (210, 490, 770, 1050, 1330):
        elbow(d, (x, 448), (x, 528), GREEN)

    elbow(d, (650, 590), (650, 738), ACCENT, vertical_first=False)
    elbow(d, (890, 413), (795, 738), ACCENT, vertical_first=False)
    elbow(d, (1115, 590), (1085, 738), ACCENT, vertical_first=False)

    # легенда
    d.text((36, H - 42), "Синий — данные и представление   ·   Жёлтый — ядро центрального цикла   ·   "
                        "Зелёный — игровые механики", font=f_small, fill=MUTED)

    img.save(os.path.join(OUT, "architecture_graph.png"))
    print("architecture_graph.png")


# ============================ 2. Блок-схема ============================

def algorithm_flowchart():
    W, H = 1500, 1000
    img = Image.new("RGB", (W, H), BG)
    d = ImageDraw.Draw(img)

    f_title = font(30, True)
    f_box = font(16, True)
    f_small = font(13)

    d.text((36, 22), "Блок-схема алгоритма: центральный игровой цикл", font=f_title, fill=FG)
    d.text((36, 60), "Ромбы — ветвления; красные ветки — контролируемая реакция на ошибочные данные",
           font=f_small, fill=MUTED)

    def rect(x, y, w, h, text, color=MUTED, fill=BOX, f=None, radius=10):
        rounded(d, [x, y, x + w, y + h], fill=fill, outline=color, width=2, radius=radius)
        center_text(d, [x + 8, y + 4, x + w - 8, y + h - 4], text, f or font(16, True), FG)

    def diamond(cx, cy, w, h, text, color=ACCENT2):
        pts = [(cx, cy - h / 2), (cx + w / 2, cy), (cx, cy + h / 2), (cx - w / 2, cy)]
        d.polygon(pts, fill=(38, 33, 24), outline=color)
        d.line(pts + [pts[0]], fill=color, width=2)
        center_text(d, [cx - w / 2 + 14, cy - h / 2 + 6, cx + w / 2 - 14, cy + h / 2 - 6], text, font(14, True), FG)

    cx = 430

    rect(230, 100, 400, 46, "СТАРТ: загрузка сцены Amir", ACCENT, (26, 34, 44))
    arrow(d, (430, 146), (430, 176))

    rect(230, 176, 400, 52, "PropsSpawner.Awake()\nчтение props_config.json", ACCENT)
    arrow(d, (430, 228), (430, 256))

    diamond(cx, 316, 420, 120, "Файл данных\nпрочитан?", ACCENT2)
    arrow(d, (430, 256), (430, 256))

    # ветвь ошибки
    d.line([(640, 316), (900, 316)], fill=RED, width=2)
    arrow(d, (900, 316), (900, 286), RED, 2)
    rect(760, 232, 330, 52, "Лог: «данные повреждены»\nпредметы не создаются", RED, (44, 26, 30))
    d.line([(900, 284), (900, 276)], fill=RED, width=2)
    arrow(d, (900, 276), (900, 286), RED, 2)

    arrow(d, (430, 376), (430, 404))

    rect(150, 404, 560, 52, "Sanitize(): проверка каждой записи", ACCENT)
    arrow(d, (430, 456), (430, 484))

    diamond(cx, 544, 460, 120, "Запись валидна?\n(есть prefab, он в реестре,\nнет дубля, координаты != NaN)", ACCENT2)

    # ветвь пропуска
    d.line([(660, 544), (940, 544)], fill=RED, width=2)
    arrow(d, (940, 544), (940, 514), RED, 2)
    rect(800, 460, 340, 52, "Лог: «запись пропущена»\nостальные записи создаются", RED, (44, 26, 30))

    arrow(d, (430, 604), (430, 632))
    rect(150, 632, 560, 52, "Instantiate() + Physics.SyncTransforms()\nобнуление скоростей, Rigidbody.Sleep()", ACCENT)
    arrow(d, (430, 684), (430, 712))

    diamond(cx, 772, 460, 120, "Игрок нажал E\n(взаимодействие)?", ACCENT2)
    arrow(d, (430, 772), (430, 832))

    rect(150, 832, 560, 52, "PlayerInteractor: луч -> цель\nвыполнить действие (взять/нажать)", GREEN, (26, 40, 30))
    arrow(d, (430, 884), (430, 912))

    rect(150, 912, 560, 44, "Состояние изменилось -> новый цикл", ACCENT2, (38, 33, 24))
    arrow(d, (710, 934), (1170, 934), ACCENT2, 2)
    d.line([(1170, 934), (1170, 780)], fill=ACCENT2, width=2)
    arrow(d, (1170, 780), (670, 780), ACCENT2, 2)
    d.text((900, 944), "возврат к циклу", font=f_small, fill=MUTED)

    # F2
    rect(1170, 190, 280, 52, "Нажата F2", ACCENT, (26, 34, 44))
    arrow(d, (1310, 242), (1310, 290), ACCENT, 2)
    rect(1150, 290, 320, 58, "Rebuild(extended.json)\n6 -> 7 предметов", ACCENT, (26, 34, 44))
    d.line([(1310, 348), (1310, 372)], fill=ACCENT, width=2)
    arrow(d, (1310, 372), (470, 372), ACCENT, 2)
    d.line([(470, 372), (470, 404)], fill=ACCENT, width=2)
    arrow(d, (470, 404), (430, 404), ACCENT, 2)

    # ветка "нет нажатия" — влево
    d.line([(200, 772), (90, 772)], fill=MUTED, width=2)
    arrow(d, (90, 772), (90, 560), MUTED, 2)
    d.text((96, 760), "нет: ждём", font=f_small, fill=MUTED)

    img.save(os.path.join(OUT, "algorithm_flowchart.png"))
    print("algorithm_flowchart.png")


# ============================ 3. Схема данных ============================

def data_schema():
    W, H = 1500, 860
    img = Image.new("RGB", (W, H), BG)
    d = ImageDraw.Draw(img)

    f_title = font(30, True)
    f_box = font(16, True)
    f_small = font(13)
    f_mono = font(14, True)

    d.text((36, 22), "Схема данных: где что хранится, кто читает и пишет", font=f_title, fill=FG)
    d.text((36, 60), "Источник истины по составу комнаты — файл данных; код его только интерпретирует",
           font=f_small, fill=MUTED)

    def box(x, y, w, h, title, rows, color=MUTED, fill=BOX):
        rounded(d, [x, y, x + w, y + h], fill=fill, outline=color, width=2)
        d.text((x + 14, y + 10), title, font=f_box, fill=color)
        yy = y + 38
        for row in rows:
            d.text((x + 14, yy), row, font=f_small, fill=FG)
            yy += 21

    # Источники данных
    box(50, 110, 420, 190, "ИСТОЧНИКИ (только данные)",
        [
            "props_config.json",
            "  title: string",
            "  props[]: { prefab, x, y, z,",
            "              rotX, rotY, rotZ }",
            "",
            "props_config.extended.json",
            "  тот же формат + 1 запись",
        ], ACCENT, (26, 34, 44))

    box(50, 330, 420, 170, "СОСТОЯНИЕ В ПАМЯТИ",
        [
            "PropsConfig / PropEntry",
            "  — только описание формата",
            "",
            "PropPrefab[]  — реестр",
            "  имя из JSON -> префаб",
        ], ACCENT)

    box(50, 530, 420, 170, "СОСТОЯНИЕ ИГРОКА",
        [
            "PlayerHealth.currentHealth",
            "SecretCodeManager.Code",
            "GrabManager.inventory[]",
            "PhysicsHoldController.Held",
        ], GREEN, (26, 40, 30))

    # Потребители
    box(560, 110, 420, 200, "ЧТО ЧИТАЕТ",
        [
            "PropsSpawner.LoadConfig()",
            "  читает файл -> JsonUtility",
            "  -> Sanitize() -> Instantiate",
            "",
            "PropsSpawner.Rebuild()",
            "  пересоздаёт объекты",
        ], ACCENT2, (38, 33, 24))

    box(560, 350, 420, 190, "ЧТО ПИШЕТ",
        [
            "PropsSpawner: создаёт/уничтожает",
            "  экземпляры предметов",
            "",
            "PlayerHealth: currentHealth,",
            "  событие OnHealthChanged",
            "",
            "GrabManager: состав инвентаря",
        ], ACCENT2, (38, 33, 24))

    box(560, 580, 420, 180, "КОНТРОЛЬ ЦЕЛОСТНОСТИ",
        [
            "Sanitize() отбрасывает запись, если:",
            "  • нет поля prefab",
            "  • префаб не найден в реестре",
            "  • дубликат prefab",
            "  • координаты NaN",
            "Состояние при этом не разрушается:",
            "  остальные предметы создаются.",
        ], RED, (44, 26, 30))

    # Результат
    box(1060, 110, 390, 200, "РЕЗУЛЬТАТ В ИГРЕ",
        [
            "Jerrycan   (1.94, 1.03, 11.98)",
            "Banana     (9.00, 0.13, 10.00)",
            "Zipline_1  (-0.97, 2.47, -1.03)",
            "Ball       (-2.00, 1.25, 6.00)",
            "Zipline_0  (13.11, 4.00, 3.14)",
            "Trampoline (7.50, 0.00, -1.50)",
        ], GREEN, (26, 40, 30))

    box(1060, 350, 390, 200, "РАСШИРЕНИЕ ЧЕРЕЗ ДАННЫЕ",
        [
            "Добавить в props[]:",
            '{ "prefab": "Vase",',
            '  "x": 5.0, "y": 1.2, "z": 6.0,',
            '  "rotX": 0, "rotY": 0, "rotZ": 0 }',
            "",
            "Код не меняется. В билде — F2.",
        ], ACCENT, (26, 34, 44))

    box(1060, 590, 390, 170, "ПОТРЕБИТЕЛИ ДАННЫХ",
        [
            "PickupObject  — поднять/бросить",
            "GeneratorController — заправить",
            "WallLiftSwitch — нажать",
            "CodeMonitor — сверять код",
        ], MUTED)

    # стрелки
    arrow(d, (470, 200), (560, 190), ACCENT2)
    arrow(d, (470, 415), (560, 400), ACCENT2)
    arrow(d, (470, 615), (560, 640), ACCENT2)
    arrow(d, (980, 200), (1060, 200), GREEN)
    arrow(d, (980, 440), (1060, 440), ACCENT)
    arrow(d, (980, 650), (1060, 660), MUTED)

    d.text((36, H - 40), "Разделение данных и логики: логика не содержит ни одной координаты и ни одного имени предмета",
           font=f_small, fill=MUTED)

    img.save(os.path.join(OUT, "data_schema.png"))
    print("data_schema.png")


if __name__ == "__main__":
    architecture_graph()
    algorithm_flowchart()
    data_schema()
    print("OK: schemas ->", OUT)
