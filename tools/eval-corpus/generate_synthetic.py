"""Generates the synthetic screenshots and documents of the evaluation corpus (E01-S10).

Every image is drawn from known English and Russian text, so its OCR ground truth is exact.
Some images get photo-like distortions (rotation, perspective, blur, noise, JPEG
artefacts, low contrast) so that OCR is not judged on clean renders only.

The script writes the images, replaces the `synth-*` entries in eval/manifest.json, and
replaces the synthetic entries in eval/ocr-truth.json. The output is deterministic on
the same Pillow version and fonts. It uses fonts from the Windows fonts folder.

Usage (from the repository root):
    python tools/eval-corpus/generate_synthetic.py eval/corpus
"""

import argparse
import json
import random
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

FONTS = Path("C:/Windows/Fonts")
FONT_FILES = {
    "sans": "arial.ttf", "sans-bold": "arialbd.ttf", "serif": "times.ttf", "serif-bold": "timesbd.ttf",
    "mono": "consola.ttf", "ui": "segoeui.ttf", "ui-bold": "segoeuib.ttf", "script": "segoesc.ttf",
    "georgia": "georgia.ttf", "verdana": "verdana.ttf", "comic": "comic.ttf", "courier": "cour.ttf",
}
SEED = 55


class Page:
    """A canvas that remembers every string it draws, in reading order."""

    def __init__(self, width, height, background="white"):
        self.image = Image.new("RGB", (width, height), background)
        self.draw = ImageDraw.Draw(self.image)
        self.lines = []

    @staticmethod
    def font(name, size):
        return ImageFont.truetype(str(FONTS / FONT_FILES[name]), size)

    def text(self, x, y, value, size=24, font="sans", fill="black", anchor="la"):
        font_object = self.font(font, size)
        self.draw.text((x, y), value, font=font_object, fill=fill, anchor=anchor)
        self.lines.append(value)
        return y + round(size * 1.35)

    def row(self, y, parts, size=24, font="sans", fill="black"):
        """Several strings on one line, read left to right; parts are (x, text) or (x, text, anchor)."""
        font_object = self.font(font, size)
        for part in parts:
            x, value = part[0], part[1]
            self.draw.text((x, y), value, font=font_object, fill=fill, anchor=part[2] if len(part) > 2 else "la")
        self.lines.append(" ".join(p[1] for p in parts))
        return y + round(size * 1.35)

    def block(self, x, y, values, size=24, font="sans", fill="black", spacing=1.35):
        for value in values:
            self.draw.text((x, y), value, font=self.font(font, size), fill=fill)
            self.lines.append(value)
            y += round(size * spacing)
        return y

    def box(self, xy, fill=None, outline=None, width=1, radius=0):
        self.draw.rounded_rectangle(xy, radius=radius, fill=fill, outline=outline, width=width)

    @property
    def truth(self):
        return "\n".join(self.lines)


# Scene templates -------------------------------------------------------------------------------


def window(title, width=1000, height=640, accent="#2b579a", dark=False):
    background, foreground = ("#1e1e1e", "#e6e6e6") if dark else ("#f3f3f3", "black")
    page = Page(width, height, background)
    page.box((0, 0, width, 44), fill=accent)
    page.text(16, 10, title, size=20, font="ui", fill="white")
    page.draw.text((width - 34, 8), "×", font=Page.font("ui", 24), fill="white")
    return page, foreground


def chat(title, messages, width=720, height=1280):
    page = Page(width, height, "#e5ddd5")
    page.box((0, 0, width, 110), fill="#075e54")
    page.text(100, 38, title, size=34, font="ui-bold", fill="white")
    page.draw.ellipse((22, 28, 78, 84), fill="#cfd8dc")
    y = 150
    for side, value, time in messages:
        font = Page.font("ui", 28)
        lines = wrap(value, font, 470)
        bubble_height = 26 + 38 * len(lines) + 30
        left = 30 if side == "in" else width - 30 - 520
        page.box((left, y, left + 520, y + bubble_height), fill="white" if side == "in" else "#dcf8c6", radius=14)
        page.block(left + 20, y + 14, lines, size=28, font="ui")
        page.text(left + 500, y + bubble_height - 34, time, size=20, font="ui", fill="#7a7a7a", anchor="ra")
        y += bubble_height + 24
    return page


def wrap(value, font, width):
    words, lines, current = value.split(), [], ""
    for word in words:
        trial = f"{current} {word}".strip()
        if font.getlength(trial) <= width:
            current = trial
        else:
            lines.append(current)
            current = word
    lines.append(current)
    return lines


def paper(width=1240, height=1754, tint="white"):
    return Page(width, height, tint)


