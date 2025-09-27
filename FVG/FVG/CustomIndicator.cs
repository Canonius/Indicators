using System;
using System.Collections.Generic;
using System.Drawing;
using IndicatorInterfaceCSharp;

namespace CustomIndicator
{
    public class FVGIndicator : IndicatorInterface
    {
        // ===== Eingaben =====
        [Input(Name = "FVGs über dem Preis")]
        public int FVGsAbove = 10;

        [Input(Name = "FVGs unter dem Preis")]
        public int FVGsBelow = 10;

        [Input(Name = "FVG Lookback (Kerzen)")]
        public int FVG_Lookback = 2000;

        [Input(Name = "FVG-Deckkraft (0..255)")]
        public int FVG_Alpha = 90;   // etwas kräftiger sichtbar

        public enum ColorChoice { Gray, Red, Green, Blue, Black, Orange, Magenta, Cyan }

        [Input(Name = "FVG-Farbe")]
        public ColorChoice FVG_Color = ColorChoice.Gray;

        [Input(Name = "FVG-Randstärke")]
        public int FVG_BorderWidth = 2;

        [Input(Name = "FVG-Randstil")]
        public LineStyle FVG_BorderStyle = LineStyle.STYLE_SOLID;

        [Input(Name = "Objekte sperren (locked)")]
        public bool LockObjects = true;

        [Input(Name = "Objekte auswählbar")]
        public bool SelectableObjects = false;

        private const string PrefixFVG = "NM_FVG_";

        public override void OnInit()
        {
            Indicator_Separate_Window = false;
            SetIndicatorShortName("Fair Value Gaps");
            SetIndicatorDigits((int)Digits());
            ClampInputs();
        }

        public override void OnCalculate(int index)
        {
            ClampInputs();
            DrawFVGs();
        }

        // ===== Helper: Eingaben absichern =====
        private void ClampInputs()
        {
            if (FVGsAbove < 0) FVGsAbove = 0;
            if (FVGsBelow < 0) FVGsBelow = 0;
            if (FVG_Lookback < 50) FVG_Lookback = 50;
            if (FVG_BorderWidth < 1) FVG_BorderWidth = 1;
            if (FVG_Alpha < 0) FVG_Alpha = 0;
            if (FVG_Alpha > 255) FVG_Alpha = 255;
        }

        // ===== FVG-Logik =====
        private struct FVG
        {
            public int LeftIndex;   // älteste Kerze im Setup (n-2)
            public int RightIndex;  // rechte Kerze im Setup (n)
            public double Top;
            public double Bottom;
            public bool Bullish;
        }

