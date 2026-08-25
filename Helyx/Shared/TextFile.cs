using System.Text;

namespace Helyx.Shared
{
    internal static class TextFile
    {
        internal static Encoding ReadWithBom(string path, out string content)
        {
            var raw = File.ReadAllBytes(path);

            Encoding encoding = raw switch
            {
                [0xEF, 0xBB, 0xBF, ..] => new UTF8Encoding(true),
                [0xFF, 0xFE, 0x00, 0x00, ..] => new UTF32Encoding(false, true),
                [0x00, 0x00, 0xFE, 0xFF, ..] => new UTF32Encoding(true, true),
                [0xFF, 0xFE, ..] => new UnicodeEncoding(false, true),
                [0xFE, 0xFF, ..] => new UnicodeEncoding(true, true),
                _ => new UTF8Encoding(false)
            };

            var preamble = encoding.GetPreamble().Length;

            content = encoding.GetString(raw, preamble, raw.Length - preamble);

            return encoding;
        }

        internal static bool Decoded(string content) => !content.Contains('�');
    }
}
