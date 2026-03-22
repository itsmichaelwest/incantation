using System.Drawing;
using System.Drawing.Drawing2D;

namespace Incantation.UI
{
    public static class GradientHeader
    {
        // Luna Blue palette
        public static readonly Color LunaBlueStart = Color.FromArgb(49, 105, 198);
        public static readonly Color LunaBlueEnd = Color.FromArgb(98, 140, 213);
        public static readonly Color LunaHeaderBg = Color.FromArgb(221, 231, 245);
        public static readonly Color PanelBorder = Color.FromArgb(172, 186, 214);
        public static readonly Color SidebarBg = Color.FromArgb(243, 243, 247);
        public static readonly Color SelectedBg = Color.FromArgb(193, 210, 238);
        public static readonly Color SelectedBorder = Color.FromArgb(49, 105, 198);

        private static Font _headerFont;

        static GradientHeader()
        {
            _headerFont = new Font("Tahoma", 8.25f, FontStyle.Bold);
        }

        public static void PaintHeader(Graphics g, Rectangle bounds, string text)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }
            using (LinearGradientBrush brush = new LinearGradientBrush(
                bounds, LunaBlueStart, LunaBlueEnd, LinearGradientMode.Horizontal))
            {
                g.FillRectangle(brush, bounds);
            }
            g.DrawString(text, _headerFont, Brushes.White, 6, 3);
        }

        public static void PaintSessionItem(Graphics g, Rectangle bounds, string title,
            string timeText, bool selected, bool active)
        {
            // Background
            if (selected)
            {
                using (SolidBrush bg = new SolidBrush(SelectedBg))
                {
                    g.FillRectangle(bg, bounds);
                }
                using (Pen border = new Pen(SelectedBorder))
                {
                    g.DrawRectangle(border, bounds.X, bounds.Y,
                        bounds.Width - 1, bounds.Height - 1);
                }
            }
            else
            {
                using (SolidBrush bg = new SolidBrush(SidebarBg))
                {
                    g.FillRectangle(bg, bounds);
                }
            }

            // Active indicator dot
            if (active)
            {
                using (SolidBrush dot = new SolidBrush(Color.FromArgb(0, 128, 0)))
                {
                    g.FillEllipse(dot, bounds.X + 6, bounds.Y + 8, 8, 8);
                }
            }

            // Title (line 1)
            int textX = active ? bounds.X + 18 : bounds.X + 8;
            Color titleColor = selected ? Color.Black : Color.FromArgb(30, 30, 30);
            using (Font titleFont = new Font("Tahoma", 8.25f, FontStyle.Bold))
            {
                g.DrawString(title, titleFont, new SolidBrush(titleColor),
                    textX, bounds.Y + 4);
            }

            // Timestamp (line 2)
            using (Font timeFont = new Font("Tahoma", 7.5f, FontStyle.Regular))
            {
                g.DrawString(timeText, timeFont, Brushes.Gray,
                    textX, bounds.Y + 20);
            }
        }
    }
}
