using System;
using System.Linq;
using System.Text;
using DictionaryImporter.Sources.Common.Helper;

namespace DictionaryImporter.Validation
{
    public class EncodingAwareValidation
    {
        public static void Run()
        {
            // Set console encoding to UTF-8
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;

            Console.WriteLine("=========================================");
            Console.WriteLine("ENCODING-AWARE VALIDATION (UTF-8)");
            Console.WriteLine("=========================================\n");

            ValidateWithEncodingCheck();
        }

        private static void ValidateWithEncodingCheck()
        {
            Console.WriteLine("1. RAW BYTE VALIDATION (Checking actual bytes in memory)");
            Console.WriteLine("=========================================================\n");

            var chineseText = "苹果apple";

            // Test 1: Check bytes in memory
            Console.WriteLine($"Input string: '{chineseText}'");
            Console.WriteLine($"Length: {chineseText.Length} chars");
            Console.WriteLine($"Bytes (UTF-8): {string.Join(" ", Encoding.UTF8.GetBytes(chineseText).Select(b => b.ToString("X2")))}");
            Console.WriteLine($"Contains Chinese chars: {ContainsChinese(chineseText)}");
            Console.WriteLine();

            // Test 2: Normalize and check
            var sources = new[] { "ENG_CHN", "CENTURY21", "ENG_OXFORD", "ENG_COLLINS", "GUT_WEBSTER" };

            foreach (var source in sources)
            {
                var normalized = TextNormalizer.NormalizeWordPreservingLanguage(chineseText, source);

                Console.WriteLine($"{source,-15}:");
                Console.WriteLine($"  Input bytes:  {GetHexBytes(chineseText)}");
                Console.WriteLine($"  Output bytes: {GetHexBytes(normalized)}");
                Console.WriteLine($"  Output string: '{normalized}'");

                var chinesePreserved = ContainsChinese(normalized);
                var byteCount = Encoding.UTF8.GetByteCount(normalized);

                Console.WriteLine($"  Has Chinese: {chinesePreserved}");
                Console.WriteLine($"  Byte count: {byteCount}");
                Console.WriteLine($"  Result: {(chinesePreserved ? "✅" : "❌")}");
                Console.WriteLine();
            }

            // Test 3: Check diacritic normalization
            Console.WriteLine("2. DIACRITIC VALIDATION (Checking character codes)");
            Console.WriteLine("===================================================\n");

            var diacriticWord = "café";
            foreach (var source in sources)
            {
                var normalized = TextNormalizer.NormalizeWordPreservingLanguage(diacriticWord, source);
                var hasDiacritic = normalized.Contains('é');

                Console.WriteLine($"{source,-15}: '{diacriticWord}' -> '{normalized}'");
                Console.WriteLine($"  Has 'é' (U+{(int)'é':X4}): {hasDiacritic}");

                bool correct;
                if (source == "GUT_WEBSTER" || source == "STRUCT_JSON")
                {
                    correct = !hasDiacritic && normalized == "cafe";
                }
                else
                {
                    correct = hasDiacritic || normalized == "café";
                }

                Console.WriteLine($"  Result: {(correct ? "✅" : "❌")}");
                Console.WriteLine();
            }

            // Test 4: SourceDataHelper validation
            Console.WriteLine("3. SOURCEDATAHELPER VALIDATION");
            Console.WriteLine("===============================\n");

            try
            {
                var result = SourceDataHelper.NormalizeWordWithSourceContext("苹果★test", "ENG_CHN");
                var hasChinese = ContainsChinese(result);
                var hasStar = result.Contains('★');

                Console.WriteLine($"Input: '苹果★test'");
                Console.WriteLine($"Output: '{result}'");
                Console.WriteLine($"Has Chinese: {hasChinese} {(hasChinese ? "✅" : "❌")}");
                Console.WriteLine($"Removed ★: {!hasStar} {(!hasStar ? "✅" : "❌")}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        private static string GetHexBytes(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return string.Join(" ", Encoding.UTF8.GetBytes(text).Select(b => b.ToString("X2")));
        }

        private static bool ContainsChinese(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;

            // Check Unicode ranges for Chinese characters
            foreach (var c in text)
            {
                // Chinese characters in Unicode blocks:
                // CJK Unified Ideographs: 0x4E00–0x9FFF
                // CJK Unified Ideographs Extension A: 0x3400–0x4DBF
                if ((c >= 0x4E00 && c <= 0x9FFF) ||
                    (c >= 0x3400 && c <= 0x4DBF))
                {
                    return true;
                }
            }
            return false;
        }
    }
}