using System;
using System.Collections.Generic;
using System.Drawing;
using IndicatorInterfaceCSharp;

namespace CustomIndicator
{
    public class CustomIndicator : IndicatorInterface
    {
        // ========= Build Info =========
        [Input(Name = "Build Info")]
        public string BuildInfo = "Erstellt am: 28.09.2025 14:34 (MESZ)";

        // ========= Eingabe-Parameter =========

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

        // 3. Zone (um aktuelles rundes Level)
        [Input(Name = "Mittlere Zone immer zeichnen?")]
        public bool AlwaysMiddleZone = true;

        [Input(Name = "Toleranz für 'auf Rundungslevel'")]
        public double OnLevelTolerance = 0.1; // >0 als Default

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

        // Marker-Optionen
        [Input(Name = "Signale aktivieren")]
        public bool EnableSignals = true;

        [Input(Name = "Marker-Abstand (vertikal)")]
        public double MarkerOffset = 0.5;

        [Input(Name = "Bull-Marker-Farbe")]
        public ColorChoice BullMarkerColor = ColorChoice.Green;

        [Input(Name = "Bear-Marker-Farbe")]
        public ColorChoice BearMarkerColor = ColorChoice.Red;

        [Input(Name = "Marker-Schriftgröße")]
        public int MarkerFontSize = 12;

        [Input(Name = "Marker fett")]
        public bool MarkerBold = true;

        // Signalmodus
        public enum SignalMode { TouchThenZoneClose = 0, OpenSideThenZoneClose = 1 }

        [Input(Name = "Signalmodus")]
        public SignalMode Mode = SignalMode.TouchThenZoneClose; // Standard

        // Max. Anzahl Markierungen (FIFO)
        [Input(Name = "Maximale Anzahl Markierungen")]
        public int MaxSignals = 200;

        // Prefixe
        private const string PrefixMain = "NM_PL_MAIN_";
        private const string PrefixZone = "NM_PL_ZONE_";
        private const string PrefixSig = "NM_PL_SIG_";

        // Bar-Close-Erkennung
        private DateTime _lastBar0Time = DateTime.MinValue;

        // FIFO der Signal-Objekte
        private readonly Queue<string> _signalObjects = new Queue<string>();

        // Anti-Doppelsignal pro Level
        private enum SignalSide { None = 0, Bull = 1, Bear = 2 }
        private readonly Dictionary<double, SignalSide> _lastSidePerLevel = new Dictionary<double, SignalSide>();

        public override void OnInit()
        {
            Indicator_Separate_Window = false;
            SetIndicatorShortName("------ Price Lines (Round Levels) + Signals");
            SetIndicatorDigits((int)Digits());
        }

        public override void OnCalculate(int index)
        {
            // Einheiten & Parameter
            double unit = UsePointUnits ? Math.Max(Point(), 1e-12) : 1.0;
            double step = Math.Max(Sanitize(Step) * unit, 1e-12);
            double zOff = Math.Max(Sanitize(PsychOffset) * unit, 0.0);
            double mOff = Math.Max(Sanitize(MarkerOffset) * unit, 0.0);
            double tolRaw = Sanitize(OnLevelTolerance) * (UsePointUnits ? Math.Max(Point(), 1e-12) : 1.0);
            double tol = Math.Max(tolRaw, 1e-9);

            // Linien/Zonen (aktueller Snapshot)
            double currentPrice = Close(0);
            double baseLevelNow = RoundToStep(currentPrice, step);

            DeleteExistingWithPrefix(PrefixMain);
            DeleteExistingWithPrefix(PrefixZone);

            CreateHLine($"{PrefixMain}MID_0", baseLevelNow, ToColor(MainColor), MainLineStyle, MainLineWidth);

            for (int i = 1; i <= LinesAbove; i++)
                CreateHLine($"{PrefixMain}UP_{i}", baseLevelNow + i * step, ToColor(MainColor), MainLineStyle, MainLineWidth);

            for (int j = 1; j <= LinesBelow; j++)
                CreateHLine($"{PrefixMain}DOWN_{j}", baseLevelNow - j * step, ToColor(MainColor), MainLineStyle, MainLineWidth);

            double nextUpNow = (baseLevelNow >= currentPrice) ? baseLevelNow : baseLevelNow + step;
            double prevDownNow = (baseLevelNow <= currentPrice) ? baseLevelNow : baseLevelNow - step;

            CreateZone($"{PrefixZone}UP", nextUpNow, zOff);
            CreateZone($"{PrefixZone}DN", prevDownNow, zOff);

            bool onBaseNow = Math.Abs(currentPrice - baseLevelNow) <= tol;
            if (AlwaysMiddleZone || onBaseNow)
                CreateZone($"{PrefixZone}MID", baseLevelNow, zOff);

            // Bar-Close-Erkennung & Signale
            if (!EnableSignals || Bars() < 2) return;

            DateTime bar0Time = Time(0);
            bool newBarStarted = bar0Time != _lastBar0Time;

            if (newBarStarted)
            {
                _lastBar0Time = bar0Time;
                ProcessSignalForBar(1, step, zOff, mOff);
            }

            if (_lastBar0Time == DateTime.MinValue && index > 1)
            {
                _lastBar0Time = bar0Time;
                ProcessSignalForBar(1, step, zOff, mOff);
            }
        }

        // ===================== Signalberechnung =====================

