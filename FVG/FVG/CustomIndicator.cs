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
        public int FVG_Alpha = 90;

        public enum ColorChoice { Gray, Red, Green, Blue, Black, Orange, Magenta, Cyan }

        [Input(Name = "FVG-Farbe")]
        public ColorChoice FVG_Color = ColorChoice.Gray;

        [Input(Name = "Rand optisch ausblenden")]
        public bool BorderHidden = true;

        [Input(Name = "Randbreite (nur falls sichtbar)")]
        public int FVG_BorderWidth = 1;

        [Input(Name = "Randstil")]
        public LineStyle FVG_BorderStyle = LineStyle.STYLE_SOLID;

        [Input(Name = "Box in Zukunft (Bars)")]
        public int ForwardBars = 20;

        [Input(Name = "Start an Displacement-Kerze (Mitte)")]
        public bool StartAtDisplacementCandle = true;

        // kleine Kanten-Einziehung, damit der (technisch min. 1px) Rand in der Fläche „verschwindet“
        [Input(Name = "Kanten-Inset (Preis)")]
        public double EdgeInset = 0.01;

        [Input(Name = "Objekte sperren (locked)")]
        public bool LockObjects = true;

        [Input(Name = "Objekte auswählbar")]
        public bool SelectableObjects = false;

        private const string PrefixFVG = "NM_FVG_";
        private readonly List<string> _lastDrawn = new List<string>(128);

        public override void OnInit()
        {
            Indicator_Separate_Window = false;
            SetIndicatorShortName("Fair Value Gaps (Projected)");
            SetIndicatorDigits((int)Digits());
            ClampInputs();
        }

        public override void OnCalculate(int index)
        {
            // Nur auf der aktuellsten Kerze arbeiten → verhindert mehrfaches Voll-Scanning
            if (index != 0) return;

            ClampInputs();
            DeletePreviouslyDrawn();
            DrawFVGs();
        }

        private void ClampInputs()
        {
            if (FVGsAbove < 0) FVGsAbove = 0;
            if (FVGsBelow < 0) FVGsBelow = 0;
            if (FVG_Lookback < 3) FVG_Lookback = 3;
            if (FVG_Alpha < 0) FVG_Alpha = 0;
            if (FVG_Alpha > 255) FVG_Alpha = 255;
            if (FVG_BorderWidth < 1) FVG_BorderWidth = 1; // API erzwingt meist min. 1px
            if (ForwardBars < 0) ForwardBars = 0;
            if (EdgeInset < 0.01) EdgeInset = 0.01;       // 0 meiden (manche NumericUpDowns)
        }

        private struct FVG
        {
            public int LeftIndex;   // älteste Kerze (n-2)
            public int MidIndex;    // mittlere Displacement-Kerze (n-1)
            public int RightIndex;  // rechte Kerze (n)
            public double Top;
            public double Bottom;
            public bool Bullish;
        }

        private void DrawFVGs()
        {
            int bars = Bars();
            if (bars < 3) return;

            // Lookback darf verfügbare Bars nie überschreiten
            int scan = Math.Min(FVG_Lookback, bars);
            int maxI = scan - 3; // i nutzt i, i+1, i+2
            if (maxI < 0) return;

            // 0 = aktuelle Kerze; größere Indizes = Vergangenheit
            List<FVG> found = new List<FVG>(256);
            for (int i = 0; i <= maxI; i++)
            {
                int left = i + 2;
                int mid = i + 1;
                int right = i;

                double hiL = High(left);
                double loL = Low(left);
                double hiR = High(right);
                double loR = Low(right);

                // Bullish FVG
                if (hiL < loR)
                {
                    found.Add(new FVG
                    {
                        LeftIndex = left,
                        MidIndex = mid,
                        RightIndex = right,
                        Bullish = true,
                        Bottom = hiL,
                        Top = loR
                    });
                }
                // Bearish FVG
                if (loL > hiR)
                {
                    found.Add(new FVG
                    {
                        LeftIndex = left,
                        MidIndex = mid,
                        RightIndex = right,
                        Bullish = false,
                        Bottom = hiR,
                        Top = loL
                    });
                }
            }

            // offene FVGs (noch nicht „gefüllt“)
            List<FVG> open = new List<FVG>(found.Count);
            foreach (var f in found)
            {
                bool closed = false;
                for (int j = f.RightIndex - 1; j >= 0; j--)
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

            // nach Lage zum aktuellen Preis trennen
            double priceNow = Close(0);
            List<FVG> above = new List<FVG>();
            List<FVG> below = new List<FVG>();
            foreach (var f in open)
            {
                if (f.Bottom > priceNow) above.Add(f);
                else if (f.Top < priceNow) below.Add(f);
                else above.Add(f); // schneidet aktuellen Preis → zu "above"
            }

            // jüngste zuerst (kleinerer RightIndex = jünger)
            above.Sort((a, b) => a.RightIndex.CompareTo(b.RightIndex));
            below.Sort((a, b) => a.RightIndex.CompareTo(b.RightIndex));

            if (FVGsAbove >= 0 && above.Count > FVGsAbove) above = above.GetRange(0, FVGsAbove);
            if (FVGsBelow >= 0 && below.Count > FVGsBelow) below = below.GetRange(0, FVGsBelow);

            // zeichnen
            int id = 0;
            foreach (var f in above) DrawFVGRect(BuildName(f, "A", id++), f);
            id = 0;
            foreach (var f in below) DrawFVGRect(BuildName(f, "B", id++), f);
        }

        private string BuildName(FVG f, string side, int rank)
        {
            long tL = Time(f.LeftIndex).Ticks;
            long tR = Time(f.RightIndex).Ticks;
            return $"{PrefixFVG}{side}_{(f.Bullish ? "BULL" : "BEAR")}_{tL}_{tR}_{rank}";
        }

        private void DrawFVGRect(string name, FVG f)
        {
            // Startzeit: mittlere Displacement-Kerze (oder rechte)
            DateTime leftTime = StartAtDisplacementCandle ? Time(f.MidIndex) : Time(f.RightIndex);

            // Zukunftsprojektion
            TimeSpan barSpan = (Bars() >= 2) ? (Time(0) - Time(1)) : TimeSpan.FromSeconds(1);
            DateTime rightTime = Time(0).AddSeconds(Math.Max(0, ForwardBars) * barSpan.TotalSeconds);

            double inset = Math.Max(EdgeInset, 0.01);
            double top = Normalize(f.Top) - (BorderHidden ? inset : 0.0);
            double bot = Normalize(f.Bottom) + (BorderHidden ? inset : 0.0);
            if (top < bot) { var tmp = top; top = bot; bot = tmp; }

            Color fill = Color.FromArgb(Clamp(FVG_Alpha, 0, 255), ToColor(FVG_Color));

            ObjectCreate(name, ObjectType.OBJ_RECTANGLE, leftTime, top, rightTime, bot);
            ObjectSet(name, ObjectProperty.OBJPROP_COLOR, fill);

            if (BorderHidden)
            {
                ObjectSet(name, ObjectProperty.OBJPROP_STYLE, LineStyle.STYLE_SOLID);
                ObjectSet(name, ObjectProperty.OBJPROP_WIDTH, 1); // min. 1px, optisch „weg“
            }
            else
            {
                ObjectSet(name, ObjectProperty.OBJPROP_STYLE, FVG_BorderStyle);
                ObjectSet(name, ObjectProperty.OBJPROP_WIDTH, Math.Max(1, FVG_BorderWidth));
            }

            ObjectSet(name, ObjectProperty.OBJPROP_LOCKED, LockObjects);
            ObjectSet(name, ObjectProperty.OBJPROP_SELECTABLE, SelectableObjects);

            _lastDrawn.Add(name);
        }

        private void DeletePreviouslyDrawn()
        {
            if (_lastDrawn.Count == 0) return;
            foreach (var n in _lastDrawn)
            {
                try { ObjectDelete(n); } catch { /* ignore */ }
            }
            _lastDrawn.Clear();
        }

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

        private double Normalize(double price)
        {
            int d = (int)Digits();
            if (d <= 0) return price;
            double factor = Math.Pow(10.0, d);
            return Math.Round(price * factor) / factor;
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
