using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Management;
using System.Windows.Forms;

namespace PowerTracker
{
    public class Program : Form
    {
        private Label lblIn, lblOut, lblNet, lblRec;
        private Timer timer;
        private ManagementObjectSearcher searcher;
        private NotifyIcon trayIcon;
        private MenuItem itemRecord;

        private double lastKnownDischargeRate = 12.0;

        // Recording & Telemetry Variables
        private bool isRecording = false;
        private List<double> recHistoryIn = new List<double>();
        private List<double> recHistoryOut = new List<double>();
        private List<double> recHistoryNet = new List<double>();
        private List<string> recHistoryTime = new List<string>();

        // Dragging Variables
        private bool isDragging = false;
        private Point dragCursorPoint;
        private Point dragFormPoint;

        public Program()
        {
            // --- Widget Window Config ---
            this.Text = "Power Widget";
            this.Size = new Size(270, 50);
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

            // REC Badge (Solid when recording)
            lblRec = new Label {
                Text = "🔴 REC",
                ForeColor = Color.Crimson,
                Location = new Point(208, 16),
                AutoSize = true,
                Visible = false,
                Font = new Font("Segoe UI", 7.5F, FontStyle.Bold)
            };
            lblRec.MouseDown += Form_MouseDown;
            lblRec.MouseMove += Form_MouseMove;
            lblRec.MouseUp += Form_MouseUp;

            // --- Context Menu ---
            ContextMenu ctx = new ContextMenu();
            itemRecord = new MenuItem("🔴 Start Recording", (s, e) => ToggleRecording());
            ctx.MenuItems.Add(itemRecord);
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

            this.Controls.AddRange(new Control[] { hIn, hOut, hNet, lblIn, lblOut, lblNet, lblRec });

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
                // Start Session
                recHistoryIn.Clear();
                recHistoryOut.Clear();
                recHistoryNet.Clear();
                recHistoryTime.Clear();
                lblRec.Visible = true;
                itemRecord.Text = "⏹️ Stop & Save Graph";
                trayIcon.ShowBalloonTip(2000, "PowerTracker", "Recording IN, OUT & NET telemetry...", ToolTipIcon.Info);
            }
            else
            {
                // Stop Session & Save
                lblRec.Visible = false;
                itemRecord.Text = "🔴 Start Recording";
                GenerateGraphAndCSV();
            }
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

                    // Record Data Points
                    if (isRecording)
                    {
                        recHistoryIn.Add(pIn);
                        recHistoryOut.Add(pOut);
                        recHistoryNet.Add(pNet);
                        recHistoryTime.Add(DateTime.Now.ToString("HH:mm:ss"));
                        
                        // Keep solid red
                        lblRec.Visible = true;
                    }
                }
            }
            catch { }
        }

        private void GenerateGraphAndCSV()
        {
            if (recHistoryNet.Count < 2) return;

            // Folder Structure: Desktop -> Charging Stats -> Graphs
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string baseFolder = Path.Combine(desktop, "Charging Stats");
            string graphsFolder = Path.Combine(baseFolder, "Graphs");

            Directory.CreateDirectory(baseFolder);
            Directory.CreateDirectory(graphsFolder);

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string graphPath = Path.Combine(graphsFolder, String.Format("PowerGraph_{0}.png", timestamp));
            string csvPath = Path.Combine(baseFolder, String.Format("PowerData_{0}.csv", timestamp));

            // 1. Export CSV (IN, OUT, NET)
            using (StreamWriter sw = new StreamWriter(csvPath))
            {
                sw.WriteLine("Time,PowerIn_W,PowerOut_W,NetFlow_W");
                for (int i = 0; i < recHistoryNet.Count; i++)
                {
                    sw.WriteLine(String.Format("{0},{1:F2},{2:F2},{3:F2}", 
                        recHistoryTime[i], recHistoryIn[i], recHistoryOut[i], recHistoryNet[i]));
                }
            }

            // 2. Render Multi-Line High-Res PNG Graph
            int w = 700, h = 380;
            using (Bitmap bmp = new Bitmap(w, h))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.FromArgb(25, 25, 28));

                // Header Title
                g.DrawString("Power Telemetry Log (Watts)", new Font("Segoe UI", 12, FontStyle.Bold), Brushes.White, 15, 12);

                // Legend (IN = Green, OUT = Red, NET = Blue)
                using (Font legendFont = new Font("Segoe UI", 8.5F, FontStyle.Bold))
                {
                    g.DrawString("■ Power IN", legendFont, new SolidBrush(Color.FromArgb(76, 175, 80)), 380, 15);
                    g.DrawString("■ Power OUT", legendFont, new SolidBrush(Color.FromArgb(244, 67, 54)), 480, 15);
                    g.DrawString("■ Net Flow", legendFont, new SolidBrush(Color.FromArgb(33, 150, 243)), 580, 15);
                }

                // Plotting Grid & Zero Line
                Pen gridPen = new Pen(Color.FromArgb(50, 50, 55), 1);
                g.DrawLine(gridPen, 45, 300, 660, 300); // Axis Baseline

                double minVal = -35.0, maxVal = 70.0;
                float xStep = 615.0f / (recHistoryNet.Count - 1);

                PointF[] ptsIn = new PointF[recHistoryNet.Count];
                PointF[] ptsOut = new PointF[recHistoryNet.Count];
                PointF[] ptsNet = new PointF[recHistoryNet.Count];

                for (int i = 0; i < recHistoryNet.Count; i++)
                {
                    float x = 45 + (i * xStep);
                    
                    ptsIn[i] = new PointF(x, (float)(300 - ((recHistoryIn[i] - minVal) / (maxVal - minVal) * 240)));
                    ptsOut[i] = new PointF(x, (float)(300 - ((recHistoryOut[i] - minVal) / (maxVal - minVal) * 240)));
                    ptsNet[i] = new PointF(x, (float)(300 - ((recHistoryNet[i] - minVal) / (maxVal - minVal) * 240)));
                }

                // Draw Lines for all three metrics
                using (Pen penIn = new Pen(Color.FromArgb(76, 175, 80), 2.2f))
                using (Pen penOut = new Pen(Color.FromArgb(244, 67, 54), 2.2f))
                using (Pen penNet = new Pen(Color.FromArgb(33, 150, 243), 2.2f))
                {
                    g.DrawLines(penIn, ptsIn);
                    g.DrawLines(penOut, ptsOut);
                    g.DrawLines(penNet, ptsNet);
                }

                bmp.Save(graphPath, System.Drawing.Imaging.ImageFormat.Png);
            }

            trayIcon.ShowBalloonTip(3000, "Saved to Charging Stats!", String.Format("Graph saved in Graphs subfolder."), ToolTipIcon.Info);
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
                dragCursorPoint = Cursor.Position;
                dragFormPoint = this.Location;
            }
        }

        private void Form_MouseMove(object sender, MouseEventArgs e)
        {
            if (isDragging)
            {
                Point dif = Point.Subtract(Cursor.Position, new Size(dragCursorPoint));
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
}