using System;
using System.Drawing;

namespace WinFormsApp1
{
    // ================= Theme Engine (professional palettes) =================
    // (Form1.cs ကနေ ခွဲထုတ်ထားသည် — သီးသန့် partial class file)
    internal static class ThemeManager
    {
        public static readonly string[] ThemeNames = { "Ocean Dark", "Nord", "Dracula", "Tokyo Night", "Gruvbox", "Daylight" };
        public static string Current = "Ocean Dark";

        // slot: 0 shell | 1 left | 2 conn/profile | 3 right | 4 logHeader | 5 categoryBar | 6 actionPanel
        //       7 combo | 8 textbox | 9 footer | 10 status | 11 rtb | 12 gridBg | 13 row | 14 rowAlt | 15 header
        private static readonly Color[][] Palettes =
        {
            // Ocean Dark (မူရင်း)
            new[] { C(18,24,32), C(14,20,27), C(27,36,48), C(22,29,39), C(35,47,61), C(31,41,55), C(25,33,44),
                    C(35,45,58), C(40,50,65), C(20,28,38), C(12,17,23), C(10,16,22), C(13,20,28), C(18,26,36), C(23,33,46), C(30,44,60) },
            // Nord (cool arctic — dev tools အကျော်ကြားဆုံး)
            new[] { C(46,52,64), C(41,47,59), C(59,66,82), C(51,57,69), C(67,76,94), C(59,66,82), C(55,62,77),
                    C(67,76,94), C(76,86,106), C(46,52,64), C(38,43,54), C(36,41,51), C(46,52,64), C(59,66,82), C(52,58,70), C(76,86,106) },
            // Dracula (classic rich dark)
            new[] { C(40,42,54), C(33,34,44), C(52,55,70), C(46,48,62), C(68,71,90), C(59,62,79), C(47,49,64),
                    C(68,71,90), C(74,78,99), C(33,34,44), C(25,26,33), C(23,24,30), C(40,42,54), C(52,55,70), C(45,47,61), C(68,71,90) },
            // Tokyo Night (deep indigo)
            new[] { C(26,27,38), C(22,22,30), C(36,40,59), C(31,35,53), C(42,47,69), C(36,40,59), C(30,34,51),
                    C(42,47,69), C(50,55,79), C(22,22,30), C(18,19,25), C(16,17,23), C(26,27,38), C(36,40,59), C(30,34,51), C(47,53,80) },
            // Gruvbox (warm retro)
            new[] { C(40,40,40), C(29,32,33), C(60,56,54), C(50,48,47), C(69,64,61), C(60,56,54), C(55,51,49),
                    C(69,64,61), C(80,73,69), C(29,32,33), C(20,21,21), C(18,20,19), C(40,40,40), C(60,56,54), C(52,49,47), C(74,69,66) },
            // Daylight (light)
            new[] { C(238,241,246), C(252,253,255), C(225,230,240), C(243,246,250), C(230,234,242), C(222,227,237), C(236,239,245),
                    C(255,255,255), C(255,255,255), C(230,234,242), C(224,229,238), C(252,252,254), C(248,250,253), C(255,255,255), C(241,244,249), C(218,224,235) },
        };

        public static Color C(int r, int g, int b) => Color.FromArgb(r, g, b);

        public static void Set(string name)
        {
            int idx = Array.IndexOf(ThemeNames, name);
            if (idx < 0) idx = 0;
            Current = ThemeNames[idx];
        }

        public static Color Slot(int slot) => Palettes[Array.IndexOf(ThemeNames, Current)][slot];

        // နောက်ခံအရောင်ရဲ့ အလင်းပြင်းအားပေါ်မူတည်ပြီး ဖတ်ရလွယ်တဲ့ text အရောင် ရွေးပေးတယ်
        public static Color TextFor(Color bg)
        {
            double lum = 0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B;
            return lum >= 150 ? C(38, 44, 54) : C(226, 232, 240);
        }

        public static bool IsLight()
        {
            Color bg = Slot(0);
            return (0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B) >= 150;
        }

        // Log အရောင်တွေကို theme နဲ့ လိုက်ဖက်အောင် ချိန်ပေးတယ်
        // (dark theme အတွက် ဒီဇိုင်းထားတဲ့ pastel တွေကို light theme မှာ နက်အောင် ပြောင်းတယ်)
        public static Color AdaptLog(Color c)
        {
            if (!IsLight()) return c;
            double lum = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
            if (lum < 120) return c;
            double f = lum > 200 ? 0.38 : 0.52;
            return C(Math.Max(0, (int)(c.R * f)), Math.Max(0, (int)(c.G * f)), Math.Max(0, (int)(c.B * f)));
        }
    }
}
