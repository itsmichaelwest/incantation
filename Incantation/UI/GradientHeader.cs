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
            Color titleColor = Color.FromArgb(30, 30, 30);
            Color timeColor = Color.Gray;

            // Background — active session gets prominent highlight
            if (active)
            {
                using (SolidBrush bg = new SolidBrush(LunaBlueStart))
                {
                    g.FillRectangle(bg, bounds);
                }
                titleColor = Color.White;
                timeColor = Color.FromArgb(190, 205, 230);
            }
            else if (selected)
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

            // Title (line 1)
            int textX = bounds.X + 8;
            using (Font titleFont = new Font("Tahoma", 8.25f, FontStyle.Bold))
            {
                using (SolidBrush brush = new SolidBrush(titleColor))
                {
                    g.DrawString(title, titleFont, brush, textX, bounds.Y + 4);
                }
            }

            // Timestamp (line 2)
            using (Font timeFont = new Font("Tahoma", 7.5f, FontStyle.Regular))
            {
                using (SolidBrush brush = new SolidBrush(timeColor))
                {
                    g.DrawString(timeText, timeFont, brush, textX, bounds.Y + 20);
                }
            }
        }
    }
}