        private void DrawFVGs()
        {
            int bars = Bars();
            if (bars < 3)
            {
                DeleteExistingWithPrefix(PrefixFVG);
                return;
            }

            int maxScan = Math.Min(bars - 1, Math.Max(50, FVG_Lookback));

            // WICHTIG (Indexrichtung!):
            // 0 = aktuelle Kerze, größere Indizes = ältere Kerzen.
            // Tripel ist [i+2] (links/älter), [i+1] (mitte), [i] (rechts/jünger).
            List<FVG> found = new List<FVG>(256);
            for (int i = 0; i <= maxScan - 2; i++)
            {
                double hiL = High(i + 2);
                double loL = Low(i + 2);
                double hiR = High(i);
                double loR = Low(i);

                // Bullish FVG: High[left] < Low[right]
                if (hiL < loR)
                {
                    found.Add(new FVG
                    {
                        LeftIndex = i + 2,
                        RightIndex = i,
                        Bullish = true,
                        Bottom = hiL,
                        Top = loR
                    });
                }

                // Bearish FVG: Low[left] > High[right]
                if (loL > hiR)
                {
                    found.Add(new FVG
                    {
                        LeftIndex = i + 2,
                        RightIndex = i,
                        Bullish = false,
                        Bottom = hiR,
                        Top = loL
                    });
                }
            }

            // offene FVGs (geschlossen durch spätere Kerzen in Richtung 0)
            List<FVG> open = new List<FVG>(found.Count);
            foreach (var f in found)
            {
                bool closed = false;
                for (int j = f.RightIndex - 1; j >= 0; j--) // spätere (neuere) Kerzen: ... 2,1,0
                {
                    if (f.Bullish)
                    {
                        if (Low(j) <= f.Bottom) { closed = true; break; }
                    }
                    else
                    {
                        if (High(j) >= f.Top) { closed = true; break; }
                    }
                }
                if (!closed) open.Add(f);
            }

            // nach Lage zum aktuellen Preis aufteilen
            double priceNow = Close(0);
            List<FVG> above = new List<FVG>();
            List<FVG> below = new List<FVG>();
            foreach (var f in open)
            {
                if (f.Bottom > priceNow) above.Add(f);
                else if (f.Top < priceNow) below.Add(f);
                else above.Add(f); // schneidet aktuellen Preis → zu "above", damit sichtbar
            }

            // jüngste zuerst (kleinerer RightIndex = jünger)
            above.Sort((a, b) => a.RightIndex.CompareTo(b.RightIndex));
            below.Sort((a, b) => a.RightIndex.CompareTo(b.RightIndex));

            if (FVGsAbove >= 0 && above.Count > FVGsAbove) above = above.GetRange(0, FVGsAbove);
            if (FVGsBelow >= 0 && below.Count > FVGsBelow) below = below.GetRange(0, FVGsBelow);

            // alte FVG-Objekte löschen und neu zeichnen
            DeleteExistingWithPrefix(PrefixFVG);

            int id = 0;
            foreach (var f in above) DrawFVGRect($"{PrefixFVG}A_{id++}", f);
            id = 0;
            foreach (var f in below) DrawFVGRect($"{PrefixFVG}B_{id++}", f);
        }

        private void DrawFVGRect(string name, FVG f)
        {
            // Von der rechten Setup-Kerze bis jetzt zeichnen
            DateTime t1 = Time(f.RightIndex);
            DateTime t2 = Time(0); // aktuell

            double top = Normalize(f.Top);
            double bot = Normalize(f.Bottom);
            if (top < bot) { var tmp = top; top = bot; bot = tmp; }

            // Halbtransparente Farbe (einzige Property für OBJ_RECTANGLE)
            Color c = Color.FromArgb(Clamp(FVG_Alpha, 0, 255), ToColor(FVG_Color));

            ObjectCreate(name, ObjectType.OBJ_RECTANGLE, t1, top, t2, bot);
            ObjectSet(name, ObjectProperty.OBJPROP_COLOR, c);
            ObjectSet(name, ObjectProperty.OBJPROP_STYLE, FVG_BorderStyle);
            ObjectSet(name, ObjectProperty.OBJPROP_WIDTH, Math.Max(1, FVG_BorderWidth));
            ObjectSet(name, ObjectProperty.OBJPROP_LOCKED, LockObjects);
            ObjectSet(name, ObjectProperty.OBJPROP_SELECTABLE, SelectableObjects);
        }

        // ===== Hilfen =====
        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

        private double Normalize(double price)
        {
            int d = (int)Digits();
            if (d <= 0) return price;
            double factor = Math.Pow(10.0, d);
            return Math.Round(price * factor) / factor;
        }

        private void DeleteExistingWithPrefix(string prefix)
        {
            int total = ObjectTotal(ObjectType.OBJ_RECTANGLE);
            List<string> del = new List<string>(total);
            for (int i = 0; i < total; i++)
            {
                string name = ObjectName(i); // API: 1-Parameter-Version
                if (!string.IsNullOrEmpty(name) && name.StartsWith(prefix, StringComparison.Ordinal))
                    del.Add(name);
            }
            foreach (var n in del) ObjectDelete(n);
        }

        private Color ToColor(ColorChoice c)
        {
            switch (c)
            {
                case ColorChoice.Gray: return Color.Gray;
                case ColorChoice.Red: return Color.Red;
                case ColorChoice.Green: return Color.Green;
                case ColorChoice.Blue: return Color.Blue;
                case ColorChoice.Black: return Color.Black;
                case ColorChoice.Orange: return Color.Orange;
                case ColorChoice.Magenta: return Color.Magenta;
                case ColorChoice.Cyan: return Color.Cyan;
                default: return Color.Gray;
            }
        }
    }
}
