using System;
using System.Collections.Generic;
using System.Drawing;
using IndicatorInterfaceCSharp;

namespace CustomIndicator
{
    public class CustomIndicator : IndicatorInterface
    {
        // ========= Eingabe-Parameter (im UI veränderbar) =========

        [Input(Name = "Linien oben")]
        public int LinesAbove = 10;

        [Input(Name = "Linien unten")]
        public int LinesBelow = 10;

        [Input(Name = "Schrittweite (Preis)")]
        public double Step = 5.0;

        [Input(Name = "Psych-Zonen Offset")]
        public double PsychOffset = 0.5;

        [Input(Name = "Step/Zonen in Points() statt Preis?")]
        public bool UsePointUnits = false;

        // --- Steuerung der 3. Zone (um das aktuelle runde Level) ---
        [Input(Name = "Mittlere Zone immer zeichnen?")]
        public bool AlwaysMiddleZone = true;

        // WICHTIG: Default NICHT 0 setzen (manche UIs verlangen Minimum > 0)
        [Input(Name = "Toleranz für 'auf Rundungslevel'")]
        public double OnLevelTolerance = 0.1; // vorher 0.0 -> OutOfRange bei manchen Builds

        // Hauptlinien-Style
        public enum ColorChoice { Red, Gray, Black, Blue, Green, Orange, Magenta, Cyan }

        [Input(Name = "Hauptlinien-Farbe")]
        public ColorChoice MainColor = ColorChoice.Red;

        [Input(Name = "Hauptlinien-Stil")]
        public LineStyle MainLineStyle = LineStyle.STYLE_SOLID;

        [Input(Name = "Hauptlinien-Stärke")]
        public int MainLineWidth = 1;

        // Zonen-Style
        [Input(Name = "Zonen-Farbe")]
        public ColorChoice ZoneColor = ColorChoice.Gray;

        [Input(Name = "Zonen-Stil")]
        public LineStyle ZoneLineStyle = LineStyle.STYLE_DASH;

        [Input(Name = "Zonen-Linienstärke")]
        public int ZoneLineWidth = 1;

        // Objekt-Eigenschaften
        [Input(Name = "Objekte sperren (locked)")]
        public bool LockObjects = true;

        [Input(Name = "Objekte auswählbar")]
        public bool SelectableObjects = false;

        // Prefixe, damit wir nur "unsere" Linien verwalten
        private const string PrefixMain = "NM_PL_MAIN_";
        private const string PrefixZone = "NM_PL_ZONE_";

        public override void OnInit()
        {
            Indicator_Separate_Window = false;
            SetIndicatorShortName("Price Lines (Round Levels)");
            SetIndicatorDigits((int)Digits());
        }

        public override void OnCalculate(int index)
        {
            // Sicherheitschecks
            if (LinesAbove < 0) LinesAbove = 0;
            if (LinesBelow < 0) LinesBelow = 0;

            double unit = UsePointUnits ? Math.Max(Point(), 1e-12) : 1.0;
            double step = Math.Max(Sanitize(Step) * unit, 1e-12);
            double zOff = Math.Max(Sanitize(PsychOffset) * unit, 0.0);

            // Toleranz: niemals 0 (um UI/Min-Restriktionen & logische Checks zu vermeiden)
            double tolRaw = Sanitize(OnLevelTolerance) * (UsePointUnits ? Math.Max(Point(), 1e-12) : 1.0);
            double tol = Math.Max(tolRaw, 1e-9); // mini-positiv

            // Aktueller Preis (Close der letzten Kerze)
            double currentPrice = Close(0);

            // Basis-Level: nächstliegende Rundung zur Schrittweite
            double baseLevel = RoundToStep(currentPrice, step);

            // Bevor wir neu zeichnen, alte eigene Linien löschen
            DeleteExistingWithPrefix(PrefixMain);
            DeleteExistingWithPrefix(PrefixZone);

            // === Hauptlinien ===
            // Mittlere Hauptlinie am aktuellen runden Level (dein Wunsch)
            CreateHLine($"{PrefixMain}MID_0", baseLevel, ToColor(MainColor), MainLineStyle, MainLineWidth);

            // Hauptlinien oberhalb
            for (int i = 1; i <= LinesAbove; i++)
            {
                double level = baseLevel + i * step;
                CreateHLine($"{PrefixMain}UP_{i}", level, ToColor(MainColor), MainLineStyle, MainLineWidth);
            }

            // Hauptlinien unterhalb
            for (int j = 1; j <= LinesBelow; j++)
            {
                double level = baseLevel - j * step;
                CreateHLine($"{PrefixMain}DOWN_{j}", level, ToColor(MainColor), MainLineStyle, MainLineWidth);
            }

            // === Psychologische Zonen (3 Stück) ===
            // Nächster oberer/unterer Rundungs-Level relativ zum aktuellen Preis
            double nextUp = (baseLevel >= currentPrice) ? baseLevel : baseLevel + step;
            double prevDown = (baseLevel <= currentPrice) ? baseLevel : baseLevel - step;

            // 1) Zone um das nächste obere Rundungslevel
            CreateZone($"{PrefixZone}UP", nextUp, zOff);

            // 2) Zone um das nächste untere Rundungslevel
            CreateZone($"{PrefixZone}DN", prevDown, zOff);

            // 3) Zone um das aktuelle runde Level:
            //    a) Immer zeichnen, wenn AlwaysMiddleZone = true
            //    b) Sonst nur, wenn |currentPrice - baseLevel| <= tol
            bool onBase = Math.Abs(currentPrice - baseLevel) <= tol;
            if (AlwaysMiddleZone || onBase)
            {
                CreateZone($"{PrefixZone}MID", baseLevel, zOff);
            }
        }

