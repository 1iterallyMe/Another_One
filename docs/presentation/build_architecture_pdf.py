#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Сборка презентации «Архитектура.pdf» по официальному шаблону НТО.

Шаблон: https://docs.google.com/presentation/d/1VshtzVhQx90ElvMylRF5WvXAAMfYCVcz0QApWrtoYr8/edit
Порядок слайдов НЕ МЕНЯЕТСЯ (требование регламента):
  1. Титул
  2. Общее положение
  3. Выбор уровня реализации
  4. Gameplay Loop
  5. Демонстрация механики
  6. Демонстрация работы механики
  7. Демонстрация вводных механики
  8. Демонстрация N (+ масштабируемость модулей и внешняя настройка)
  9. Видео-отчёт

Запуск:
  python3 build_architecture_pdf.py
Результат:
  Another_One_54266_Архитектура.pptx и .pdf (через LibreOffice)
"""

import os
import subprocess
import sys

from pptx import Presentation
from pptx.dml.color import RGBColor
from pptx.enum.text import PP_ALIGN
from pptx.util import Inches, Pt

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
SCHEMAS = os.path.join(ROOT, "docs", "schemas")

TEMPLATE_PPTX = "/tmp/tmpl/tmpl.pptx"

OUT_PPTX = os.path.join(HERE, "Another_One_54266_Архитектура.pptx")
OUT_PDF = os.path.join(HERE, "Another_One_54266_Архитектура.pdf")

ACCENT = RGBColor(0x4F, 0xDE, 0xFC)
TEXT = RGBColor(0xE6, 0xE6, 0xE6)
MUTED = RGBColor(0xA8, 0xB2, 0xBE)
ORANGE = RGBColor(0xFF, 0xC4, 0x3D)
GREEN = RGBColor(0x56, 0xD6, 0x7A)
PANEL = RGBColor(0x1E, 0x24, 0x2C)

GITHUB_URL = "https://github.com/1iterallyMe/Another_One"
VIDEO_URL = "https://drive.google.com/drive/folders/1Y7NNo3TI36tQvV_y_r6DEoGsyR4OzP-U"


def set_text(shape, lines, size=16, bold=False, color=TEXT, space_after=4):
    tf = shape.text_frame
    tf.word_wrap = True

    # Удаляем ВСЕ абзацы, включая первый: в первом могут остаться старые run'ы
    # (иначе заголовок дублируется).
    for p in list(tf.paragraphs):
        p._p.getparent().remove(p._p)

    for i, item in enumerate(lines):
        if isinstance(item, tuple):
            text, opts = item
        else:
            text, opts = item, {}

        p = tf.add_paragraph()
        run = p.add_run()
        run.text = text
        run.font.size = Pt(opts.get("size", size))
        run.font.bold = opts.get("bold", bold)
        run.font.color.rgb = opts.get("color", color)
        run.font.name = "Arial"
        p.space_after = Pt(opts.get("space_after", space_after))
    return shape


def add_textbox(slide, x, y, w, h, lines, size=16, bold=False, color=TEXT, fill=None, space_after=4):
    box = slide.shapes.add_textbox(Inches(x), Inches(y), Inches(w), Inches(h))
    if fill is not None:
        box.fill.solid()
        box.fill.fore_color.rgb = fill
        box.line.color.rgb = RGBColor(0x3A, 0x42, 0x50)
        box.line.width = Pt(0.75)
    else:
        box.fill.background()
        box.line.fill.background()

    set_text(box, lines, size=size, bold=bold, color=color, space_after=space_after)
    return box


def add_picture(slide, path, x, y, w=None, h=None):
    if not os.path.exists(path):
        print("WARN: нет файла", path)
        return None
    kwargs = {}
    if w is not None:
        kwargs["width"] = Inches(w)
    if h is not None:
        kwargs["height"] = Inches(h)
    pic = slide.shapes.add_picture(path, Inches(x), Inches(y), **kwargs)
    # Не даём картинке вылезти за нижний край слайда (10 x 5.625 дюйма).
    max_bottom = Inches(5.45)
    bottom = pic.top + pic.height
    if bottom > max_bottom:
        scale = (max_bottom - pic.top) / pic.height
        pic.width = int(pic.width * scale)
        pic.height = int(pic.height * scale)
    return pic


def replace_shape(slide, shape_id, lines, size=16, bold=False, color=TEXT):
    for sh in slide.shapes:
        if sh.shape_id == shape_id:
            return set_text(sh, lines, size=size, bold=bold, color=color)
    return None


def delete_shape(shape):
    shape._element.getparent().remove(shape._element)


def delete_shapes(slide, ids):
    """Удаляет фигуры шаблона по их shape_id."""
    for sh in list(slide.shapes):
        if sh.shape_id in ids:
            delete_shape(sh)


def set_title(slide, shape_id, text, size=24):
    """Ставит заголовок слайда в одну строку по всей ширине."""
    for sh in slide.shapes:
        if sh.shape_id == shape_id:
            set_text(sh, [(text, {"size": size, "bold": True, "color": ACCENT})])
            sh.left = Inches(0.59)
            sh.top = Inches(0.5)
            sh.width = Inches(8.9)
            sh.height = Inches(0.45)
            return sh
    return None


def caption(slide, x, y, w, text, size=13):
    """Подпись под заголовком слайда."""
    return add_textbox(slide, x, y, w, 0.32, [(text, {"size": size})])


def build():
    if not os.path.exists(TEMPLATE_PPTX):
        print("Шаблон не найден:", TEMPLATE_PPTX)
        sys.exit(1)

    prs = Presentation(TEMPLATE_PPTX)

    # ---------- Слайд 1 — титул ----------
    s = prs.slides[0]
    replace_shape(s, 56, [
        ("Блок программирования", {"size": 40, "color": ACCENT}),
        ("«Another One»", {"size": 40, "color": ACCENT}),
        ("Unity 6000.3.10f1 · URP 17.3 · Input System 1.18", {"size": 24}),
        ("НТО 2026/2027 · II этап · профиль «Разработка компьютерных игр»", {"size": 16, "color": MUTED}),
        ("ТалантID команды: 54266", {"size": 16, "color": MUTED}),
    ])

    # ---------- Слайд 2 — общее положение ----------
    s = prs.slides[1]
    replace_shape(s, 64, [
        ("Презентация заполнена строго по шаблону, порядок слайдов не изменён.",
         {"size": 15}),
        ("Дизайн сохранён, текст и изображения читаемы; на слайде не более двух "
         "динамических графических элементов.", {"size": 15}),
        ("Внутри слайдов 4–8: архитектурный граф, блок-схема алгоритма, схема данных, "
         "ответственность и входы/выходы модулей, места настройки, структура "
         "конфигурации с заполненным примером, обработка ошибочных данных, "
         "инструкция расширения через данные с исходным и изменённым вариантом JSON.",
         {"size": 15}),
        ("Движок и версия указаны на титульном слайде: Unity 6000.3.10f1 (URP 17.3, "
         "Input System 1.18). Сборка — для Windows.", {"size": 15}),
        ("Управление: WASD — движение, мышь — обзор, Space — прыжок, "
         "Left Shift — бег, Left Ctrl — присед, E — взаимодействие, "
         "F — использовать/бросить, Esc — освободить курсор.", {"size": 15}),
    ])

    # ---------- Слайд 3 — уровень реализации ----------
    s = prs.slides[2]
    replace_shape(s, 71, [
        ("Выбранный уровень: 3. Продвинутый (высший).",
         {"size": 18, "bold": True, "color": ACCENT}),
        ("Начальный: полный центральный цикл, архитектурный граф, блок-схема, "
         "схема данных — выполнено (слайды 4–6).", {"size": 15}),
        ("Средний: декомпозиция, отделение данных от логики, ответственность и "
         "входы/выходы модулей, типовой сценарий, места настройки, конфигурация "
         "с заполненным примером, контролируемая реакция на ошибочные данные — "
         "выполнено (слайды 4–7).", {"size": 15}),
        ("Продвинутый: независимость модулей и расширение через данные — новый "
         "предмет существующего типа добавлен ТОЛЬКО правкой JSON, без изменения "
         "кода (слайд 8).", {"size": 15}),
        ("Слайды о масштабируемости модулей и внешней настройке добавлены — "
         "в соответствии с требованием продвинутого уровня.", {"size": 15}),
    ])

    # ---------- Слайд 4 — Gameplay Loop ----------
    s = prs.slides[3]
    for sh in list(s.shapes):
        if sh.shape_id == 78:
            sh.left = Inches(0.59)
            sh.top = Inches(0.52)
            sh.width = Inches(5.58)
            sh.height = Inches(0.4)
        elif sh.shape_id == 79:
            sh.left = Inches(0.59)
            sh.top = Inches(0.92)
        elif sh.shape_id == 77:
            delete_shape(sh)
    add_picture(s, os.path.join(SCHEMAS, "architecture_graph.png"), 0.35, 1.10, w=9.3)

    # ---------- Слайд 5 — демонстрация механики ----------
    s = prs.slides[4]
    delete_shapes(s, {86, 87})
    set_title(s, 84, "Демонстрация механики")
    caption(s, 0.56, 1.0, 8.9,
            "Игрок играет: «взять предмет → применить по назначению → изменить состояние мира».")

    add_textbox(s, 0.56, 1.38, 4.35, 4.0, [
        ("Центральный цикл замкнут:", {"size": 14, "bold": True, "color": ACCENT}),
        ("1. Действие игрока — навести прицел на предмет, кнопку или сокет "
         "и нажать E.", {"size": 12}),
        ("2. Изменение состояния — предмет в руке, топливо в генераторе, кабель "
         "в сокете, деталь зиплайна присоединена.", {"size": 12}),
        ("3. Видимый результат — питание включено, стена уехала, проход открыт, "
         "горит индикатор генератора.", {"size": 12}),
        ("4. Новое действие — открывшиеся возможности меняют следующий выбор "
         "(например, запитать монитор кода и открыть дверь).", {"size": 12}),
        ("", {"size": 5}),
        ("Типичный сценарий (5 модулей):", {"size": 13, "bold": True, "color": ORANGE}),
        ("FPSController (движение и обзор) → PlayerInteractor (луч от камеры, "
         "выбор цели) → "
         "PickupObject / GeneratorController / WallLiftSwitch / CodeMonitor "
         "(действие) → PlayerHealth, SecretCodeManager, PropsSpawner (состояние) → "
         "HealthBarUI (результат).", {"size": 11}),
    ], fill=PANEL)

    add_textbox(s, 5.08, 1.38, 4.35, 4.0, [
        ("Ответственность и входы/выходы модулей:", {"size": 14, "bold": True, "color": ACCENT}),
        ("props_config.json — вход: нет; выход: «какие предметы и где».", {"size": 11}),
        ("PropsConfig — вход: нет; выход: типы PropsConfig / PropEntry / Prefab "
         "(только описание формата).", {"size": 11}),
        ("PropsSpawner — вход: файл + реестр префабов; выход: экземпляры предметов "
         "в сцене, лог, переключение набора по F2.", {"size": 11}),
        ("PlayerInteractor — вход: луч; выход: выбранная цель и выполненное действие.",
         {"size": 11}),
        ("IInteractable — контракт: CanInteract / Interact / SetFocused; реализуют "
         "PickupObject, GeneratorController, WallLiftSwitch, CodeMonitor.", {"size": 11}),
        ("FPSController — вход: движение/обзор/прыжок; выход: скорость и положение "
         "игрока, анимация.", {"size": 11}),
        ("PickupObject — вход: игрок, физика; выход: предмет в руках / брошен.", {"size": 11}),
        ("CodeMonitor — вход: цифры кода; выход: проверка и открытие двери.", {"size": 11}),
    ], fill=PANEL, space_after=1)

    # ---------- Слайд 6 — демонстрация работы механики ----------
    s = prs.slides[5]
    delete_shapes(s, {94, 95})
    set_title(s, 92, "Демонстрация работы механики")
    caption(s, 0.56, 1.0, 8.9,
            "Трагедия в 4 актах: как предмет из данных становится частью игрового мира")

    add_picture(s, os.path.join(SCHEMAS, "algorithm_flowchart.png"), 0.45, 1.4, w=5.25)
    add_textbox(s, 5.9, 1.4, 3.55, 4.0, [
        ("Блок-схема: ветвления и отказоустойчивость",
         {"size": 13, "bold": True, "color": ACCENT}),
        ("«Файл данных прочитан?» — нет → лог «данные повреждены», предметы не "
         "создаются, игра продолжается.", {"size": 10}),
        ("«Запись валидна?» — нет → лог «запись пропущена», остальные записи "
         "создаются.", {"size": 10}),
        ("«Игрок нажал E?» — нет → ждём следующего кадра.", {"size": 10}),
        ("", {"size": 4}),
        ("Места настройки:", {"size": 12, "bold": True, "color": ORANGE}),
        ("• Данные: Assets/StreamingAssets/props_config*.json", {"size": 10}),
        ("• Реестр префабов: массив prefabs на PropsSpawner", {"size": 10}),
        ("• Параметры игрока: FPSController (скорость, прыжок, присед)", {"size": 10}),
        ("• Правила механик: GeneratorController (бак, расход), "
         "SecretCodeManager (длина кода)", {"size": 10}),
        ("• Интерфейс: HealthBarUI, DeathScreen, CodeMonitor (размеры панелей, подписи)", {"size": 10}),
    ], fill=PANEL, space_after=1)

    # ---------- Слайд 7 — вводные механики ----------
    s = prs.slides[6]
    delete_shapes(s, {102, 103, 104})
    set_title(s, 100, "Демонстрация вводных механики")
    caption(s, 0.56, 1.0, 8.9,
            "Схема данных: где что хранится, кто читает и кто пишет.")
    add_picture(s, os.path.join(SCHEMAS, "data_schema.png"), 0.4, 1.38, w=5.45)
    add_textbox(s, 6.05, 1.38, 3.4, 4.0, [
        ("Структура конфигурации:", {"size": 13, "bold": True, "color": ORANGE}),
        ('{ "title": string,', {"size": 11}),
        ('  "props": [ { "prefab": string,', {"size": 11}),
        ('                "x": float, "y": float, "z": float,', {"size": 11}),
        ('                "rotX": float, "rotY": float,', {"size": 11}),
        ('                "rotZ": float } ] }', {"size": 11}),
        ("", {"size": 4}),
        ("Заполненный пример (props_config.json):", {"size": 13, "bold": True, "color": ORANGE}),
        ('{ "prefab": "Jerrycan", "x": 1.94, "y": 1.03,', {"size": 11}),
        ('  "z": 11.98, "rotX": 0, "rotY": 0, "rotZ": 0 }', {"size": 11}),
        ("", {"size": 4}),
        ("Кто читает и пишет:", {"size": 13, "bold": True, "color": ORANGE}),
        ("PropsSpawner.LoadConfig() читает файл; Sanitize() отбрасывает негодные "
         "записи; Rebuild() создаёт и уничтожает предметы. Логика не содержит ни "
         "одной координаты и ни одного имени предмета.", {"size": 11}),
    ], fill=PANEL)

    # ---------- Слайд 8 — демонстрация N ----------
    s = prs.slides[7]
    set_title(s, 109, "Демонстрация N: расширение через данные", size=22)
    caption(s, 0.56, 1.0, 8.9,
            "Уровень «Продвинутый»: новый элемент добавляется ТОЛЬКО правкой данных.")
    add_textbox(s, 0.5, 1.4, 4.5, 4.0, [
        ("Инструкция «как добавить элемент данными»",
         {"size": 13, "bold": True, "color": ACCENT}),
        ("1. Открыть Assets/StreamingAssets/props_config.json.", {"size": 11}),
        ("2. Дописать в массив props новый объект: имя существующего префаба + "
         "координаты + поворот.", {"size": 11}),
        ("3. Сохранить файл и запустить игру — предмет появляется в комнате.",
         {"size": 11}),
        ("4. Для демонстрации в билде: F2 переключает наборы 6 ⇄ 7 предметов.",
         {"size": 11}),
        ("Код при этом не меняется.", {"size": 11, "bold": True, "color": GREEN}),
        ("", {"size": 4}),
        ("Исходный вариант данных (6 предметов):", {"size": 12, "bold": True, "color": ORANGE}),
        ('{ "prefab": "Trampoline", "x": 7.5, "y": 0.0,', {"size": 10}),
        ('  "z": -1.5, "rotX": 0, "rotY": 0, "rotZ": 0 }', {"size": 10}),
        ("", {"size": 4}),
        ("Изменённый вариант данных (7 предметов):", {"size": 12, "bold": True, "color": ORANGE}),
        ('{ "prefab": "Vase", "x": 5.0, "y": 1.2, "z": 6.0,', {"size": 10}),
        ('  "rotX": 0, "rotY": 0, "rotZ": 0 }', {"size": 10}),
        ("(добавлена одна строка данных — Vase)", {"size": 10, "color": MUTED}),
    ], fill=PANEL)

    add_textbox(s, 5.2, 1.4, 4.35, 1.95, [
        ("Масштабируемость модулей", {"size": 13, "bold": True, "color": ACCENT}),
        ("Модули независимы: PropsSpawner ничего не знает про PickupObject, "
         "CodeMonitor — про генератор. Замена одного модуля не ломает остальные: "
         "каждый отвечает за свою зону и общается через контракт IInteractable "
         "и файлы данных.", {"size": 11}),
        ("Новый тип предмета = новый префаб в реестре + запись в JSON.", {"size": 11}),
        ("Новая механика = новый класс IInteractable + запись в JSON.", {"size": 11}),
    ], fill=PANEL, space_after=1)

    add_textbox(s, 5.2, 3.45, 4.35, 1.95, [
        ("Внешняя настройка (без правки кода)", {"size": 13, "bold": True, "color": ACCENT}),
        ("• Состав комнаты — props_config.json", {"size": 11}),
        ("• Позиции и повороты предметов — props_config.json", {"size": 11}),
        ("• Скорость, прыжок, присед — FPSController (инспектор)", {"size": 11}),
        ("• Бак и расход топлива — GeneratorController (инспектор)", {"size": 11}),
        ("• Длина кода двери — SecretCodeManager (инспектор)", {"size": 11}),
        ("• Подписи панелей интерфейса — HealthBarUI / CodeMonitor (инспектор)", {"size": 11}),
        ("Файлы данных меняются без пересборки кода: игра читает их при старте.",
         {"size": 10, "color": MUTED}),
    ], fill=PANEL, space_after=1)

    # ---------- Слайд 9 — видео-отчёт ----------
    s = prs.slides[8]
    replace_shape(s, 117, [
        ("Ссылка на Github", {"size": 18, "bold": True, "color": ACCENT}),
        (GITHUB_URL, {"size": 14}),
        ("", {"size": 6}),
        ("Ссылка на видео-демонстрацию", {"size": 18, "bold": True, "color": ACCENT}),
        (VIDEO_URL, {"size": 14}),
    ])
    for sh in s.shapes:
        if sh.shape_id == 117:
            ps = sh.text_frame.paragraphs
            if len(ps) >= 2:
                for r in ps[1].runs:
                    r.hyperlink.address = GITHUB_URL
            if len(ps) >= 5:
                for r in ps[4].runs:
                    r.hyperlink.address = VIDEO_URL

    prs.save(OUT_PPTX)
    print("PPTX:", OUT_PPTX)
    return OUT_PPTX


def to_pdf(pptx_path):
    soffice = "/Applications/LibreOffice.app/Contents/MacOS/soffice"
    outdir = os.path.dirname(pptx_path)
    cmd = [soffice, "--headless", "--convert-to", "pdf", "--outdir", outdir, pptx_path]
    print("Запуск:", " ".join(cmd))
    res = subprocess.run(cmd, capture_output=True, text=True, timeout=300)
    print(res.stdout.strip())
    if res.stderr.strip():
        print("stderr:", res.stderr.strip()[:800])

    produced = os.path.join(outdir, os.path.splitext(os.path.basename(pptx_path))[0] + ".pdf")
    if not os.path.exists(produced):
        print("PDF не создан! LibreOffice не выдал файл:", produced)
        sys.exit(1)

    if os.path.abspath(produced) != os.path.abspath(OUT_PDF):
        if os.path.exists(OUT_PDF):
            os.remove(OUT_PDF)
        os.replace(produced, OUT_PDF)

    print("PDF:", OUT_PDF, os.path.getsize(OUT_PDF), "байт")


if __name__ == "__main__":
    pptx = build()
    to_pdf(pptx)
