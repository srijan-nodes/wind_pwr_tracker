using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Management;
using System.Windows.Forms;

namespace PowerTracker
{
    public class Program : Form
    {
        private Label lblIn, lblOut, lblNet;
        private Timer timer;
        private ManagementObjectSearcher searcher;
        private NotifyIcon trayIcon;

        private double lastKnownDischargeRate = 12.0; // Default baseline estimate when plugged in

        // Dragging Variables
        private bool isDragging = false;
        private Point dragCursorPoint;
        private Point dragFormPoint;

        public Program()
        {
            // --- Widget Window Config ---
            this.Text = "Power Widget";
            this.Size = new Size(220, 50);
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

            // Context Menu
            ContextMenu ctx = new ContextMenu();
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

            this.Controls.AddRange(new Control[] { hIn, hOut, hNet, lblIn, lblOut, lblNet });

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
                        // PLUGGED IN MODE:
                        // Net flow to battery is raw ChargeRate
                        pNet = rawChargeRate > 0 ? rawChargeRate : 0.0;

                        // Track discharge rate if available, otherwise use last known value
                        if (rawDischargeRate > 0)
                        {
                            lastKnownDischargeRate = rawDischargeRate;
                        }

                        pOut = lastKnownDischargeRate;
                        pIn = pNet + pOut; // Total Power Provided by Charger
                    }
                    else
                    {
                        // ON BATTERY MODE:
                        pIn = 0.0;
                        pOut = rawDischargeRate > 0 ? rawDischargeRate : 0.0;
                        pNet = -pOut;
                        
                        if (pOut > 0)
                        {
                            lastKnownDischargeRate = pOut; // Cache for when charger is plugged back in
                        }
                    }

                    string formattedNet = String.Format("{0}{1:F1}W", (pNet > 0 ? "+" : ""), pNet);

                    lblIn.Text = String.Format("{0:F1}W", pIn);
                    lblOut.Text = String.Format("{0:F1}W", pOut);
                    lblNet.Text = formattedNet;
                    lblNet.ForeColor = pNet > 0 ? Color.FromArgb(76, 175, 80) : (pNet < 0 ? Color.FromArgb(244, 67, 54) : Color.Gray);

                    trayIcon.Text = String.Format("Power Net: {0}", formattedNet);
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