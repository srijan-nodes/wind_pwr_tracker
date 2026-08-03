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
        private Label lblIn, lblOut, lblNet, lblRec, lblViewGraph;
        private Timer timer;
        private ManagementObjectSearcher searcher;
        private NotifyIcon trayIcon;
        private MenuItem itemRecord;

        private double lastKnownDischargeRate = 12.0;

        // Recording state & direct disk writer (No RAM session buffering!)
        private bool isRecording = false;
        private StreamWriter recWriter = null;

        // Active Live Graph Form Instance
        private GraphForm liveGraphForm = null;

        // Dragging Variables
        private bool isDragging = false;
        private Point dragCursorPoint;
        private Point dragFormPoint;

        public Program()
        {
            // --- Widget Window Config ---
            this.Text = "Power Widget";
            this.Size = new Size(270, 52);
            this.FormBorderStyle = FormBorderStyle.None;
            this.TopMost = true;
            this.ShowInTaskbar = false;
            this.BackColor = Color.FromArgb(20, 20, 20);
            this.Opacity = 0.90;
            this.DoubleBuffered = true;

            // Position at Bottom-Right of Screen
            Rectangle workingArea = Screen.PrimaryScreen.WorkingArea;
            this.Location = new Point(workingArea.Right - this.Width - 20, workingArea.Bottom - this.Height - 20);

            // Dragging Handlers
            this.MouseDown += Form_MouseDown;
            this.MouseMove += Form_MouseMove;
            this.MouseUp += Form_MouseUp;

            // --- UI Layout ---
            Label hIn = CreateHeaderLabel("IN", 12, 8);
            lblIn = CreateValueLabel("0.0W", 12, 22, Color.FromArgb(76, 175, 80));

            Label hOut = CreateHeaderLabel("OUT", 78, 8);
            lblOut = CreateValueLabel("0.0W", 78, 22, Color.FromArgb(244, 67, 54));

            Label hNet = CreateHeaderLabel("NET", 144, 8);
            lblNet = CreateValueLabel("0.0W", 144, 22, Color.FromArgb(33, 150, 243));

            // REC Toggle Button Badge (Grey when idle, Red when recording)
            lblRec = new Label {
                Text = "● REC",
                ForeColor = Color.Gray,
                Location = new Point(204, 8),
                AutoSize = true,
                Visible = true,
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 7.5F, FontStyle.Bold)
            };
            lblRec.Click += (s, e) => ToggleRecording();

            // View Graph Button Badge (Directly below Record button on widget)
            lblViewGraph = new Label {
                Text = "📈 GRAPH",
                ForeColor = Color.FromArgb(33, 150, 243),
                Location = new Point(204, 27),
                AutoSize = true,
                Visible = true,
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 7.5F, FontStyle.Bold)
            };
            lblViewGraph.Click += (s, e) => OpenLiveGraph();

            // --- Context Menu ---
            ContextMenu ctx = new ContextMenu();
            itemRecord = new MenuItem("🔴 Start Recording", (s, e) => ToggleRecording());
            ctx.MenuItems.Add(itemRecord);
            ctx.MenuItems.Add("📈 View Live Graph", (s, e) => OpenLiveGraph());
            ctx.MenuItems.Add("📊 Open Saved CSV Graph...", (s, e) => OpenSavedCsvGraph());
            ctx.MenuItems.Add("📁 Open Graphs Folder", (s, e) => OpenGraphsFolder());
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

            this.Controls.AddRange(new Control[] { hIn, hOut, hNet, lblIn, lblOut, lblNet, lblRec, lblViewGraph });

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

            UpdateTelemetry(null, null);
        }

        private Label CreateHeaderLabel(string text, int x, int y)
        {
            Label lbl = new Label {
                Text = text,
                ForeColor = Color.FromArgb(140, 140, 140),
                Location = new Point(x, y),
                AutoSize = true,
                Font = new Font("Segoe UI", 6.5F, FontStyle.Bold)
            };
            lbl.MouseDown += Form_MouseDown;
            lbl.MouseMove += Form_MouseMove;
            lbl.MouseUp += Form_MouseUp;
            return lbl;
        }

        private Label CreateValueLabel(string text, int x, int y, Color color)
        {
            Label lbl = new Label {
                Text = text,
                ForeColor = color,
                Location = new Point(x, y),
                AutoSize = true,
                Font = new Font("Segoe UI", 10.5F, FontStyle.Bold)
            };
            lbl.MouseDown += Form_MouseDown;
            lbl.MouseMove += Form_MouseMove;
            lbl.MouseUp += Form_MouseUp;
            return lbl;
        }

        private void ToggleRecording()
        {
            isRecording = !isRecording;

            if (isRecording)
            {
                // Start Session Stream to Disk
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

                lblRec.Text = "🔴 REC";
                lblRec.ForeColor = Color.Crimson;
                itemRecord.Text = "⏹️ Stop Recording";
                trayIcon.ShowBalloonTip(2000, "PowerTracker", "🔴 Recording session started...", ToolTipIcon.Info);
            }
            else
            {
                // Stop Session & Close Writer (DO NOT LAUNCH GRAPH)
                lblRec.Text = "● REC";
                lblRec.ForeColor = Color.Gray;
                itemRecord.Text = "🔴 Start Recording";

                if (recWriter != null)
                {
                    try
                    {
                        recWriter.Flush();
                        recWriter.Close();
                        recWriter.Dispose();
                    }
                    catch { }
                    recWriter = null;
                }

                trayIcon.ShowBalloonTip(3000, "Recording Saved!", "CSV log saved to Charging Stats folder.", ToolTipIcon.Info);
            }
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
                {
                    ofd.InitialDirectory = defaultFolder;
                }

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

            try
            {
                System.Diagnostics.Process.Start(graphsFolder);
            }
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

                    double pIn = 0.0;
                    double pOut = 0.0;
                    double pNet = 0.0;

                    if (online)
                    {
                        pNet = rawChargeRate > 0 ? rawChargeRate : 0.0;
                        if (rawDischargeRate > 0) lastKnownDischargeRate = rawDischargeRate;
                        pOut = lastKnownDischargeRate;
                        pIn = pNet + pOut;
                    }
                    else
                    {
                        pIn = 0.0;
                        pOut = rawDischargeRate > 0 ? rawDischargeRate : 0.0;
                        pNet = -pOut;
                        if (pOut > 0) lastKnownDischargeRate = pOut;
                    }

                    string formattedNet = String.Format("{0}{1:F1}W", (pNet > 0 ? "+" : ""), pNet);

                    lblIn.Text = String.Format("{0:F1}W", pIn);
                    lblOut.Text = String.Format("{0:F1}W", pOut);
                    lblNet.Text = formattedNet;
                    lblNet.ForeColor = pNet > 0 ? Color.FromArgb(76, 175, 80) : (pNet < 0 ? Color.FromArgb(244, 67, 54) : Color.Gray);

                    trayIcon.Text = String.Format("Power Net: {0}", formattedNet);

                    string currentTimeStr = DateTime.Now.ToString("HH:mm:ss");

                    // Direct disk write if recording active
                    if (isRecording && recWriter != null)
                    {
                        try
                        {
                            recWriter.WriteLine(String.Format("{0},{1:F2},{2:F2},{3:F2}",
                                currentTimeStr, pIn, pOut, pNet));
                            recWriter.Flush();
                        }
                        catch { }
                    }

                    // Push live points ONLY when live graph window is open
                    if (liveGraphForm != null && !liveGraphForm.IsDisposed)
                    {
                        liveGraphForm.AddLivePoint(pIn, pOut, pNet, currentTimeStr);
                    }
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

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using (Pen pen = new Pen(Color.FromArgb(60, 60, 65), 1))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, this.Width - 1, this.Height - 1);
            }
        }

        private void Form_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
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
            this.Text = "⚡ Power Telemetry - Live Graph (From Point of Launch)";
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
                Text = "📂 Open CSV...",
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
                Text = "🔍 Reset Zoom",
                ForeColor = Color.White,
                BackColor = Color.FromArgb(45, 45, 55),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Size = new Size(110, 26),
                Location = new Point(535, 8),
                Cursor = Cursors.Hand
            };
            btnResetZoom.FlatAppearance.BorderSize = 0;
            btnResetZoom.Click += (s, e) => ResetZoom();

            btnExportCsv = new Button {
                Text = "💾 Export CSV",
                ForeColor = Color.White,
                BackColor = Color.FromArgb(45, 45, 55),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Size = new Size(110, 26),
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
                Text = "💡 Move mouse over any point on the graph to view instant telemetry values...",
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

            // Axes setup
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

            // Zero Baseline StripLine
            StripLine zeroLine = new StripLine {
                Interval = 0,
                IntervalOffset = 0,
                StripWidth = 0,
                BorderColor = Color.FromArgb(100, 100, 110),
                BorderWidth = 1,
                BorderDashStyle = ChartDashStyle.Dash
            };
            area.AxisY.StripLines.Add(zeroLine);

            // Zooming, Selection & Crosshair Cursor
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

            // Legend
            Legend legend = new Legend("MainLegend") {
                BackColor = Color.Transparent,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Docking = Docking.Top,
                Alignment = StringAlignment.Far
            };
            chart.Legends.Add(legend);

            // Title
            Title title = new Title("Power Telemetry - Live Graph", Docking.Top, new Font("Segoe UI", 12F, FontStyle.Bold), Color.White);
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
                {
                    ofd.InitialDirectory = defaultFolder;
                }

                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    LoadCsvFile(ofd.FileName);
                }
            }
        }

        public void LoadCsvFile(string csvPath)
        {
            try
            {
                historyIn.Clear();
                historyOut.Clear();
                historyNet.Clear();
                historyTime.Clear();

                string[] lines = File.ReadAllLines(csvPath);
                if (lines.Length <= 1) return;

                for (int i = 1; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (string.IsNullOrEmpty(line)) continue;

                    string[] parts = line.Split(',');
                    if (parts.Length >= 4)
                    {
                        string t = parts[0].Trim();
                        double pIn, pOut, pNet;

                        if (double.TryParse(parts[1], out pIn) &&
                            double.TryParse(parts[2], out pOut) &&
                            double.TryParse(parts[3], out pNet))
                        {
                            historyTime.Add(t);
                            historyIn.Add(pIn);
                            historyOut.Add(pOut);
                            historyNet.Add(pNet);
                        }
                    }
                }

                string fileName = Path.GetFileName(csvPath);
                this.Text = "⚡ Power Telemetry - CSV File: " + fileName;
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
                    string timeStr = historyTime[index];
                    double pIn = historyIn[index];
                    double pOut = historyOut[index];
                    double pNet = historyNet[index];

                    area.CursorX.Position = index + 1;

                    lblHoverReadout.Text = String.Format(
                        "⏱️ Time: {0}   │   🟢 Power IN: {1:F2} W   │   🔴 Power OUT: {2:F2} W   │   🔵 Net Flow: {3:+0.00;-0.00;0.00} W",
                        timeStr, pIn, pOut, pNet
                    );
                }
            }
            catch { }
        }

        private void PlotData()
        {
            chart.Series.Clear();

            Series sIn = new Series("Power IN") {
                ChartType = SeriesChartType.Spline,
                Color = Color.FromArgb(76, 175, 80),
                BorderWidth = 3,
                MarkerStyle = MarkerStyle.Circle,
                MarkerSize = 5,
                ToolTip = "Time: #VALX\nPower IN: #VALY{F2} W"
            };

            Series sOut = new Series("Power OUT") {
                ChartType = SeriesChartType.Spline,
                Color = Color.FromArgb(244, 67, 54),
                BorderWidth = 3,
                MarkerStyle = MarkerStyle.Circle,
                MarkerSize = 5,
                ToolTip = "Time: #VALX\nPower OUT: #VALY{F2} W"
            };

            Series sNet = new Series("Net Flow") {
                ChartType = SeriesChartType.Spline,
                Color = Color.FromArgb(33, 150, 243),
                BorderWidth = 3,
                MarkerStyle = MarkerStyle.Circle,
                MarkerSize = 5,
                ToolTip = "Time: #VALX\nNet Flow: #VALY{F2} W"
            };

            for (int i = 0; i < historyTime.Count; i++)
            {
                string t = historyTime[i];
                sIn.Points.AddXY(t, historyIn[i]);
                sOut.Points.AddXY(t, historyOut[i]);
                sNet.Points.AddXY(t, historyNet[i]);
            }

            chart.Series.Add(sIn);
            chart.Series.Add(sOut);
            chart.Series.Add(sNet);

            ToggleSeriesVisibility();
        }

        public void AddLivePoint(double pIn, double pOut, double pNet, string timeStr)
        {
            historyIn.Add(pIn);
            historyOut.Add(pOut);
            historyNet.Add(pNet);
            historyTime.Add(timeStr);

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
                if (chart.Series.IndexOf("Power IN") != -1)
                    chart.Series["Power IN"].Enabled = chkIn.Checked;

                if (chart.Series.IndexOf("Power OUT") != -1)
                    chart.Series["Power OUT"].Enabled = chkOut.Checked;

                if (chart.Series.IndexOf("Net Flow") != -1)
                    chart.Series["Net Flow"].Enabled = chkNet.Checked;

                chart.Invalidate();
                chart.Update();
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
                {
                    sw.WriteLine(String.Format("{0},{1:F2},{2:F2},{3:F2}", 
                        historyTime[i], historyIn[i], historyOut[i], historyNet[i]));
                }
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
            {
                LoadCsvFile(files[0]);
            }
        }
    }
}