def receipt(header, items, total_label, total, footer, width=560, height=1100, font="mono"):
    page = Page(width, height, "#fbfbf6")
    y = 40
    for value in header:
        y = page.text(width // 2, y, value, size=26, font=font, anchor="ma")
    y += 10
    page.draw.line((30, y, width - 30, y), fill="black", width=2)
    y += 20
    for name, price in items:
        y = page.row(y, [(30, name), (width - 30, price, "ra")], size=24, font=font)
    y += 10
    page.draw.line((30, y, width - 30, y), fill="black", width=2)
    y += 20
    y = page.row(y, [(30, total_label), (width - 30, total, "ra")], size=30, font=font)
    y += 20
    for value in footer:
        y = page.text(width // 2, y, value, size=22, font=font, anchor="ma")
    return page


def sign(lines, background, foreground, width=1000, height=600, font="sans-bold", size=90, border=None):
    page = Page(width, height, background)
    if border:
        page.box((20, 20, width - 20, height - 20), outline=border, width=12, radius=30)
    total = len(lines) * size * 1.25
    y = (height - total) / 2
    for value in lines:
        page.text(width // 2, round(y), value, size=size, font=font, fill=foreground, anchor="ma")
        y += size * 1.25
    return page


# Distortions -----------------------------------------------------------------------------------


def distort(image, steps, rng):
    for step in steps:
        if step == "rotate":
            image = image.rotate(rng.uniform(-6, 6), resample=Image.BICUBIC, expand=True, fillcolor=(90, 90, 90))
        elif step == "perspective":
            # Pad first, so the warp never pushes text out of the frame.
            pad = round(0.1 * max(image.size))
            padded = Image.new("RGB", (image.width + 2 * pad, image.height + 2 * pad), (70, 70, 70))
            padded.paste(image, (pad, pad))
            image = padded
            w, h = image.size
            d = 0.08
            quad = [rng.uniform(0, d) * w, rng.uniform(0, d) * h, rng.uniform(0, d) * w, h - rng.uniform(0, d) * h,
                    w - rng.uniform(0, d) * w, h - rng.uniform(0, d) * h, w - rng.uniform(0, d) * w, rng.uniform(0, d) * h]
            image = image.transform((w, h), Image.QUAD, quad, resample=Image.BICUBIC, fillcolor=(70, 70, 70))
        elif step == "blur":
            image = image.filter(ImageFilter.GaussianBlur(1.6))
        elif step == "noise":
            noise = Image.effect_noise(image.size, 28).convert("RGB")
            image = Image.blend(image, noise, 0.12)
        elif step == "low-contrast":
            image = Image.blend(image, Image.new("RGB", image.size, (150, 150, 150)), 0.45)
        elif step == "shadow":
            w, h = image.size
            mask = Image.linear_gradient("L").resize((w, h)).rotate(90, expand=False)
            image = Image.composite(image, Image.blend(image, Image.new("RGB", (w, h), "black"), 0.5), mask)
        elif step == "moire":
            w, h = image.size
            lines = Image.new("RGB", (w, h), "white")
            draw = ImageDraw.Draw(lines)
            for y in range(0, h, 4):
                draw.line((0, y, w, y + w // 25), fill=(200, 200, 230))
            image = Image.blend(image, lines, 0.18)
    return image


# The corpus ------------------------------------------------------------------------------------


def scenes():
    """Yields (kind, description, tags, scripts, distortions, page)."""

    p = chat("Emma", [
        ("in", "Are we still on for dinner tonight?", "18:02"),
        ("out", "Yes! Italian place on Baker Street at 8?", "18:04"),
        ("in", "Perfect. I'll book a table for four.", "18:05"),
        ("out", "Great, see you there", "18:05"),
    ])
    yield "screenshot", "messenger chat in English about dinner plans", "chat,messenger,phone,dinner,text-latin", ["latin"], [], p

    p = chat("Мама", [
        ("in", "Ты не забыл купить хлеб?", "17:40"),
        ("out", "Купил, ещё взял молоко и яйца", "17:43"),
        ("in", "Молодец! Приезжай к семи, будут пельмени", "17:45"),
        ("out", "Хорошо, буду вовремя", "17:46"),
    ])
    yield "screenshot", "messenger chat in Russian with mum about groceries and pelmeni", "chat,messenger,phone,groceries,pelmeni,text-cyrillic", ["cyrillic"], [], p

    p, fg = window("Inbox - Mail")
    y = p.text(40, 80, "Your order has shipped", size=36, font="ui-bold", fill=fg)
    y = p.text(40, y + 10, "From: orders@example-shop.com", size=22, font="ui", fill="#555555")
    y = p.block(40, y + 20, [
        "Hi Alex,",
        "Good news: your order #48213 is on its way.",
        "Estimated delivery: Tuesday, 14 April.",
        "Track your parcel in the app at any time.",
        "Thank you for shopping with us!",
    ], size=26, font="ui", fill=fg)
    yield "screenshot", "email in English saying an order has shipped", "email,order,delivery,parcel,text-latin", ["latin"], [], p

    p, fg = window("Входящие - Почта")
    y = p.text(40, 80, "Ваш заказ отправлен", size=36, font="ui-bold", fill=fg)
    y = p.text(40, y + 10, "От: zakaz@example-shop.ru", size=22, font="ui", fill="#555555")
    y = p.block(40, y + 20, [
        "Здравствуйте, Алексей!",
        "Ваш заказ № 48213 передан в службу доставки.",
        "Ожидаемая дата доставки: вторник, 14 апреля.",
        "Отслеживать посылку можно в приложении.",
        "Спасибо за покупку!",
    ], size=26, font="ui", fill=fg)
    yield "screenshot", "email in Russian saying an order has shipped", "email,order,delivery,parcel,text-cyrillic", ["cyrillic"], [], p

    p, fg = window("Error", 760, 300, accent="#c42b1c")
    p.draw.ellipse((40, 90, 110, 160), fill="#c42b1c")
    p.draw.text((75, 125), "!", font=Page.font("sans-bold", 48), fill="white", anchor="mm")
    p.block(140, 90, ["Unable to save the file.", "The disk is full."], size=28, font="ui", fill=fg)
    p.box((600, 220, 720, 270), fill="#e1e1e1", outline="#adadad")
    p.text(660, 245, "OK", size=24, font="ui", fill=fg, anchor="mm")
    yield "screenshot", "error dialog in English: unable to save the file, disk is full", "error,dialog,disk-full,warning,text-latin", ["latin"], [], p

    p, fg = window("Ошибка", 820, 300, accent="#c42b1c")
    p.draw.ellipse((40, 90, 110, 160), fill="#c42b1c")
    p.draw.text((75, 125), "!", font=Page.font("sans-bold", 48), fill="white", anchor="mm")
    p.block(140, 90, ["Не удалось сохранить файл.", "Недостаточно места на диске."], size=28, font="ui", fill=fg)
    p.box((640, 220, 780, 270), fill="#e1e1e1", outline="#adadad")
    p.text(710, 245, "ОК", size=24, font="ui", fill=fg, anchor="mm")
    yield "screenshot", "error dialog in Russian: unable to save the file, not enough disk space", "error,dialog,disk-full,warning,text-cyrillic", ["cyrillic"], ["perspective", "moire", "blur"], p

    def settings(title, rows, dark):
        p, fg = window(title, 900, 620, accent="#444444", dark=dark)
        y = 90
        for label, on in rows:
            p.text(50, y, label, size=28, font="ui", fill=fg)
            p.box((740, y, 820, y + 36), fill="#0f6cbd" if on else "#8a8a8a", radius=18)
            p.draw.ellipse((784, y + 4, 812, y + 32) if on else (748, y + 4, 776, y + 32), fill="white")
            y += 90
        return p

    yield "screenshot", "settings screen in English with dark mode on", "settings,toggles,dark-mode,text-latin", ["latin"], [], settings(
        "Settings", [("Dark mode", True), ("Notifications", True), ("Location access", False), ("Automatic updates", True), ("Send usage data", False)], True)
    yield "screenshot", "settings screen in Russian with toggles", "settings,toggles,text-cyrillic", ["cyrillic"], [], settings(
        "Настройки", [("Тёмная тема", False), ("Уведомления", True), ("Доступ к геопозиции", False), ("Автообновление", True), ("Звук клавиш", False)], False)

    p = Page(1100, 560, "#0c0c0c")
    p.block(24, 24, [
        "PS C:\\src\\app> dotnet build",
        "  Determining projects to restore...",
        "  All projects are up-to-date for restore.",
        "  App -> C:\\src\\app\\bin\\Debug\\net10.0\\App.dll",
        "",
        "Build succeeded.",
        "    0 Warning(s)",
        "    0 Error(s)",
        "",
        "Time Elapsed 00:00:04.21",
    ], size=24, font="mono", fill="#cccccc")
    p.lines = [line for line in p.lines if line]
    yield "screenshot", "terminal window showing a successful build", "terminal,console,code,build,text-latin", ["latin"], [], p

    p, fg = window("Проводник - Фотографии", 1000, 600, accent="#3a3a3a")
    y = p.row(70, [(60, "Имя"), (560, "Дата изменения"), (820, "Размер")], size=22, font="ui-bold", fill=fg)
    for name, date, size in [("Отпуск 2025.jpg", "12.08.2025", "4,2 МБ"), ("Дача, шашлыки.jpg", "03.06.2025", "3,8 МБ"),
                             ("Кот спит.png", "21.01.2025", "1,1 МБ"), ("Новый год у бабушки.mp4", "01.01.2025", "212 МБ"),
                             ("Скан паспорта.pdf", "15.11.2024", "640 КБ"), ("Свадьба Ани и Миши", "29.09.2024", "Папка")]:
        y = p.row(y + 14, [(60, name), (560, date), (820, size)], size=24, font="ui", fill=fg)
    yield "screenshot", "file explorer in Russian listing holiday photos and videos", "file-explorer,files,folder,photos,text-cyrillic", ["cyrillic"], [], p

    p, fg = window("Calendar - Week 16", 1100, 640, accent="#0f6cbd")
    days = ["Mon 13", "Tue 14", "Wed 15", "Thu 16", "Fri 17"]
    p.row(70, [(60 + i * 205, d) for i, d in enumerate(days)], size=24, font="ui-bold", fill=fg)
    events = [(0, 140, "Team stand-up"), (1, 220, "Dentist 10:30"), (2, 140, "Design review"), (3, 320, "Lunch with Sam"), (4, 220, "Flight to Berlin")]
    for day, top, title in events:
        p.box((55 + day * 205, top, 245 + day * 205, top + 70), fill="#cfe4fa", radius=6)
        p.text(65 + day * 205, top + 20, title, size=21, font="ui", fill="black")
    yield "screenshot", "weekly calendar in English with meetings and a flight", "calendar,schedule,meetings,week,text-latin", ["latin"], [], p

    p = Page(720, 480, "#3b5f8a")
    p.text(40, 40, "Москва", size=44, font="ui-bold", fill="white")
    p.text(40, 110, "−12 °C", size=110, font="ui", fill="white")
    p.text(40, 270, "Снег, ветер 6 м/с", size=32, font="ui", fill="white")
    p.text(40, 330, "Ощущается как −19 °C", size=28, font="ui", fill="#dbe7f5")
    yield "screenshot", "weather widget in Russian: Moscow, minus 12, snow", "weather,widget,snow,winter,moscow,cold,text-cyrillic", ["cyrillic"], [], p

    p = Page(720, 480, "#5a6b78")
    p.text(40, 40, "London", size=44, font="ui-bold", fill="white")
    p.text(40, 110, "14 °C", size=110, font="ui", fill="white")
    p.text(40, 270, "Light rain, wind 20 km/h", size=32, font="ui", fill="white")
    p.text(40, 330, "Umbrella recommended", size=28, font="ui", fill="#e1e8ee")
    yield "screenshot", "weather widget in English: London, 14 degrees, light rain", "weather,widget,rain,london,text-latin", ["latin"], [], p

    p = Page(800, 900, "white")
    p.text(400, 80, "Sign in", size=48, font="ui-bold", anchor="ma")
    p.text(120, 200, "Email address", size=24, font="ui", fill="#444444")
    p.box((120, 240, 680, 300), outline="#8a8a8a", width=2, radius=6)
    p.text(120, 340, "Password", size=24, font="ui", fill="#444444")
    p.box((120, 380, 680, 440), outline="#8a8a8a", width=2, radius=6)
    p.box((120, 490, 680, 560), fill="#0f6cbd", radius=6)
    p.text(400, 525, "Continue", size=28, font="ui-bold", fill="white", anchor="mm")
    p.text(400, 610, "Forgot password?", size=24, font="ui", fill="#0f6cbd", anchor="ma")
    yield "screenshot", "sign-in form in English with email and password fields", "login,sign-in,form,password,text-latin", ["latin"], [], p

    p, fg = window("Корзина", 900, 640, accent="#e8410b")
    y = 80
    for name, price in [("Сковорода чугунная 26 см", "2 490 ₽"), ("Набор ложек, 6 шт.", "890 ₽"), ("Чайник электрический", "3 150 ₽")]:
        y = p.row(y, [(50, name), (850, price, "ra")], size=28, font="ui", fill=fg) + 20
    p.draw.line((50, y, 850, y), fill="#bbbbbb", width=2)
    y = p.row(y + 20, [(50, "Итого"), (850, "6 530 ₽", "ra")], size=32, font="ui-bold", fill=fg)
    p.box((500, y + 40, 850, y + 110), fill="#e8410b", radius=8)
    p.text(675, y + 75, "Оформить заказ", size=28, font="ui-bold", fill="white", anchor="mm")
    yield "screenshot", "online shop cart in Russian with a frying pan, spoons and a kettle", "shopping,cart,online-shop,kitchen,text-cyrillic", ["cyrillic"], [], p

    p = Page(720, 1100, "#121212")
    p.box((110, 120, 610, 620), fill="#3d2b56", radius=20)
    p.draw.ellipse((260, 270, 460, 470), outline="#9b7fd1", width=8)
    p.text(360, 680, "Midnight Train", size=40, font="ui-bold", fill="white", anchor="ma")
    p.text(360, 740, "The Northern Lines", size=28, font="ui", fill="#b3b3b3", anchor="ma")
    p.row(830, [(110, "1:42"), (610, "3:58", "ra")], size=22, font="ui", fill="#b3b3b3")
    yield "screenshot", "music player in English playing the song Midnight Train", "music,player,song,phone,text-latin", ["latin"], [], p

    p = Page(720, 1280, "#1b2838")
    p.text(360, 160, "07:15", size=150, font="ui", fill="white", anchor="ma")
    p.text(360, 360, "среда, 8 апреля", size=34, font="ui", fill="white", anchor="ma")
    p.box((40, 500, 680, 690), fill="#2f4054", radius=24)
    p.text(70, 525, "Напоминание", size=26, font="ui-bold", fill="white")
    p.text(70, 575, "День рождения мамы — купить цветы", size=28, font="ui", fill="white")
    p.text(70, 625, "сегодня, 18:00", size=24, font="ui", fill="#b6c2cf")
    yield "screenshot", "phone lock screen in Russian with a reminder about mum's birthday", "phone,lock-screen,reminder,birthday,flowers,text-cyrillic", ["cyrillic"], [], p

    p, fg = window("Budget 2026.xlsx", 1000, 520, accent="#107c41")
    y = p.row(70, [(40, "Category"), (420, "Planned"), (640, "Spent")], size=24, font="sans-bold", fill=fg)
    for row in [("Rent", "1,200", "1,200"), ("Groceries", "450", "512"), ("Transport", "120", "96"), ("Holidays", "300", "0"), ("Total", "2,070", "1,808")]:
        y = p.row(y + 10, [(40, row[0]), (420, row[1]), (640, row[2])], size=24, font="sans", fill=fg)
    yield "screenshot", "spreadsheet in English with a monthly budget table", "spreadsheet,budget,table,money,text-latin", ["latin"], [], p

    p, fg = window("Program.cs - Editor", 1000, 520, accent="#333333", dark=True)
    p.block(30, 70, [
        "using System;",
        "",
        "var cats = new[] { \"Barsik\", \"Murka\" };",
        "foreach (var cat in cats)",
        "{",
        "    Console.WriteLine($\"Feed {cat}\");",
        "}",
    ], size=26, font="mono", fill="#d4d4d4")
    p.lines = [line for line in p.lines if line]
    yield "screenshot", "code editor with a short C# program that prints cat names", "code,editor,programming,text-latin", ["latin"], [], p

    p = Page(1000, 760, "white")
    p.text(40, 30, "НОВОСТИ ГОРОДА", size=26, font="sans-bold", fill="#b71c1c")
    p.text(40, 90, "В Москве выпал первый снег", size=50, font="georgia", fill="black")
    p.text(40, 170, "Синоптики обещают похолодание до −5 °C к выходным", size=28, font="georgia", fill="#444444")
    p.box((40, 240, 960, 560), fill="#d9e2ea")
    p.block(40, 590, ["Утром жители столицы увидели первый снег.", "Коммунальные службы работают в усиленном режиме."], size=26, font="georgia")
    yield "screenshot", "Russian news page: first snow fell in Moscow", "news,article,snow,winter,moscow,text-cyrillic", ["cyrillic"], [], p

    p = Page(1000, 760, "white")
    p.text(40, 30, "CITY NEWS", size=26, font="sans-bold", fill="#0d47a1")
    p.text(40, 90, "City opens new tram line", size=50, font="georgia", fill="black")
    p.text(40, 170, "Route 9 links the station with the university", size=28, font="georgia", fill="#444444")
    p.box((40, 240, 960, 560), fill="#e0e0d8")
    p.block(40, 590, ["Trams will run every six minutes at peak times.", "Tickets cost the same as on the bus."], size=26, font="georgia")
    yield "screenshot", "English news page: city opens a new tram line", "news,article,tram,transport,text-latin", ["latin"], ["rotate", "noise"], p

    p = Page(720, 1000, "white")
    p.text(40, 40, "To do", size=48, font="ui-bold")
    y = 140
    for done, value in [(True, "Pay electricity bill"), (False, "Book train tickets"), (False, "Call the vet about Max"), (True, "Return library books"), (False, "Buy birthday present")]:
        p.box((40, y, 76, y + 36), outline="#555555", width=3, radius=6, fill="#0f6cbd" if done else None)
        p.text(100, y, value, size=30, font="ui", fill="#888888" if done else "black")
        y += 80
    yield "screenshot", "to-do list app in English with bills, train tickets and a vet call", "todo,list,tasks,phone,text-latin", ["latin"], [], p

    p = Page(720, 1000, "#fffbe6")
    p.text(40, 40, "Список покупок", size=46, font="ui-bold")
    p.block(60, 140, ["молоко 2,5%", "хлеб бородинский", "яйца — десяток", "сметана", "картошка 2 кг", "свёкла для борща"], size=32, font="ui", spacing=1.9)
    yield "screenshot", "shopping list app in Russian: milk, bread, eggs, beetroot for borscht", "shopping-list,groceries,list,borscht,text-cyrillic", ["cyrillic"], [], p

    p = Page(900, 500, "#202020")
    p.box((60, 60, 840, 440), fill="#2d2d2d", radius=16)
    p.text(450, 120, "Battery low", size=44, font="ui-bold", fill="white", anchor="ma")
    p.text(450, 200, "5% remaining", size=34, font="ui", fill="#ff6b6b", anchor="ma")
    p.text(450, 270, "Connect your charger soon.", size=28, font="ui", fill="#cccccc", anchor="ma")
    yield "screenshot", "laptop notification in English: battery low, 5 percent", "battery,notification,warning,laptop,text-latin", ["latin"], [], p

    # Documents ---------------------------------------------------------------------------------

    p = paper()
    p.text(100, 100, "INVOICE", size=72, font="sans-bold")
    p.block(100, 220, ["Northwind Studio Ltd", "12 Harbour Road, Bristol BS1 4QA"], size=28)
    p.block(780, 220, ["Invoice no. 2026-0417", "Date: 17 April 2026"], size=28)
    y = p.row(420, [(100, "Description"), (820, "Qty"), (1140, "Amount", "ra")], size=28, font="sans-bold")
    for row in [("Website design", "1", "£1,800.00"), ("Photo retouching", "24", "£360.00"), ("Hosting, 12 months", "1", "£144.00")]:
        y = p.row(y + 16, [(100, row[0]), (820, row[1]), (1140, row[2], "ra")], size=28)
    p.row(y + 60, [(700, "Total due"), (1140, "£2,304.00", "ra")], size=34, font="sans-bold")
    p.text(100, 1500, "Payment within 30 days. Thank you for your business.", size=26)
    yield "document", "English invoice from a design studio, total £2,304", "invoice,bill,payment,business,money,text-latin", ["latin"], [], p

    p = paper()
    p.text(100, 100, "СЧЁТ НА ОПЛАТУ № 117", size=60, font="sans-bold")
    p.text(100, 200, "от 17 апреля 2026 г.", size=30)
    p.block(100, 280, ["Поставщик: ООО «Северный ветер»", "ИНН 7701234567, г. Москва, ул. Тверская, д. 7"], size=28)
    y = p.row(460, [(100, "Наименование"), (820, "Кол-во"), (1140, "Сумма", "ra")], size=28, font="sans-bold")
    for row in [("Бумага офисная А4", "10", "3 500,00"), ("Картридж для принтера", "2", "7 980,00"), ("Доставка", "1", "500,00")]:
        y = p.row(y + 16, [(100, row[0]), (820, row[1]), (1140, row[2], "ra")], size=28)
    p.row(y + 60, [(700, "Итого к оплате:"), (1140, "11 980,00 руб.", "ra")], size=32, font="sans-bold")
    p.text(100, 1500, "Оплатить в течение 5 банковских дней.", size=26)
    yield "document", "Russian invoice for office paper and printer cartridges", "invoice,bill,payment,business,money,text-cyrillic", ["cyrillic"], ["shadow", "rotate"], p

    p = receipt(["ООО «ПРОДУКТЫ 24»", "КАССОВЫЙ ЧЕК", "Приход"],
                [("Молоко 3,2% 1л", "89.90"), ("Хлеб ржаной", "54.00"), ("Яйцо С1 10шт", "119.50"), ("Сметана 20%", "76.30"), ("Пакет", "9.00")],
                "ИТОГ", "=348.70", ["Спасибо за покупку!", "17.04.2026 19:42"])
    yield "document", "Russian grocery store receipt for milk, bread, eggs and sour cream", "receipt,shop,groceries,money,text-cyrillic", ["cyrillic"], ["rotate", "noise", "blur"], p

    p = receipt(["THE DAILY GRIND", "Coffee & Bakery", "Table 4"],
                [("Flat white", "3.40"), ("Cappuccino", "3.20"), ("Almond croissant", "2.90"), ("Carrot cake", "3.75")],
                "TOTAL", "13.25", ["Card payment approved", "Thank you, come again!"])
    yield "document", "English coffee shop receipt for coffees, a croissant and carrot cake", "receipt,coffee,cafe,cake,money,text-latin", ["latin"], [], p

    p = paper(tint="#fffdf7")
    p.block(780, 120, ["14 Elm Street", "Oxford OX2 6DP", "3 March 2026"], size=28, font="serif")
    p.block(120, 360, [
        "Dear Ms Turner,",
        "",
        "Thank you for your letter of 24 February.",
        "I am pleased to confirm my place on the photography",
        "course starting on 6 April. I enclose the booking form",
        "and a cheque for the deposit.",
        "",
        "Yours sincerely,",
        "Daniel Hughes",
    ], size=32, font="serif", spacing=1.6)
    p.lines = [line for line in p.lines if line]
    yield "document", "formal English letter confirming a place on a photography course", "letter,correspondence,formal,photography,text-latin", ["latin"], [], p

    p = paper(tint="#fffdf7")
    p.block(780, 120, ["г. Казань", "5 марта 2026 г."], size=28, font="serif")
    p.block(120, 320, [
        "Дорогая Ольга Петровна!",
        "",
        "Спасибо за тёплое письмо и фотографии с дачи.",
        "У нас всё хорошо: дети здоровы, Мурзик растолстел,",
        "а весной мы наконец поедем в Петербург.",
        "Передавайте привет Николаю Ивановичу.",
        "",
        "С уважением,",
        "Ирина",
    ], size=32, font="serif", spacing=1.6)
    p.lines = [line for line in p.lines if line]
    yield "document", "personal letter in Russian thanking for dacha photos", "letter,correspondence,personal,dacha,text-cyrillic", ["cyrillic"], ["perspective", "shadow"], p

    p = paper(tint="#fff8ee")
    p.text(100, 100, "Fluffy Pancakes", size=64, font="georgia")
    p.text(100, 200, "Serves 4 · 20 minutes", size=28, font="georgia", fill="#7a5c3a")
    p.text(100, 300, "Ingredients", size=36, font="sans-bold")
    p.block(130, 360, ["200 g plain flour", "2 tsp baking powder", "1 tbsp sugar", "2 eggs", "300 ml milk", "Butter for frying"], size=30, font="georgia")
    p.text(100, 640, "Method", size=36, font="sans-bold")
    p.block(130, 700, ["1. Mix the flour, baking powder and sugar.", "2. Whisk in the eggs and milk until smooth.",
                       "3. Fry spoonfuls in butter, 2 minutes per side."], size=30, font="georgia")
    yield "document", "English recipe for fluffy pancakes", "recipe,pancakes,cooking,food,text-latin", ["latin"], [], p

    p = paper(tint="#fff8ee")
    p.text(100, 100, "Борщ по-домашнему", size=64, font="georgia")
    p.text(100, 200, "На 6 порций · 2 часа", size=28, font="georgia", fill="#7a5c3a")
    p.text(100, 300, "Ингредиенты", size=36, font="sans-bold")
    p.block(130, 360, ["говядина на кости — 500 г", "свёкла — 2 шт.", "капуста — 300 г", "картофель — 3 шт.",
                       "морковь и лук — по 1 шт.", "томатная паста — 2 ст. л."], size=30, font="georgia")
    p.text(100, 640, "Приготовление", size=36, font="sans-bold")
    p.block(130, 700, ["1. Сварите бульон из говядины.", "2. Обжарьте свёклу, морковь и лук с томатной пастой.",
                       "3. Добавьте картофель, капусту и зажарку.", "4. Подавайте со сметаной и зеленью."], size=30, font="georgia")
    yield "document", "Russian recipe for homemade borscht", "recipe,borscht,soup,cooking,food,text-cyrillic", ["cyrillic"], ["perspective", "low-contrast"], p

    p = Page(1400, 600, "#f4f0ff")
    p.box((0, 0, 1400, 90), fill="#d0021b")
    p.text(40, 22, "ЭЛЕКТРОННЫЙ БИЛЕТ", size=40, font="sans-bold", fill="white")
    p.row(140, [(60, "Москва"), (700, "Санкт-Петербург")], size=52, font="sans-bold")
    p.row(230, [(60, "Отправление: 18.04.2026 07:00"), (700, "Прибытие: 18.04.2026 10:45")], size=28)
    p.row(310, [(60, "Поезд 752А «Сапсан»"), (700, "Вагон 5, место 23")], size=28)
    p.text(60, 400, "Пассажир: ИВАНОВ И.И.", size=28)
    yield "document", "Russian e-ticket for the Sapsan train from Moscow to Saint Petersburg", "ticket,train,travel,moscow,saint-petersburg,text-cyrillic", ["cyrillic"], [], p

    p = Page(1400, 560, "white")
    p.box((0, 0, 1400, 100), fill="#003a70")
    p.text(40, 26, "BOARDING PASS", size=44, font="sans-bold", fill="white")
    p.row(150, [(60, "LHR"), (500, "JFK")], size=80, font="sans-bold")
    p.row(270, [(60, "London Heathrow"), (500, "New York JFK")], size=26)
    p.row(340, [(60, "Flight BA117"), (500, "Gate B42"), (900, "Seat 23A")], size=30, font="sans-bold")
    p.row(410, [(60, "Boarding 10:25"), (500, "Departs 11:05")], size=28)
    yield "document", "English boarding pass for a flight from London to New York", "boarding-pass,flight,airplane,travel,ticket,text-latin", ["latin"], ["rotate"], p

    p = Page(1050, 600, "white")
    p.box((0, 0, 24, 600), fill="#00796b")
    p.text(80, 80, "Anna Smirnova", size=48, font="sans-bold")
    p.text(80, 150, "Анна Смирнова", size=40, font="sans")
    p.text(80, 230, "Wedding photographer · Свадебный фотограф", size=28, fill="#00796b")
    p.block(80, 380, ["+7 900 123-45-67", "anna@example.photo"], size=28)
    yield "document", "bilingual English-Russian business card of a wedding photographer", "business-card,photographer,wedding,text-latin,text-cyrillic", ["latin", "cyrillic"], [], p

    p = paper()
    p.text(100, 100, "Project Meeting Agenda", size=56, font="sans-bold")
    p.text(100, 190, "Thursday 16 April, 14:00, Room 3B", size=30)
    p.block(130, 300, ["1. Welcome and apologies", "2. Progress on the photo search feature", "3. Russian language support",
                       "4. Release date and testing", "5. Any other business"], size=32, spacing=1.8)
    yield "document", "English meeting agenda about a photo search project", "agenda,meeting,work,office,text-latin", ["latin"], [], p

    p = paper(tint="#f7f2e7")
    p.text(620, 140, "А. С. Пушкин", size=36, font="serif", anchor="ma")
    p.text(620, 220, "К ***", size=44, font="serif-bold", anchor="ma")
    p.block(300, 340, ["Я помню чудное мгновенье:", "Передо мной явилась ты,", "Как мимолётное виденье,", "Как гений чистой красоты."],
            size=40, font="serif", spacing=1.6)
    yield "document", "Pushkin poem in Russian, first stanza of 'I remember a wonderful moment'", "poem,poetry,pushkin,literature,text-cyrillic", ["cyrillic"], [], p

    p = paper(tint="#f7f2e7")
    p.text(620, 140, "William Shakespeare", size=36, font="serif", anchor="ma")
    p.text(620, 220, "Sonnet 18", size=44, font="serif-bold", anchor="ma")
    p.block(240, 340, ["Shall I compare thee to a summer's day?", "Thou art more lovely and more temperate:",
                       "Rough winds do shake the darling buds of May,", "And summer's lease hath all too short a date;"],
            size=38, font="serif", spacing=1.6)
    yield "document", "Shakespeare's Sonnet 18 in English, first four lines", "poem,poetry,shakespeare,literature,text-latin", ["latin"], ["low-contrast", "blur"], p

    p = paper(tint="#fdfdfd")
    p.text(620, 120, "ОБЪЯВЛЕНИЕ", size=80, font="sans-bold", anchor="ma")
    p.block(120, 300, ["Уважаемые жильцы!", "В связи с плановыми работами", "с 20 по 30 апреля", "будет отключена горячая вода.",
                       "Приносим извинения за неудобства.", "Управляющая компания «Уют»"], size=44, font="sans", spacing=1.7)
    yield "document", "Russian notice to residents about hot water being switched off", "notice,announcement,hot-water,apartment,text-cyrillic", ["cyrillic"], ["perspective", "shadow", "noise"], p

    p = paper(tint="white")
    p.text(620, 100, "LOST CAT", size=110, font="sans-bold", fill="#c62828", anchor="ma")
    p.box((320, 280, 920, 820), fill="#d7ccc8")
    p.draw.text((620, 550), "PHOTO", font=Page.font("sans", 40), fill="#8d6e63", anchor="mm")
    p.block(160, 880, ["Ginger tabby called Biscuit", "Last seen on Mill Lane, 2 April", "Very friendly, wears a blue collar",
                       "Please call 07700 900123"], size=44, spacing=1.6)
    yield "document", "English 'Lost cat' poster for a ginger tabby", "poster,lost-cat,cat,pet,notice,text-latin", ["latin"], ["rotate", "shadow"], p

    p = sign(["ОСТОРОЖНО,", "ЗЛАЯ СОБАКА"], "#ffd600", "#b71c1c", border="#b71c1c")
    yield "document", "yellow sign in Russian: beware of the dog", "sign,warning,dog,danger,text-cyrillic", ["cyrillic"], ["perspective", "noise"], p

    p = sign(["CAUTION", "WET FLOOR"], "#ffeb3b", "black", border="black", size=100)
    yield "document", "yellow 'Caution, wet floor' sign in English", "sign,warning,caution,wet-floor,text-latin", ["latin"], [], p

    p = paper(1000, 1400, "#2e2a24")
    p.text(500, 80, "Кафе «Самовар»", size=60, font="georgia", fill="#f5deb3", anchor="ma")
    p.text(500, 180, "МЕНЮ", size=44, font="sans-bold", fill="#f5deb3", anchor="ma")
    y = 290
    for name, price in [("Борщ со сметаной", "320 ₽"), ("Пельмени домашние", "390 ₽"), ("Блины с икрой", "550 ₽"),
                        ("Сырники", "280 ₽"), ("Чай из самовара", "150 ₽"), ("Морс клюквенный", "120 ₽")]:
        y = p.row(y, [(100, name), (900, price, "ra")], size=36, font="georgia", fill="white") + 34
    yield "document", "Russian cafe menu with borscht, pelmeni, blini and tea", "menu,restaurant,cafe,food,borscht,pelmeni,blini,text-cyrillic", ["cyrillic"], [], p

    p = paper(1000, 1400, "#fafafa")
    p.text(500, 80, "Harbour Bistro", size=60, font="georgia", anchor="ma")
    p.text(500, 180, "Today's Menu", size=40, font="sans-bold", anchor="ma")
    y = 290
    for name, price in [("Tomato soup", "£5.50"), ("Fish and chips", "£14.00"), ("Margherita pizza", "£11.50"),
                        ("Caesar salad", "£9.00"), ("Sticky toffee pudding", "£6.50"), ("Pot of tea", "£3.00")]:
        y = p.row(y, [(100, name), (900, price, "ra")], size=36, font="georgia") + 34
    yield "document", "English bistro menu with soup, fish and chips and pizza", "menu,restaurant,food,pizza,text-latin", ["latin"], ["perspective", "blur"], p

    p = paper(1754, 1240, "#fffaf0")
    p.box((40, 40, 1714, 1200), outline="#b8860b", width=10)
    p.text(877, 160, "Certificate of Completion", size=80, font="georgia", fill="#5d4037", anchor="ma")
    p.text(877, 340, "This certifies that", size=36, font="georgia", anchor="ma")
    p.text(877, 430, "Maria Lopez", size=72, font="script", fill="#1a237e", anchor="ma")
    p.text(877, 600, "has completed the course Introduction to Photography", size=36, font="georgia", anchor="ma")
    p.text(877, 680, "on 28 March 2026", size=36, font="georgia", anchor="ma")
    yield "document", "English certificate of completion for a photography course", "certificate,diploma,award,photography,text-latin", ["latin"], [], p

    p = paper(1754, 1240, "#fff8f0")
    p.box((40, 40, 1714, 1200), outline="#c62828", width=10)
    p.text(877, 160, "ГРАМОТА", size=110, font="serif-bold", fill="#c62828", anchor="ma")
    p.text(877, 360, "награждается", size=40, font="serif", anchor="ma")
    p.text(877, 450, "Петров Михаил", size=70, font="script", fill="#1a237e", anchor="ma")
    p.text(877, 620, "за первое место в школьном турнире по шахматам", size=40, font="serif", anchor="ma")
    p.text(877, 700, "Директор школы № 12", size=36, font="serif", anchor="ma")
    yield "document", "Russian school certificate for first place in a chess tournament", "certificate,diploma,award,chess,school,text-cyrillic", ["cyrillic"], ["rotate", "low-contrast"], p

    p = Page(900, 900, "#fff59d")
    p.block(80, 120, ["Don't forget to", "feed the cat!", "Back at 6 pm", "Love, J."], size=60, font="script", fill="#1a237e", spacing=1.7)
    yield "document", "handwritten-style sticky note in English: don't forget to feed the cat", "note,sticky-note,handwriting,cat,text-latin", ["latin"], ["rotate", "shadow"], p

    p = Page(900, 900, "#c8e6c9")
    p.block(80, 120, ["Купить молоко", "и хлеб.", "Позвонить бабушке!", "Целую, Лена"], size=60, font="script", fill="#263238", spacing=1.7)
    yield "document", "handwritten-style note in Russian: buy milk and bread, call grandma", "note,sticky-note,handwriting,groceries,text-cyrillic", ["cyrillic"], ["perspective"], p

    p = sign(["ул. Ленина", "15"], "#1565c0", "white", width=1200, height=500, size=110, border="white")
    yield "document", "blue Russian street name sign 'ul. Lenina 15'", "street-sign,sign,address,text-cyrillic", ["cyrillic"], ["perspective", "noise", "blur"], p

    p = sign(["EXIT 24", "Downtown", "Airport →"], "#00695c", "white", width=1200, height=700, size=100, border="white")
    yield "document", "green motorway exit sign: Exit 24, Downtown, Airport", "road-sign,sign,motorway,airport,text-latin", ["latin"], ["rotate", "noise"], p

    p = Page(1200, 800, "#f5f5f5")
    for i, (name, price) in enumerate([("Яблоки Гренни", "129 ₽/кг"), ("Мандарины", "189 ₽/кг"), ("Огурцы", "99 ₽/кг"), ("Бананы", "115 ₽/кг")]):
        x, y = 60 + (i % 2) * 560, 60 + (i // 2) * 360
        p.box((x, y, x + 520, y + 300), fill="#fff176", outline="#f9a825", width=6)
        p.text(x + 30, y + 40, name, size=44, font="sans-bold")
        p.text(x + 30, y + 150, price, size=72, font="sans-bold", fill="#c62828")
    yield "document", "Russian supermarket price tags for apples, tangerines, cucumbers and bananas", "price-tags,shop,fruit,apples,market,text-cyrillic", ["cyrillic"], ["perspective"], p

    p = paper()
    p.row(100, [(100, "Instructions"), (680, "Инструкция")], size=56, font="sans-bold")
    y = 240
    for en, ru in [("1. Fill the kettle with water.", "1. Налейте воду в чайник."), ("2. Close the lid.", "2. Закройте крышку."),
                   ("3. Press the switch.", "3. Нажмите выключатель."), ("4. Wait for the click.", "4. Дождитесь щелчка.")]:
        y = p.row(y, [(100, en), (680, ru)], size=32) + 30
    yield "document", "bilingual English-Russian kettle instructions", "instructions,manual,kettle,text-latin,text-cyrillic", ["latin", "cyrillic"], [], p

    p = paper(1400, 1000)
    p.text(700, 60, "Расписание уроков, 5 «Б» класс", size=48, font="sans-bold", anchor="ma")
    y = p.row(180, [(80, "Понедельник"), (520, "Вторник"), (960, "Среда")], size=34, font="sans-bold")
    for row in [("Математика", "Русский язык", "История"), ("Литература", "Английский язык", "Математика"),
                ("Физкультура", "Биология", "Музыка"), ("География", "Рисование", "Технология")]:
        y = p.row(y + 30, [(80, row[0]), (520, row[1]), (960, row[2])], size=32)
    yield "document", "Russian school timetable for grade 5", "timetable,schedule,school,lessons,text-cyrillic", ["cyrillic"], ["shadow"], p

    p = Page(1200, 900, "#263238")
    p.text(60, 50, "Bus 42 · Central Station", size=48, font="sans-bold", fill="#ffca28")
    y = p.row(170, [(60, "Weekdays"), (600, "Weekends")], size=34, font="sans-bold", fill="white")
    for weekday, weekend in [("06:15  06:45  07:15", "07:30  08:30"), ("07:35  07:55  08:15", "09:30  10:30"),
                             ("then every 20 min", "then hourly"), ("Last bus 23:40", "Last bus 22:30")]:
        y = p.row(y + 24, [(60, weekday), (600, weekend)], size=32, font="mono", fill="white")
    yield "document", "English bus timetable for bus 42 to Central Station", "timetable,bus,schedule,transport,text-latin", ["latin"], [], p

    p = paper(tint="#f0f0f0")
    p.text(100, 100, "TENANCY AGREEMENT", size=48, font="serif-bold")
    p.block(100, 200, ["4. Pets", "The tenant may keep one cat or one small dog at the", "property with the written consent of the landlord.",
                       "Any damage caused by the animal shall be repaired", "at the tenant's expense."], size=32, font="courier", spacing=1.6)
    yield "document", "photocopied English tenancy agreement clause about pets", "contract,agreement,legal,pets,text-latin", ["latin"], ["low-contrast", "noise", "rotate"], p

    p = paper(tint="#f0f0f0")
    p.text(100, 100, "ПРОТОКОЛ № 3", size=48, font="serif-bold")
    p.text(100, 180, "общего собрания собственников", size=32, font="serif")
    p.block(100, 280, ["Повестка дня:", "1. Ремонт подъезда.", "2. Установка детской площадки.", "3. Выбор председателя совета дома.",
                       "Решили: работы начать в мае 2026 года."], size=32, font="serif", spacing=1.6)
    yield "document", "Russian minutes of a residents' meeting", "minutes,meeting,protocol,apartment,text-cyrillic", ["cyrillic"], ["blur", "low-contrast"], p

    p = Page(1600, 900, "#0d47a1")
    p.text(100, 100, "Quarterly results", size=80, font="ui-bold", fill="white")
    p.block(120, 280, ["Revenue up 18%", "12,400 new customers", "Mobile app launched in March"], size=48, font="ui", fill="white", spacing=1.8)
    p.box((1050, 300, 1130, 700), fill="#90caf9")
    p.box((1170, 200, 1250, 700), fill="#64b5f6")
    p.box((1290, 120, 1370, 700), fill="#ffffff")
    yield "screenshot", "English presentation slide with quarterly results and a bar chart", "slide,presentation,chart,business,text-latin", ["latin"], ["perspective", "moire"], p

    p = Page(1600, 900, "#b71c1c")
    p.text(100, 100, "Итоги года", size=80, font="ui-bold", fill="white")
    p.block(120, 280, ["Выручка выросла на 25%", "Открыли 3 новых магазина", "Команда выросла до 140 человек"], size=48, font="ui", fill="white", spacing=1.8)
    yield "screenshot", "Russian presentation slide with the year's results", "slide,presentation,business,text-cyrillic", ["cyrillic"], [], p

    p = Page(1000, 640, "white")
    p.box((30, 30, 970, 610), outline="#37474f", width=6, radius=30)
    p.text(500, 90, "Guest Wi-Fi", size=60, font="sans-bold", anchor="ma")
    p.row(260, [(120, "Network:"), (440, "GuestNet")], size=44)
    p.row(360, [(120, "Password:"), (440, "sunflower42")], size=44, font="sans")
    p.text(500, 500, "Enjoy your stay!", size=36, font="script", anchor="ma")
    yield "document", "English guest Wi-Fi card with network name and password", "wifi,card,password,hotel,text-latin", ["latin"], [], p

    p = Page(1000, 1400, "#fce4ec")
    p.text(500, 200, "С днём", size=120, font="script", fill="#ad1457", anchor="ma")
    p.text(500, 360, "рождения!", size=120, font="script", fill="#ad1457", anchor="ma")
    p.block(140, 700, ["Дорогая бабушка,", "желаем здоровья, тепла", "и много радостных дней!", "Твои внуки"], size=52, font="georgia", spacing=1.6)
    yield "document", "Russian birthday card for grandma", "card,birthday,greeting,celebration,text-cyrillic", ["cyrillic"], [], p


def write_json_lines(path, items):
    with open(path, "w", encoding="utf-8", newline="\n") as stream:
        stream.write("[\n" + ",\n".join("  " + json.dumps(i, ensure_ascii=False) for i in items) + "\n]\n")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("out", type=Path, help="corpus folder, e.g. eval/corpus")
    parser.add_argument("--manifest", type=Path, default=Path("eval/manifest.json"))
    parser.add_argument("--ocr-truth", type=Path, default=Path("eval/ocr-truth.json"))
    args = parser.parse_args()

    rng = random.Random(SEED)
    (args.out / "synthetic").mkdir(parents=True, exist_ok=True)
    entries, truths = [], []
    for number, (kind, description, tags, scripts, distortions, page) in enumerate(scenes(), start=1):
        item_id = f"synth-{number:03}"
        image = distort(page.image, distortions, rng)
        extension = "jpg" if distortions else "png"
        file = f"synthetic/{item_id}.{extension}"
        if extension == "jpg":
            image.save(args.out / file, quality=72 if "noise" in distortions else 85)
        else:
            image.save(args.out / file, optimize=True)
        entries.append({
            "id": item_id, "kind": kind, "file": file, "description": description, "tags": tags.split(","),
            "source": {"synthetic": "tools/eval-corpus/generate_synthetic.py", "licence": "CC0 1.0"},
            "changes": "Generated; " + (", ".join(distortions) + " applied." if distortions else "clean render."),
        })
        truths.append({"id": item_id, "scripts": scripts, "origin": "synthetic", "text": page.truth})

    manifest = json.loads(args.manifest.read_text(encoding="utf-8")) if args.manifest.exists() else []
    manifest = [i for i in manifest if not i["id"].startswith("synth-")] + entries
    write_json_lines(args.manifest, manifest)

    truth = json.loads(args.ocr_truth.read_text(encoding="utf-8")) if args.ocr_truth.exists() else []
    truth = [t for t in truth if t.get("origin") != "synthetic"] + truths
    write_json_lines(args.ocr_truth, truth)
    print(f"{len(entries)} synthetic items")


if __name__ == "__main__":
    sys.exit(main())
