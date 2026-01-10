using System.Text;

namespace DictionaryImporter.Core.PreProcessing
{
    internal static class IpaSyllabifier
    {
        private static readonly HashSet<char> Vowels =
            new()
            {
                'i','y','ɪ','ʏ','e','ø','ɛ','œ','æ','a','ɑ','ɒ','ɔ','o','ʊ','u',
                'ə','ɚ','ɝ','ɜ','ɵ','ɐ'
            };

        public static IReadOnlyList<IpaSyllable> Split(string ipa)
        {
            var result = new List<IpaSyllable>();

            if (string.IsNullOrWhiteSpace(ipa))
                return result;

            var buffer = new StringBuilder();
            bool hasVowel = false;
            byte currentStress = 0;
            int index = 1;

            foreach (var ch in ipa)
            {
                if (ch == 'ˈ')
                {
                    currentStress = 2;
                    continue;
                }

                if (ch == 'ˌ')
                {
                    currentStress = 1;
                    continue;
                }

                buffer.Append(ch);

                if (Vowels.Contains(ch))
                {
                    if (hasVowel)
                    {
                        result.Add(
                            new IpaSyllable(
                                index++,
                                buffer.ToString(0, buffer.Length - 1),
                                currentStress));

                        buffer.Clear();
                        buffer.Append(ch);
                        currentStress = 0;
                    }

                    hasVowel = true;
                }
            }

            if (buffer.Length > 0)
            {
                result.Add(
                    new IpaSyllable(
                        index,
                        buffer.ToString(),
                        currentStress));
            }

            return result;
        }
    }
}
