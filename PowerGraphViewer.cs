using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace PowerGraphViewer
{
    public class Program : Form
    {
        private Chart chart;
        private CheckBox chkIn, chkOut, chkNet;
        private Button btnOpenCsv, btnResetZoom;
        private Label lblFileInfo, lblHoverReadout;

        private List<double> historyIn = new List<double>();
        private List<double> historyOut = new List<double>();
        private List<double> historyNet = new List<double>();
        private List<string> historyTime = new List<string>();
        private string currentFileName = "";

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            string initialFile = args.Length > 0 ? args[0] : null;
            Application.Run(new Program(initialFile));
        }

        public Program(string filePath = null)
        {
            this.Text = "📊 Power Telemetry CSV Graph Viewer";
            this.Size = new Size(1000, 640);
            this.MinimumSize = new Size(750, 480);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(20, 20, 24);
            this.ForeColor = Color.White;
            this.Icon = SystemIcons.Application;
            this.AllowDrop = true;

            this.DragEnter += Form_DragEnter;
            this.DragDrop += Form_DragDrop;

            InitializeControls();

            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                LoadCsv(filePath);
            }
            else
            {
                // Try finding latest CSV in Charging Stats folder
                string defaultFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Charging Stats");
                if (Directory.Exists(defaultFolder))
                {
                    string[] csvFiles = Directory.GetFiles(defaultFolder, "*.csv");
                    if (csvFiles.Length > 0)
                    {
                        Array.Sort(csvFiles);
                        LoadCsv(csvFiles[csvFiles.Length - 1]);
                    }
                }
            }
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
                Text = "📂 Open CSV File...",
                ForeColor = Color.White,
                BackColor = Color.FromArgb(33, 150, 243),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Size = new Size(140, 28),
                Location = new Point(12, 8),
                Cursor = Cursors.Hand
            };
            btnOpenCsv.FlatAppearance.BorderSize = 0;
            btnOpenCsv.Click += (s, e) => BrowseCsv();

            chkIn = new CheckBox {
                Text = "Power IN (W)",
                Checked = true,
                ForeColor = Color.FromArgb(76, 175, 80),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(165, 12),
                Cursor = Cursors.Hand
            };
            chkIn.CheckedChanged += (s, e) => ToggleSeriesVisibility();

            chkOut = new CheckBox {
                Text = "Power OUT (W)",
                Checked = true,
                ForeColor = Color.FromArgb(244, 67, 54),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(295, 12),
                Cursor = Cursors.Hand
            };
            chkOut.CheckedChanged += (s, e) => ToggleSeriesVisibility();

            chkNet = new CheckBox {
                Text = "Net Flow (W)",
                Checked = true,
                ForeColor = Color.FromArgb(33, 150, 243),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(435, 12),
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
                Location = new Point(565, 9),
                Cursor = Cursors.Hand
            };
            btnResetZoom.FlatAppearance.BorderSize = 0;
            btnResetZoom.Click += (s, e) => ResetZoom();

            lblFileInfo = new Label {
                Text = "📄 Drag & drop any PowerData CSV file here",
                ForeColor = Color.FromArgb(180, 180, 190),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(690, 13)
            };

            pnlTop.Controls.AddRange(new Control[] { btnOpenCsv, chkIn, chkOut, chkNet, btnResetZoom, lblFileInfo });
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
            Title title = new Title("CSV Power Telemetry Analysis", Docking.Top, new Font("Segoe UI", 12F, FontStyle.Bold), Color.White);
            chart.Titles.Add(title);

            this.Controls.Add(chart);
            chart.BringToFront();
        }

        private void BrowseCsv()
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
                    LoadCsv(ofd.FileName);
                }
            }
        }

        private void LoadCsv(string csvPath)
        {
            try
            {
                historyIn.Clear();
                historyOut.Clear();
                historyNet.Clear();
                historyTime.Clear();

                string[] lines = File.ReadAllLines(csvPath);
                if (lines.Length <= 1)
                {
                    MessageBox.Show("Selected CSV file is empty or missing data.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                double maxIn = 0, maxOut = 0;

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

                            if (pIn > maxIn) maxIn = pIn;
                            if (pOut > maxOut) maxOut = pOut;
                        }
                    }
                }

                currentFileName = Path.GetFileName(csvPath);
                this.Text = String.Format("📊 {0} - Power Telemetry CSV Graph Viewer", currentFileName);
                lblFileInfo.Text = String.Format("📄 {0}  │  Samples: {1}  │  Peak IN: {2:F1}W  │  Peak OUT: {3:F1}W", 
                    currentFileName, historyTime.Count, maxIn, maxOut);

                if (chart.Titles.Count > 0)
                    chart.Titles[0].Text = "Telemetry Log: " + currentFileName;

                PlotData();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to parse CSV file:\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
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
                LoadCsv(files[0]);
            }
        }
    }
}
