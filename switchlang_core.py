"""Pure text conversion logic for SwitchLang.

The conversion is based on physical key positions in the standard Russian and
US keyboard layouts. It does not translate words and does not need a network
connection.
"""

from __future__ import annotations


# The strings describe the same physical keys in the two layouts. Keeping the
# mapping position-based also handles punctuation typed with Shift.
EN_UNSHIFTED = "`1234567890-=qwertyuiop[]\\asdfghjkl;'zxcvbnm,./"
RU_UNSHIFTED = "ё1234567890-=йцукенгшщзхъ\\фывапролджэячсмитьбю."
EN_SHIFTED = '~!@#$%^&*()_+QWERTYUIOP{}|ASDFGHJKL:"ZXCVBNM<>?'
RU_SHIFTED = 'Ё!"№;%:?*()_+ЙЦУКЕНГШЩЗХЪ/ФЫВАПРОЛДЖЭЯЧСМИТЬБЮ,'


def _make_mapping(source: str, target: str, *, preserve_source: set[str] | None = None) -> dict[int, str]:
    if len(source) != len(target):
        raise ValueError("Keyboard layout rows must have equal lengths")
    # str.translate expects integer Unicode code points as dictionary keys.
    preserve_source = preserve_source or set()
    return {
        ord(source_character): target_character
        for source_character, target_character in zip(source, target)
        if source_character not in preserve_source
    }


def _make_directional_mappings() -> tuple[dict[int, str], dict[int, str]]:
    en_to_ru: dict[int, str] = {}
    ru_to_en: dict[int, str] = {}

    punctuation = set(",./<>?")
    for source, target in (
        (EN_UNSHIFTED, RU_UNSHIFTED),
        (EN_SHIFTED, RU_SHIFTED),
    ):
        for source_character, target_character in zip(source, target):
            if source_character == "?" or target_character == "?":
                continue
            if (
                source_character.isalpha()
                or target_character.isalpha()
                or source_character in punctuation
                or target_character in punctuation
            ):
                en_to_ru[ord(source_character)] = target_character
                ru_to_en[ord(target_character)] = source_character
    return en_to_ru, ru_to_en


EN_TO_RU, RU_TO_EN = _make_directional_mappings()


def normalize_layout(layout: str | None) -> str | None:
    """Return ``ru`` or ``en`` for a supported layout, otherwise ``None``."""

    if not layout:
        return None
    value = layout.strip().lower()
    if value.startswith("ru") or "russian" in value or "рус" in value:
        return "ru"
    if value.startswith("en") or "english" in value or "abc" in value:
        return "en"
    return None


def convert_layout(text: str, current_layout: str | None) -> str:
    """Convert text from the currently active layout to the opposite one.

    ``current_layout`` is the layout that was active while the text was typed.
    The caller is responsible for determining it from the operating system.
    Unknown layouts are intentionally left unchanged.
    """

    layout = normalize_layout(current_layout)
    if layout == "ru":
        mapping = RU_TO_EN
    elif layout == "en":
        mapping = EN_TO_RU
    else:
        return text
    return text.translate(mapping)


def can_convert(text: str, current_layout: str | None) -> bool:
    """Return whether text contains at least one character in the source map."""

    layout = normalize_layout(current_layout)
    mapping = RU_TO_EN if layout == "ru" else EN_TO_RU if layout == "en" else {}
    return any(ord(character) in mapping for character in text)