        private void ProcessSignalForBar(int b, double step, double zOff, double mOff)
        {
            double openB = Open(b);
            double closeB = Close(b);
            double highB = High(b);
            double lowB = Low(b);
            DateTime t = Time(b);

            // Basis: Open(b)
            double baseLevelB = RoundToStep(openB, step);

            // Für Altmodus
            double nextUpB = (baseLevelB >= openB) ? baseLevelB : baseLevelB + step;
            double prevDownB = (baseLevelB <= openB) ? baseLevelB : baseLevelB - step;
            double upperTopB = nextUpB + zOff;
            double lowerBotB = prevDownB - zOff;

            bool bullSignal = false;
            bool bearSignal = false;
            double bullLevelUsed = double.NaN;
            double bearLevelUsed = double.NaN;

            if (Mode == SignalMode.TouchThenZoneClose)
            {
                double first = Math.Ceiling(lowB / step) * step;
                double last = Math.Floor(highB / step) * step;

                double? touchedClosestAboveOpen = null;
                double? touchedClosestBelowOpen = null;

                if (last >= first)
                {
                    for (double lvl = first; lvl <= last + 1e-10; lvl += step)
                    {
                        double level = Normalize(lvl);
                        if (level >= openB)
                        {
                            if (touchedClosestAboveOpen == null || level < touchedClosestAboveOpen.Value)
                                touchedClosestAboveOpen = level;
                        }
                        if (level <= openB)
                        {
                            if (touchedClosestBelowOpen == null || level > touchedClosestBelowOpen.Value)
                                touchedClosestBelowOpen = level;
                        }
                    }
                }

                if (touchedClosestAboveOpen != null || (last >= first))
                {
                    double usedLevel = touchedClosestAboveOpen ?? Normalize(first);
                    double usedUpperTop = usedLevel + zOff;
                    bullSignal = closeB > usedUpperTop;
                    bullLevelUsed = usedLevel;
                }

                if (touchedClosestBelowOpen != null || (last >= first))
                {
                    double usedLevel = touchedClosestBelowOpen ?? Normalize(last);
                    double usedLowerBot = usedLevel - zOff;
                    bearSignal = closeB < usedLowerBot;
                    bearLevelUsed = usedLevel;
                }
            }
            else
            {
                bullSignal = (openB < nextUpB) && (closeB > upperTopB);
                bearSignal = (openB > prevDownB) && (closeB < lowerBotB);
                bullLevelUsed = nextUpB;
                bearLevelUsed = prevDownB;
            }

            if (bullSignal && !double.IsNaN(bullLevelUsed))
            {
                if (AllowedForLevel(bullLevelUsed, SignalSide.Bull))
                {
                    string name = $"{PrefixSig}BULL_{t:yyyyMMdd_HHmmss}";
                    double y = highB + mOff;
                    CreateTriangleText(name, t, y, "▲", ToColor(BullMarkerColor)); // Bull über der Kerze
                    AddSignalObject(name);
                }
            }

            if (bearSignal && !double.IsNaN(bearLevelUsed))
            {
                if (AllowedForLevel(bearLevelUsed, SignalSide.Bear))
                {
                    string name = $"{PrefixSig}BEAR_{t:yyyyMMdd_HHmmss}";
                    double y = lowB - mOff;
                    CreateTriangleText(name, t, y, "▼", ToColor(BearMarkerColor)); // Bear unter der Kerze
                    AddSignalObject(name);
                }
            }
        }

        private bool AllowedForLevel(double level, SignalSide side)
        {
            double key = Normalize(level);
            if (_lastSidePerLevel.TryGetValue(key, out var prev) && prev == side)
                return false;

            _lastSidePerLevel[key] = side;
            return true;
        }

        private void AddSignalObject(string name)
        {
            _signalObjects.Enqueue(name);
            if (MaxSignals < 1) MaxSignals = 1;

            while (_signalObjects.Count > MaxSignals)
            {
                string oldest = _signalObjects.Dequeue();
                ObjectDelete(oldest);
            }
        }

        private static double Sanitize(double v) => (double.IsNaN(v) || double.IsInfinity(v)) ? 0.0 : v;

        private double RoundToStep(double price, double step)
        {
            double k = price / step;
            double r = Math.Round(k, 0, MidpointRounding.AwayFromZero);
            return r * step;
        }

        private void CreateZone(string tag, double level, double offset)
        {
            CreateHLine($"{tag}_TOP", level + offset, ToColor(ZoneColor), ZoneLineStyle, ZoneLineWidth);
            CreateHLine($"{tag}_BOTTOM", level - offset, ToColor(ZoneColor), ZoneLineStyle, ZoneLineWidth);
        }

        private void CreateHLine(string name, double price, Color color, LineStyle style, int width)
        {
            ObjectDelete(name);
            ObjectCreate(name, ObjectType.OBJ_HLINE, DateTime.MinValue, Normalize(price));
            ObjectSet(name, ObjectProperty.OBJPROP_COLOR, color);
            ObjectSet(name, ObjectProperty.OBJPROP_STYLE, style);
            ObjectSet(name, ObjectProperty.OBJPROP_WIDTH, Math.Max(1, width));
            ObjectSet(name, ObjectProperty.OBJPROP_LOCKED, LockObjects);
            ObjectSet(name, ObjectProperty.OBJPROP_SELECTABLE, SelectableObjects);
        }

        private void CreateTriangleText(string name, DateTime time, double price, string symbol, Color color)
        {
            ObjectDelete(name);
            ObjectCreate(name, ObjectType.OBJ_TEXT, time, Normalize(price));
            string font = MarkerBold ? "Segoe UI Semibold" : "Segoe UI";
            ObjectSetText(name, symbol, Math.Max(8, MarkerFontSize), font, color);
            ObjectSet(name, ObjectProperty.OBJPROP_COLOR, color);
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