        // ===================== Hilfsfunktionen =====================

        private static double Sanitize(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return 0.0;
            return v;
        }

        private double RoundToStep(double price, double step)
        {
            // Rundet auf das nächste Vielfache von "step"
            double k = price / step;
            double r = Math.Round(k, 0, MidpointRounding.AwayFromZero);
            return r * step;
        }

        private void CreateZone(string tag, double level, double offset)
        {
            // Zeichnet obere/untere gestrichelte Zonenlinie um "level"
            CreateHLine($"{tag}_TOP", level + offset, ToColor(ZoneColor), ZoneLineStyle, ZoneLineWidth);
            CreateHLine($"{tag}_BOTTOM", level - offset, ToColor(ZoneColor), ZoneLineStyle, ZoneLineWidth);
        }

        private void CreateHLine(string name, double price, Color color, LineStyle style, int width)
        {
            // Horizontale Linie: nur der Preis ist relevant (Time = DateTime.MinValue)
            ObjectCreate(name, ObjectType.OBJ_HLINE, DateTime.MinValue, Normalize(price));
            ObjectSet(name, ObjectProperty.OBJPROP_COLOR, color);
            ObjectSet(name, ObjectProperty.OBJPROP_STYLE, style);
            ObjectSet(name, ObjectProperty.OBJPROP_WIDTH, Math.Max(1, width));
            ObjectSet(name, ObjectProperty.OBJPROP_LOCKED, LockObjects);
            ObjectSet(name, ObjectProperty.OBJPROP_SELECTABLE, SelectableObjects);
        }

        private double Normalize(double price)
        {
            // Preis auf Symbolpräzision runden
            int d = (int)Digits();
            if (d <= 0) return price;
            double factor = Math.Pow(10.0, d);
            return Math.Round(price * factor) / factor;
        }

        private void DeleteExistingWithPrefix(string prefix)
        {
            // Alle HLINE-Objekte einsammeln und die mit unserem Prefix entfernen
            int total = ObjectTotal(ObjectType.OBJ_HLINE);
            List<string> toDelete = new List<string>(capacity: total);

            for (int i = 0; i < total; i++)
            {
                string name = ObjectName(i);
                if (!string.IsNullOrEmpty(name) && name.StartsWith(prefix, StringComparison.Ordinal))
                    toDelete.Add(name);
            }

            foreach (var n in toDelete)
                ObjectDelete(n);
        }

        private Color ToColor(ColorChoice choice)
        {
            switch (choice)
            {
                case ColorChoice.Red: return Color.Red;
                case ColorChoice.Gray: return Color.Gray;
                case ColorChoice.Black: return Color.Black;
                case ColorChoice.Blue: return Color.Blue;
                case ColorChoice.Green: return Color.Green;
                case ColorChoice.Orange: return Color.Orange;
                case ColorChoice.Magenta: return Color.Magenta;
                case ColorChoice.Cyan: return Color.Cyan;
                default: return Color.Red;
            }
        }
    }
}
