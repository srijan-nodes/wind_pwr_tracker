using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Management;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace PowerTracker
{
    public class Program : Form
    {
        private Timer timer;
        private ManagementObjectSearcher searcher;
        private NotifyIcon trayIcon;
        private MenuItem itemRecord;

        private double lastKnownDischargeRate = 12.0;

        // Recording state & direct disk writer
        private bool isRecording = false;
        private StreamWriter recWriter = null;

        // Active Live Graph Form Instance
        private GraphForm liveGraphForm = null;

        // Dragging Variables
        private bool isDragging = false;
        private Point dragCursorPoint;
        private Point dragFormPoint;

        // Current telemetry values for painting
        private double curIn = 0, curOut = 0, curNet = 0;
        private bool isCharging = false;

        // Hit-test regions for clickable icons
        private Rectangle rectRec = Rectangle.Empty;
        private Rectangle rectGraph = Rectangle.Empty;
        private Rectangle rectPos = Rectangle.Empty;

        // Hover tracking
        private bool hoverRec = false, hoverGraph = false, hoverPos = false;

        // Widget state: 0=collapsed, 1=normal, 2=expanded
        private int widgetState = 1;
        private Size collapsedSize = new Size(300, 8);
        private Size normalSize = new Size(300, 62);
        private Size expandedSize = new Size(300, 110);

        // Blink timer for recording indicator
        private Timer blinkTimer;
        private bool recDotVisible = true;

        public Program()
        {
            this.Text = "Power Widget";
            this.Size = new Size(300, 62);
            this.FormBorderStyle = FormBorderStyle.None;
            this.TopMost = true;
            this.ShowInTaskbar = false;
            this.BackColor = Color.Black;
            this.Opacity = 0.95;
            this.DoubleBuffered = true;

            // Make form region a rounded rectangle
            this.Region = CreateRoundedRegion(this.Width, this.Height, 12);
            this.Resize += (s, e) => { this.Region = CreateRoundedRegion(this.Width, this.Height, 12); };

            Rectangle workingArea = Screen.PrimaryScreen.WorkingArea;
            this.Location = new Point(workingArea.Right - this.Width - 20, workingArea.Bottom - this.Height - 20);

            this.MouseDown += Form_MouseDown;
            this.MouseMove += Form_MouseMove;
            this.MouseUp += Form_MouseUp;
            this.MouseClick += Form_MouseClick;

            // --- Context Menu ---
            ContextMenu ctx = new ContextMenu();
            itemRecord = new MenuItem("Start Recording", (s, e) => ToggleRecording());
            ctx.MenuItems.Add(itemRecord);
            ctx.MenuItems.Add("View Live Graph", (s, e) => OpenLiveGraph());
            ctx.MenuItems.Add("Open Saved CSV Graph...", (s, e) => OpenSavedCsvGraph());
            ctx.MenuItems.Add("Open Graphs Folder", (s, e) => OpenGraphsFolder());
            ctx.MenuItems.Add("-");
            ctx.MenuItems.Add("Toggle Widget", (s, e) => { this.Visible = !this.Visible; });
            ctx.MenuItems.Add("Snap to Corner", (s, e) => SnapToCorner());
            ctx.MenuItems.Add("-");
            ctx.MenuItems.Add("Exit", (s, e) => ExitApp());

            this.ContextMenu = ctx;

            // System Tray
            trayIcon = new NotifyIcon();
            trayIcon.Icon = SystemIcons.Application;
            trayIcon.Text = "Power Telemetry Widget";
            trayIcon.ContextMenu = ctx;
            trayIcon.Visible = true;
            trayIcon.DoubleClick += (s, e) => { this.Visible = !this.Visible; };

            // Initialize WMI
            try
            {
                searcher = new ManagementObjectSearcher("root\\wmi", "SELECT * FROM BatteryStatus");
            }
            catch { }

            // Timer Loop
            timer = new Timer();
            timer.Interval = 1000;
            timer.Tick += UpdateTelemetry;
            timer.Start();

            // Recording blink timer
            blinkTimer = new Timer();
            blinkTimer.Interval = 500;
            blinkTimer.Tick += (s, e) => { recDotVisible = !recDotVisible; this.Invalidate(); };

            UpdateTelemetry(null, null);
        }

        private static Region CreateRoundedRegion(int w, int h, int r)
        {
            GraphicsPath path = new GraphicsPath();
            path.AddArc(0, 0, r * 2, r * 2, 180, 90);
            path.AddArc(w - r * 2, 0, r * 2, r * 2, 270, 90);
            path.AddArc(w - r * 2, h - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(0, h - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            return new Region(path);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int w = this.Width, h = this.Height;

            // --- Background gradient ---
            using (LinearGradientBrush bg = new LinearGradientBrush(
                new Point(0, 0), new Point(w, h),
                Color.FromArgb(24, 26, 32), Color.FromArgb(16, 17, 22)))
            {
                FillRoundedRect(g, bg, 0, 0, w, h, 12);
            }

            // --- Subtle border ---
            using (Pen borderPen = new Pen(Color.FromArgb(55, 60, 75), 1.2f))
            {
                DrawRoundedRect(g, borderPen, 0, 0, w - 1, h - 1, 4);
            }

            // === COLLAPSED: just a thin accent strip ===
            if (widgetState == 0)
            {
                Color barColor = isCharging ? Color.FromArgb(76, 175, 80) : Color.FromArgb(244, 67, 54);
                if (isRecording)
                {
                    // Pulsing red strip when recording
                    barColor = recDotVisible ? Color.FromArgb(244, 67, 54) : Color.FromArgb(180, 40, 30);
                }
                using (LinearGradientBrush barBrush = new LinearGradientBrush(
                    new Point(0, 0), new Point(w, 0), barColor, Color.FromArgb(80, barColor)))
                {
                    g.FillRectangle(barBrush, 1, 1, w - 2, h - 2);
                }
                return; // Nothing else to draw
            }

            // --- Left accent: bolt icon (lights up when charging) ---
            DrawBoltIcon(g, 6, 10, isCharging);

            // === METRIC: Power IN ===
            int col1X = 26;
            DrawMetricBlock(g, col1X, "IN", String.Format("{0:F1}", curIn), "W",
                Color.FromArgb(76, 175, 80), Color.FromArgb(40, 76, 175, 80));

            // === METRIC: Power OUT ===
            int col2X = 96;
            DrawMetricBlock(g, col2X, "OUT", String.Format("{0:F1}", curOut), "W",
                Color.FromArgb(244, 67, 54), Color.FromArgb(40, 244, 67, 54));

            // === METRIC: Net Flow ===
            int col3X = 170;
            string netPrefix = curNet > 0 ? "+" : "";
            Color netColor = curNet > 0 ? Color.FromArgb(76, 175, 80) : (curNet < 0 ? Color.FromArgb(244, 67, 54) : Color.FromArgb(150, 150, 160));
            DrawMetricBlock(g, col3X, "NET", String.Format("{0}{1:F1}", netPrefix, curNet), "W",
                netColor, Color.FromArgb(40, netColor));

            // === Position kebab button (⋮) ===
            int posX = 232, posY = 20;
            rectPos = new Rectangle(posX - 4, posY - 14, 16, 36);
            DrawKebabIcon(g, posX, posY, hoverPos);

            // === Right side: REC button ===
            int btnX = 250, recY = 8;
            rectRec = new Rectangle(btnX - 4, recY - 2, 52, 22);

            Color recBg = hoverRec ? Color.FromArgb(50, 52, 60) : Color.FromArgb(36, 38, 46);
            using (SolidBrush rb = new SolidBrush(recBg))
            {
                FillRoundedRect(g, rb, rectRec.X, rectRec.Y, rectRec.Width, rectRec.Height, 6);
            }

            // Record dot
            if (isRecording)
            {
                if (recDotVisible)
                {
                    using (SolidBrush dotBrush = new SolidBrush(Color.FromArgb(244, 67, 54)))
                    {
                        g.FillEllipse(dotBrush, btnX, recY + 4, 10, 10);
                    }
                }
                using (Font f = new Font("Segoe UI", 7.5F, FontStyle.Bold))
                    g.DrawString("REC", f, new SolidBrush(Color.FromArgb(244, 67, 54)), btnX + 13, recY + 2);
            }
            else
            {
                using (SolidBrush dotBrush = new SolidBrush(Color.FromArgb(100, 105, 115)))
                {
                    g.FillEllipse(dotBrush, btnX, recY + 4, 10, 10);
                }
                using (Font f = new Font("Segoe UI", 7.5F, FontStyle.Bold))
                    g.DrawString("REC", f, new SolidBrush(Color.FromArgb(130, 135, 145)), btnX + 13, recY + 2);
            }

            // === Right side: GRAPH button ===
            int graphY = 34;
            rectGraph = new Rectangle(btnX - 4, graphY - 2, 52, 22);

            Color graphBg = hoverGraph ? Color.FromArgb(50, 52, 60) : Color.FromArgb(36, 38, 46);
            using (SolidBrush gb = new SolidBrush(graphBg))
            {
                FillRoundedRect(g, gb, rectGraph.X, rectGraph.Y, rectGraph.Width, rectGraph.Height, 6);
            }

            DrawMiniChartIcon(g, btnX + 1, graphY + 3, Color.FromArgb(33, 150, 243));

            using (Font f = new Font("Segoe UI", 7.5F, FontStyle.Bold))
                g.DrawString("LIVE", f, new SolidBrush(Color.FromArgb(33, 150, 243)), btnX + 16, graphY + 2);

            // === Expanded section ===
            if (widgetState == 2)
            {
                int ey = 62;
                // Separator line
                using (Pen sep = new Pen(Color.FromArgb(45, 50, 60), 1))
                {
                    g.DrawLine(sep, 12, ey, w - 12, ey);
                }

                // Status info in expanded area
                using (Font sf = new Font("Segoe UI", 8F, FontStyle.Bold))
                {
                    Color chargingColor = isCharging ? Color.FromArgb(76, 175, 80) : Color.FromArgb(244, 67, 54);
                    string statusText = isCharging ? "CHARGING" : "ON BATTERY";
                    g.DrawString(statusText, sf, new SolidBrush(chargingColor), 14, ey + 6);

                    string recStatus = isRecording ? "REC: ON" : "REC: OFF";
                    Color recStatusColor = isRecording ? Color.FromArgb(244, 67, 54) : Color.FromArgb(100, 105, 115);
                    g.DrawString(recStatus, sf, new SolidBrush(recStatusColor), 110, ey + 6);

                    string topStatus = this.TopMost ? "PINNED" : "UNPINNED";
                    Color topColor = this.TopMost ? Color.FromArgb(255, 193, 7) : Color.FromArgb(100, 105, 115);
                    g.DrawString(topStatus, sf, new SolidBrush(topColor), 190, ey + 6);
                }

                // Efficiency bar (Net / In ratio when charging)
                int barY = ey + 26;
                using (Font lf = new Font("Segoe UI", 7F, FontStyle.Regular))
                {
                    g.DrawString("Efficiency", lf, new SolidBrush(Color.FromArgb(100, 105, 115)), 14, barY);
                }
                int barX = 74, barW = 200, barH = 6;
                using (SolidBrush bgBar = new SolidBrush(Color.FromArgb(35, 38, 48)))
                {
                    FillRoundedRect(g, bgBar, barX, barY + 2, barW, barH, 3);
                }
                double efficiency = (curIn > 0.1) ? Math.Max(0, Math.Min(1, (curIn - curOut) / curIn)) : 0;
                int fillW = (int)(barW * efficiency);
                if (fillW > 0)
                {
                    Color effColor = efficiency > 0.5 ? Color.FromArgb(76, 175, 80) : Color.FromArgb(255, 193, 7);
                    using (SolidBrush fillBar = new SolidBrush(effColor))
                    {
                        FillRoundedRect(g, fillBar, barX, barY + 2, fillW, barH, 3);
                    }
                }
            }
        }

        private void DrawMetricBlock(Graphics g, int x, string label, string value, string unit,
            Color accentColor, Color barColor)
        {
            // Header label
            using (Font hf = new Font("Segoe UI", 6.5F, FontStyle.Bold))
            using (SolidBrush hb = new SolidBrush(Color.FromArgb(120, 125, 135)))
            {
                g.DrawString(label, hf, hb, x, 8);
            }

            // Value
            using (Font vf = new Font("Segoe UI", 12F, FontStyle.Bold))
            using (SolidBrush vb = new SolidBrush(accentColor))
            {
                g.DrawString(value, vf, vb, x - 2, 20);
            }

            // Unit suffix
            SizeF valSize;
            using (Font vf = new Font("Segoe UI", 12F, FontStyle.Bold))
                valSize = g.MeasureString(value, vf);

            using (Font uf = new Font("Segoe UI", 7F, FontStyle.Regular))
            using (SolidBrush ub = new SolidBrush(Color.FromArgb(100, 105, 115)))
            {
                g.DrawString(unit, uf, ub, x - 2 + valSize.Width - 4, 30);
            }

            // Accent bar at bottom
            using (SolidBrush bb = new SolidBrush(barColor))
            {
                FillRoundedRect(g, bb, x, 50, 55, 3, 1);
            }
        }

        private void DrawBoltIcon(Graphics g, int x, int y, bool charging)
        {
            // Refined lightning bolt shape
            PointF[] bolt = {
                new PointF(x + 10, y),
                new PointF(x + 4,  y + 16),
                new PointF(x + 9,  y + 16),
                new PointF(x + 3,  y + 36),
                new PointF(x + 15, y + 14),
                new PointF(x + 10, y + 14),
                new PointF(x + 14, y)
            };

            if (charging)
            {
                // Outer glow layer
                using (Pen glowPen = new Pen(Color.FromArgb(60, 255, 193, 7), 5))
                {
                    glowPen.LineJoin = LineJoin.Round;
                    g.DrawPolygon(glowPen, bolt);
                }

                // Bright fill
                using (LinearGradientBrush fill = new LinearGradientBrush(
                    new Point(x, y), new Point(x, y + 36),
                    Color.FromArgb(255, 235, 59), Color.FromArgb(255, 160, 0)))
                {
                    g.FillPolygon(fill, bolt);
                }

                // Crisp edge
                using (Pen edgePen = new Pen(Color.FromArgb(180, 255, 193, 7), 1))
                {
                    edgePen.LineJoin = LineJoin.Round;
                    g.DrawPolygon(edgePen, bolt);
                }
            }
            else
            {
                // Dim / off state
                using (SolidBrush dimBrush = new SolidBrush(Color.FromArgb(50, 55, 65)))
                {
                    g.FillPolygon(dimBrush, bolt);
                }
                using (Pen dimPen = new Pen(Color.FromArgb(65, 70, 80), 1))
                {
                    dimPen.LineJoin = LineJoin.Round;
                    g.DrawPolygon(dimPen, bolt);
                }
            }
        }

        private void DrawMiniChartIcon(Graphics g, int x, int y, Color c)
        {
            using (SolidBrush b = new SolidBrush(c))
            {
                g.FillRectangle(b, x, y + 10, 3, 4);
                g.FillRectangle(b, x + 4, y + 6, 3, 8);
                g.FillRectangle(b, x + 8, y + 2, 3, 12);
                g.FillRectangle(b, x + 12, y + 5, 3, 9);
            }
        }

        private void DrawKebabIcon(Graphics g, int x, int y, bool hover)
        {
            Color dotColor = hover ? Color.FromArgb(200, 205, 215) : Color.FromArgb(110, 115, 125);
            using (SolidBrush b = new SolidBrush(dotColor))
            {
                g.FillEllipse(b, x, y - 8, 5, 5);
                g.FillEllipse(b, x, y,     5, 5);
                g.FillEllipse(b, x, y + 8, 5, 5);
            }
        }

        private void FillRoundedRect(Graphics g, Brush b, int x, int y, int w, int h, int r)
        {
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddArc(x, y, r * 2, r * 2, 180, 90);
                path.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
                path.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
                path.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
                path.CloseFigure();
                g.FillPath(b, path);
            }
        }

        private void DrawRoundedRect(Graphics g, Pen p, int x, int y, int w, int h, int r)
        {
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddArc(x, y, r * 2, r * 2, 180, 90);
                path.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
                path.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
                path.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
                path.CloseFigure();
                g.DrawPath(p, path);
            }
        }

        private void Form_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                // If collapsed, click anywhere to restore
                if (widgetState == 0)
                {
                    SetWidgetState(1);
                    return;
                }
                if (rectRec.Contains(e.Location)) ToggleRecording();
                else if (rectGraph.Contains(e.Location)) OpenLiveGraph();
                else if (rectPos.Contains(e.Location)) ShowPositionMenu(e.Location);
            }
        }

        private void ShowPositionMenu(Point loc)
        {
            ContextMenu posMenu = new ContextMenu();

            if (widgetState != 2)
                posMenu.MenuItems.Add("Expand", (s, ev) => SetWidgetState(2));
            if (widgetState != 1)
                posMenu.MenuItems.Add("Normal", (s, ev) => SetWidgetState(1));
            posMenu.MenuItems.Add("Collapse", (s, ev) => SetWidgetState(0));

            posMenu.MenuItems.Add("-");
            posMenu.MenuItems.Add("Minimize to Tray", (s, ev) => { this.Visible = false; });

            string pinText = this.TopMost ? "Send to Back (Unpin)" : "Bring to Front (Pin)";
            posMenu.MenuItems.Add(pinText, (s, ev) => { this.TopMost = !this.TopMost; this.Invalidate(); });

            posMenu.MenuItems.Add("-");
            posMenu.MenuItems.Add("Snap to Corner", (s, ev) => SnapToCorner());

            posMenu.Show(this, loc);
        }

        private void SetWidgetState(int state)
        {
            widgetState = state;
            if (state == 0) this.Size = collapsedSize;
            else if (state == 1) this.Size = normalSize;
            else this.Size = expandedSize;
            int radius = (state == 0) ? 4 : 12;
            this.Region = CreateRoundedRegion(this.Width, this.Height, radius);
            this.Invalidate();
        }

        private void ToggleRecording()
        {
            isRecording = !isRecording;

            if (isRecording)
            {
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                string baseFolder = Path.Combine(appDir, "Charging Stats");
                Directory.CreateDirectory(baseFolder);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string csvPath = Path.Combine(baseFolder, String.Format("PowerData_{0}.csv", timestamp));

                try
                {
                    recWriter = new StreamWriter(csvPath, true);
                    recWriter.WriteLine("Time,PowerIn_W,PowerOut_W,NetFlow_W");
                    recWriter.Flush();
                }
                catch { }

                itemRecord.Text = "Stop Recording";
                blinkTimer.Start();
                trayIcon.ShowBalloonTip(2000, "PowerTracker", "Recording session started...", ToolTipIcon.Info);
            }
            else
            {
                itemRecord.Text = "Start Recording";
                blinkTimer.Stop();
                recDotVisible = true;

                if (recWriter != null)
                {
                    try { recWriter.Flush(); recWriter.Close(); recWriter.Dispose(); }
                    catch { }
                    recWriter = null;
                }

                trayIcon.ShowBalloonTip(3000, "Recording Saved!", "CSV log saved to Charging Stats folder.", ToolTipIcon.Info);
            }

            this.Invalidate();
        }

        private void OpenLiveGraph()
        {
            if (liveGraphForm == null || liveGraphForm.IsDisposed)
            {
                liveGraphForm = new GraphForm();
                liveGraphForm.Show();
            }
            else
            {
                liveGraphForm.BringToFront();
                liveGraphForm.WindowState = FormWindowState.Normal;
            }
        }

        private void OpenSavedCsvGraph()
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Filter = "CSV Log Files (*.csv)|*.csv|All Files (*.*)|*.*";
                string defaultFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Charging Stats");
                if (Directory.Exists(defaultFolder))
                    ofd.InitialDirectory = defaultFolder;

                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    GraphForm csvForm = new GraphForm();
                    csvForm.LoadCsvFile(ofd.FileName);
                    csvForm.Show();
                }
            }
        }

        private void OpenGraphsFolder()
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string graphsFolder = Path.Combine(appDir, "Charging Stats");
            Directory.CreateDirectory(graphsFolder);
            try { System.Diagnostics.Process.Start(graphsFolder); }
            catch { }
        }

        private void UpdateTelemetry(object sender, EventArgs e)
        {
            try
            {
                foreach (ManagementObject queryObj in searcher.Get())
                {
                    bool online = Convert.ToBoolean(queryObj["PowerOnline"]);
                    double rawChargeRate = Convert.ToDouble(queryObj["ChargeRate"]) / 1000.0;
                    double rawDischargeRate = Convert.ToDouble(queryObj["DischargeRate"]) / 1000.0;

                    isCharging = online;

                    if (online)
                    {
                        curNet = rawChargeRate > 0 ? rawChargeRate : 0.0;
                        if (rawDischargeRate > 0) lastKnownDischargeRate = rawDischargeRate;
                        curOut = lastKnownDischargeRate;
                        curIn = curNet + curOut;
                    }
                    else
                    {
                        curIn = 0.0;
                        curOut = rawDischargeRate > 0 ? rawDischargeRate : 0.0;
                        curNet = -curOut;
                        if (curOut > 0) lastKnownDischargeRate = curOut;
                    }

                    string formattedNet = String.Format("{0}{1:F1}W", (curNet > 0 ? "+" : ""), curNet);
                    trayIcon.Text = String.Format("Power Net: {0}", formattedNet);

                    string currentTimeStr = DateTime.Now.ToString("HH:mm:ss");

                    if (isRecording && recWriter != null)
                    {
                        try
                        {
                            recWriter.WriteLine(String.Format("{0},{1:F2},{2:F2},{3:F2}",
                                currentTimeStr, curIn, curOut, curNet));
                            recWriter.Flush();
                        }
                        catch { }
                    }

                    if (liveGraphForm != null && !liveGraphForm.IsDisposed)
                        liveGraphForm.AddLivePoint(curIn, curOut, curNet, currentTimeStr);

                    this.Invalidate();
                }
            }
            catch { }
        }

        private void SnapToCorner()
        {
            Rectangle workingArea = Screen.PrimaryScreen.WorkingArea;
            this.Location = new Point(workingArea.Right - this.Width - 20, workingArea.Bottom - this.Height - 20);
            this.Visible = true;
        }

        private void ExitApp()
        {
            if (isRecording) ToggleRecording();
            trayIcon.Visible = false;
            Application.Exit();
        }

        private void Form_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && !rectRec.Contains(e.Location) && !rectGraph.Contains(e.Location) && !rectPos.Contains(e.Location))
            {
                isDragging = true;
                dragCursorPoint = System.Windows.Forms.Cursor.Position;
                dragFormPoint = this.Location;
            }
        }

        private void Form_MouseMove(object sender, MouseEventArgs e)
        {
            if (isDragging)
            {
                Point dif = Point.Subtract(System.Windows.Forms.Cursor.Position, new Size(dragCursorPoint));
                this.Location = Point.Add(dragFormPoint, new Size(dif));
            }

            // Hover tracking
            bool newHoverRec = rectRec.Contains(e.Location);
            bool newHoverGraph = rectGraph.Contains(e.Location);
            bool newHoverPos = rectPos.Contains(e.Location);
            if (newHoverRec != hoverRec || newHoverGraph != hoverGraph || newHoverPos != hoverPos)
            {
                hoverRec = newHoverRec;
                hoverGraph = newHoverGraph;
                hoverPos = newHoverPos;
                this.Cursor = (hoverRec || hoverGraph || hoverPos) ? Cursors.Hand : Cursors.Default;
                this.Invalidate();
            }
        }

        private void Form_MouseUp(object sender, MouseEventArgs e)
        {
            isDragging = false;
        }

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.Run(new Program());
        }
    }

    // --- Interactive Vector Graph Form ---
    public class GraphForm : Form
    {
        private Chart chart;
        private CheckBox chkIn, chkOut, chkNet;
        private Button btnOpenCsv, btnResetZoom, btnExportCsv;
        private List<double> historyIn = new List<double>();
        private List<double> historyOut = new List<double>();
        private List<double> historyNet = new List<double>();
        private List<string> historyTime = new List<string>();

        private Label lblHoverReadout;

        public GraphForm()
        {
            this.Text = "Power Telemetry - Live Graph";
            this.Size = new Size(960, 600);
            this.MinimumSize = new Size(700, 450);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(20, 20, 24);
            this.ForeColor = Color.White;
            this.Icon = SystemIcons.Application;
            this.AllowDrop = true;

            this.DragEnter += Form_DragEnter;
            this.DragDrop += Form_DragDrop;

            InitializeControls();
            PlotData();
        }

        private void InitializeControls()
        {
            // Top Toolbar Panel
            Panel pnlTop = new Panel {
                Dock = DockStyle.Top,
                Height = 45,
                BackColor = Color.FromArgb(28, 28, 34),
                Padding = new Padding(10, 8, 10, 8)
            };

            btnOpenCsv = new Button {
                Text = "Open CSV...",
                ForeColor = Color.White,
                BackColor = Color.FromArgb(33, 150, 243),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Size = new Size(110, 26),
                Location = new Point(12, 8),
                Cursor = Cursors.Hand
            };
            btnOpenCsv.FlatAppearance.BorderSize = 0;
            btnOpenCsv.Click += (s, e) => BrowseAndLoadCsv();

            chkIn = new CheckBox {
                Text = "Power IN (W)",
                Checked = true,
                ForeColor = Color.FromArgb(76, 175, 80),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(135, 12),
                Cursor = Cursors.Hand
            };
            chkIn.CheckedChanged += (s, e) => ToggleSeriesVisibility();

            chkOut = new CheckBox {
                Text = "Power OUT (W)",
                Checked = true,
                ForeColor = Color.FromArgb(244, 67, 54),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(265, 12),
                Cursor = Cursors.Hand
            };
            chkOut.CheckedChanged += (s, e) => ToggleSeriesVisibility();

            chkNet = new CheckBox {
                Text = "Net Flow (W)",
                Checked = true,
                ForeColor = Color.FromArgb(33, 150, 243),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(405, 12),
                Cursor = Cursors.Hand
            };
            chkNet.CheckedChanged += (s, e) => ToggleSeriesVisibility();

            btnResetZoom = new Button {
                Text = "Reset Zoom",
                ForeColor = Color.White,
                BackColor = Color.FromArgb(45, 45, 55),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Size = new Size(100, 26),
                Location = new Point(545, 8),
                Cursor = Cursors.Hand
            };
            btnResetZoom.FlatAppearance.BorderSize = 0;
            btnResetZoom.Click += (s, e) => ResetZoom();

            btnExportCsv = new Button {
                Text = "Export CSV",
                ForeColor = Color.White,
                BackColor = Color.FromArgb(45, 45, 55),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Size = new Size(100, 26),
                Location = new Point(655, 8),
                Cursor = Cursors.Hand
            };
            btnExportCsv.FlatAppearance.BorderSize = 0;
            btnExportCsv.Click += (s, e) => ExportCsv();

            pnlTop.Controls.AddRange(new Control[] { btnOpenCsv, chkIn, chkOut, chkNet, btnResetZoom, btnExportCsv });
            this.Controls.Add(pnlTop);

            // Bottom Hover Values Readout Banner
            Panel pnlReadout = new Panel {
                Dock = DockStyle.Bottom,
                Height = 32,
                BackColor = Color.FromArgb(15, 15, 18),
                Padding = new Padding(15, 6, 15, 6)
            };

            lblHoverReadout = new Label {
                Text = "Move mouse over graph to inspect values...",
                ForeColor = Color.FromArgb(200, 200, 210),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };

            pnlReadout.Controls.Add(lblHoverReadout);
            this.Controls.Add(pnlReadout);

            // Chart Control
            chart = new Chart {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(20, 20, 24)
            };

            ChartArea area = new ChartArea("MainArea") {
                BackColor = Color.FromArgb(25, 25, 30)
            };

            area.AxisX.LabelStyle.ForeColor = Color.FromArgb(180, 180, 180);
            area.AxisX.LabelStyle.Font = new Font("Segoe UI", 8F);
            area.AxisX.MajorGrid.LineColor = Color.FromArgb(45, 45, 52);
            area.AxisX.Title = "Time";
            area.AxisX.TitleForeColor = Color.FromArgb(160, 160, 170);
            area.AxisX.TitleFont = new Font("Segoe UI", 9F, FontStyle.Bold);

            area.AxisY.LabelStyle.ForeColor = Color.FromArgb(180, 180, 180);
            area.AxisY.LabelStyle.Format = "{0:F1} W";
            area.AxisY.LabelStyle.Font = new Font("Segoe UI", 8F);
            area.AxisY.MajorGrid.LineColor = Color.FromArgb(45, 45, 52);
            area.AxisY.Title = "Power (Watts)";
            area.AxisY.TitleForeColor = Color.FromArgb(160, 160, 170);
            area.AxisY.TitleFont = new Font("Segoe UI", 9F, FontStyle.Bold);

            StripLine zeroLine = new StripLine {
                Interval = 0, IntervalOffset = 0, StripWidth = 0,
                BorderColor = Color.FromArgb(100, 100, 110),
                BorderWidth = 1, BorderDashStyle = ChartDashStyle.Dash
            };
            area.AxisY.StripLines.Add(zeroLine);

            area.CursorX.IsUserEnabled = true;
            area.CursorX.IsUserSelectionEnabled = true;
            area.CursorX.LineColor = Color.FromArgb(220, 0, 210, 255);
            area.CursorX.LineWidth = 1;
            area.CursorX.LineDashStyle = ChartDashStyle.Dash;
            area.AxisX.ScaleView.Zoomable = true;

            area.CursorY.IsUserEnabled = true;
            area.CursorY.IsUserSelectionEnabled = true;
            area.AxisY.ScaleView.Zoomable = true;

            chart.ChartAreas.Add(area);
            chart.MouseMove += Chart_MouseMove;

            Legend legend = new Legend("MainLegend") {
                BackColor = Color.Transparent, ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Docking = Docking.Top, Alignment = StringAlignment.Far
            };
            chart.Legends.Add(legend);

            Title title = new Title("Power Telemetry - Live Graph", Docking.Top,
                new Font("Segoe UI", 12F, FontStyle.Bold), Color.White);
            chart.Titles.Add(title);

            this.Controls.Add(chart);
            chart.BringToFront();
        }

        private void BrowseAndLoadCsv()
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Filter = "CSV Log Files (*.csv)|*.csv|All Files (*.*)|*.*";
                string defaultFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Charging Stats");
                if (Directory.Exists(defaultFolder))
                    ofd.InitialDirectory = defaultFolder;

                if (ofd.ShowDialog() == DialogResult.OK)
                    LoadCsvFile(ofd.FileName);
            }
        }

        public void LoadCsvFile(string csvPath)
        {
            try
            {
                historyIn.Clear(); historyOut.Clear();
                historyNet.Clear(); historyTime.Clear();

                string[] lines = File.ReadAllLines(csvPath);
                if (lines.Length <= 1) return;

                for (int i = 1; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (string.IsNullOrEmpty(line)) continue;
                    string[] parts = line.Split(',');
                    if (parts.Length >= 4)
                    {
                        double pIn, pOut, pNet;
                        if (double.TryParse(parts[1], out pIn) &&
                            double.TryParse(parts[2], out pOut) &&
                            double.TryParse(parts[3], out pNet))
                        {
                            historyTime.Add(parts[0].Trim());
                            historyIn.Add(pIn); historyOut.Add(pOut); historyNet.Add(pNet);
                        }
                    }
                }

                string fileName = Path.GetFileName(csvPath);
                this.Text = "Power Telemetry - " + fileName;
                if (chart.Titles.Count > 0)
                    chart.Titles[0].Text = "CSV Telemetry: " + fileName;

                PlotData();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to open CSV:\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Chart_MouseMove(object sender, MouseEventArgs e)
        {
            try
            {
                if (historyTime == null || historyTime.Count == 0) return;
                ChartArea area = chart.ChartAreas[0];
                double xVal = area.AxisX.PixelPositionToValue(e.X);
                int index = (int)Math.Round(xVal) - 1;

                if (index >= 0 && index < historyTime.Count)
                {
                    area.CursorX.Position = index + 1;
                    lblHoverReadout.Text = String.Format(
                        "Time: {0}   |   IN: {1:F2} W   |   OUT: {2:F2} W   |   Net: {3:+0.00;-0.00;0.00} W",
                        historyTime[index], historyIn[index], historyOut[index], historyNet[index]);
                }
            }
            catch { }
        }

        private void PlotData()
        {
            chart.Series.Clear();

            Series sIn = new Series("Power IN") {
                ChartType = SeriesChartType.Spline, Color = Color.FromArgb(76, 175, 80),
                BorderWidth = 3, MarkerStyle = MarkerStyle.Circle, MarkerSize = 5,
                ToolTip = "Time: #VALX\nPower IN: #VALY{F2} W"
            };
            Series sOut = new Series("Power OUT") {
                ChartType = SeriesChartType.Spline, Color = Color.FromArgb(244, 67, 54),
                BorderWidth = 3, MarkerStyle = MarkerStyle.Circle, MarkerSize = 5,
                ToolTip = "Time: #VALX\nPower OUT: #VALY{F2} W"
            };
            Series sNet = new Series("Net Flow") {
                ChartType = SeriesChartType.Spline, Color = Color.FromArgb(33, 150, 243),
                BorderWidth = 3, MarkerStyle = MarkerStyle.Circle, MarkerSize = 5,
                ToolTip = "Time: #VALX\nNet Flow: #VALY{F2} W"
            };

            for (int i = 0; i < historyTime.Count; i++)
            {
                sIn.Points.AddXY(historyTime[i], historyIn[i]);
                sOut.Points.AddXY(historyTime[i], historyOut[i]);
                sNet.Points.AddXY(historyTime[i], historyNet[i]);
            }

            chart.Series.Add(sIn);
            chart.Series.Add(sOut);
            chart.Series.Add(sNet);
            ToggleSeriesVisibility();
        }

        public void AddLivePoint(double pIn, double pOut, double pNet, string timeStr)
        {
            historyIn.Add(pIn); historyOut.Add(pOut);
            historyNet.Add(pNet); historyTime.Add(timeStr);

            if (chart.Series.Count >= 3)
            {
                chart.Series["Power IN"].Points.AddXY(timeStr, pIn);
                chart.Series["Power OUT"].Points.AddXY(timeStr, pOut);
                chart.Series["Net Flow"].Points.AddXY(timeStr, pNet);
            }
        }

        private void ToggleSeriesVisibility()
        {
            try
            {
                if (chart.Series.IndexOf("Power IN") != -1) chart.Series["Power IN"].Enabled = chkIn.Checked;
                if (chart.Series.IndexOf("Power OUT") != -1) chart.Series["Power OUT"].Enabled = chkOut.Checked;
                if (chart.Series.IndexOf("Net Flow") != -1) chart.Series["Net Flow"].Enabled = chkNet.Checked;
                chart.Invalidate(); chart.Update();
            }
            catch { }
        }

        private void ResetZoom()
        {
            if (chart.ChartAreas.Count > 0)
            {
                chart.ChartAreas[0].AxisX.ScaleView.ZoomReset(0);
                chart.ChartAreas[0].AxisY.ScaleView.ZoomReset(0);
                chart.Invalidate();
            }
        }

        private void ExportCsv()
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string baseFolder = Path.Combine(appDir, "Charging Stats");
            Directory.CreateDirectory(baseFolder);

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string csvPath = Path.Combine(baseFolder, String.Format("PowerData_{0}.csv", timestamp));

            using (StreamWriter sw = new StreamWriter(csvPath))
            {
                sw.WriteLine("Time,PowerIn_W,PowerOut_W,NetFlow_W");
                for (int i = 0; i < historyTime.Count; i++)
                    sw.WriteLine(String.Format("{0},{1:F2},{2:F2},{3:F2}",
                        historyTime[i], historyIn[i], historyOut[i], historyNet[i]));
            }

            MessageBox.Show("CSV exported to:\n" + csvPath, "PowerTracker", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void Form_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
        }

        private void Form_DragDrop(object sender, DragEventArgs e)
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0 && files[0].EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                LoadCsvFile(files[0]);
        }
    }
}