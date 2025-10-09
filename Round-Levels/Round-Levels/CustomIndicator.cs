using System;
using System.Collections.Generic;
using System.Drawing;
using IndicatorInterfaceCSharp;

namespace CustomIndicator
{
    public class RoundLevelsOnly : IndicatorInterface
    {
        // ========= Build Info (deutsche Zeit) =========
        [Input(Name = "Build Info")]
        public string BuildInfo = "Erstellt am: 28.09.2025 18:57 (MESZ)";

        // ========= Eingabe-Parameter =========
        [Input(Name = "Lines above")]
        public int LinesAbove = 10;

        [Input(Name = "Lines below")]
        public int LinesBelow = 10;

        [Input(Name = "Step (Price)?")]
        public double Step = 5.0;

        [Input(Name = "Step in Points?")]
        public bool UsePointUnits = false;

        public enum ColorChoice { Red, Gray, Black, Blue, Green, Orange, Magenta, Cyan }

        [Input(Name = "Color?")]
        public ColorChoice LineColor = ColorChoice.Gray;

        [Input(Name = "Line Style")]
        public LineStyle LineStyleMain = LineStyle.STYLE_SOLID;

        [Input(Name = "Line Width")]
        public int LineWidth = 1;

        // Objekt-Eigenschaften
        [Input(Name = "Lock Lines")]
        public bool LockObjects = true;

        [Input(Name = "Selectable?")]
        public bool SelectableObjects = false;

        // Prefix für unsere Objekte
        private const string PrefixMain = "NM_RL_MAIN_";

        public override void OnInit()
        {
            Indicator_Separate_Window = false;
            SetIndicatorShortName("------ Round Levels");
            SetIndicatorDigits((int)Digits());
        }

        public override void OnCalculate(int index)
        {
            // Einheiten & Parameter
            double unit = UsePointUnits ? Math.Max(Point(), 1e-12) : 1.0;
            double step = Math.Max(Sanitize(Step) * unit, 1e-12);

            // Aktueller Preis (Close der letzten Kerze)
            double currentPrice = Close(0);

            // Basis-Level: nächstliegende Rundung zur Schrittweite
            double baseLevel = RoundToStep(currentPrice, step);

            // Vor dem Neuzeichnen alte Linien löschen
            DeleteExistingWithPrefix(PrefixMain);

            // Hauptlinie
            CreateHLine($"{PrefixMain}MID_0", baseLevel, ToColor(LineColor), LineStyleMain, LineWidth);

            // Linien darüber
            for (int i = 1; i <= LinesAbove; i++)
            {
                double level = baseLevel + i * step;
                CreateHLine($"{PrefixMain}UP_{i}", level, ToColor(LineColor), LineStyleMain, LineWidth);
            }

            // Linien darunter
            for (int j = 1; j <= LinesBelow; j++)
            {
                double level = baseLevel - j * step;
                CreateHLine($"{PrefixMain}DOWN_{j}", level, ToColor(LineColor), LineStyleMain, LineWidth);
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
            double k = price / step;
            double r = Math.Round(k, 0, MidpointRounding.AwayFromZero);
            return r * step;
        }

        private void CreateHLine(string name, double price, Color color, LineStyle style, int width)
        {
            ObjectDelete(name); // doppelte Namen vermeiden
            ObjectCreate(name, ObjectType.OBJ_HLINE, DateTime.MinValue, Normalize(price));
            ObjectSet(name, ObjectProperty.OBJPROP_COLOR, color);
            ObjectSet(name, ObjectProperty.OBJPROP_STYLE, style);
            ObjectSet(name, ObjectProperty.OBJPROP_WIDTH, Math.Max(1, width));
            ObjectSet(name, ObjectProperty.OBJPROP_LOCKED, LockObjects);
            ObjectSet(name, ObjectProperty.OBJPROP_SELECTABLE, SelectableObjects);
        }

        private double Normalize(double price)
        {
            int d = (int)Digits();
            if (d <= 0) return price;
            double factor = Math.Pow(10.0, d);
            return Math.Round(price * factor) / factor;
        }

        private void DeleteExistingWithPrefix(string prefix)
        {
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
