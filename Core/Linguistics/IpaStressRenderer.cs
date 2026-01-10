using DictionaryImporter.Core.PreProcessing;
using System.Text;

namespace DictionaryImporter.Core.Linguistics
{
    /// <summary>
    /// INTERNAL renderer for IPA syllables with stress.
    /// </summary>
    internal static class IpaStressRenderer
    {
        public static string Render(
            IReadOnlyList<IpaSyllable> syllables,
            IpaStressRenderProfile profile)
        {
            if (syllables == null || syllables.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();

            bool useDots = profile == IpaStressRenderProfile.EnUk;

            sb.Append('/');

            for (int i = 0; i < syllables.Count; i++)
            {
                var s = syllables[i];

                if (i > 0 && useDots)
                    sb.Append('.');

                if (s.StressLevel == 2)
                    sb.Append('ˈ');
                else if (s.StressLevel == 1)
                    sb.Append('ˌ');

                sb.Append(s.Text);
            }

            sb.Append('/');

            return sb.ToString();
        }
    }
}
