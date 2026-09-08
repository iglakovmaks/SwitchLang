import unittest

from switchlang_core import can_convert, convert_layout


class LayoutConversionTests(unittest.TestCase):
    def test_russian_to_english(self):
        self.assertEqual(convert_layout("руддщ", "ru"), "hello")

    def test_english_to_russian(self):
        self.assertEqual(convert_layout("ghbdtn", "en"), "привет")

    def test_case_and_punctuation_are_preserved(self):
        self.assertEqual(convert_layout("Ghbdtn, Vfr!", "en"), "Приветб Мак!")

    def test_comma_period_and_slash_follow_physical_keys(self):
        self.assertEqual(convert_layout(",./", "en"), "бю.")
        self.assertEqual(convert_layout("бю.", "ru"), ",./")
        self.assertEqual(convert_layout("<>?", "en"), "БЮ?")
        self.assertEqual(convert_layout("БЮ,", "ru"), "<>,")

    def test_question_mark_is_layout_neutral(self):
        self.assertEqual(convert_layout("?", "en"), "?")
        self.assertEqual(convert_layout("?", "ru"), "?")

    def test_spaces_and_numbers_are_preserved(self):
        self.assertEqual(convert_layout("руддщ 123", "ru"), "hello 123")

    def test_conversion_is_reversible_for_typical_words(self):
        russian_source = "руддщ"
        english_source = "ghbdtn"
        self.assertEqual(convert_layout(convert_layout(russian_source, "ru"), "en"), russian_source)
        self.assertEqual(convert_layout(convert_layout(english_source, "en"), "ru"), english_source)

    def test_unknown_layout_is_safe(self):
        original = "ghbdtn"
        self.assertEqual(convert_layout(original, "de"), original)
        self.assertFalse(can_convert(original, "de"))


if __name__ == "__main__":
    unittest.main()
