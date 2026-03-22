using System.Drawing;
using System.Windows.Forms;

namespace Incantation.UI
{
    public class SlateCopperColorTable : ProfessionalColorTable
    {
        // Toolbar gradient (pale slate top → medium slate bottom)
        public override Color ToolStripGradientBegin { get { return Color.FromArgb(176, 192, 204); } }
        public override Color ToolStripGradientMiddle { get { return Color.FromArgb(148, 170, 186); } }
        public override Color ToolStripGradientEnd { get { return Color.FromArgb(120, 148, 168); } }

        // Menu strip
        public override Color MenuStripGradientBegin { get { return Color.FromArgb(176, 192, 204); } }
        public override Color MenuStripGradientEnd { get { return Color.FromArgb(120, 148, 168); } }

        // Button hover (copper)
        public override Color ButtonSelectedGradientBegin { get { return Color.FromArgb(232, 168, 124); } }
        public override Color ButtonSelectedGradientEnd { get { return Color.FromArgb(212, 132, 90); } }
        public override Color ButtonSelectedBorder { get { return Color.FromArgb(181, 100, 58); } }

        // Button pressed (deeper copper)
        public override Color ButtonPressedGradientBegin { get { return Color.FromArgb(192, 96, 48); } }
        public override Color ButtonPressedGradientEnd { get { return Color.FromArgb(232, 168, 124); } }
        public override Color ButtonPressedBorder { get { return Color.FromArgb(140, 70, 30); } }

        // Button checked
        public override Color ButtonCheckedGradientBegin { get { return Color.FromArgb(232, 168, 124); } }
        public override Color ButtonCheckedGradientEnd { get { return Color.FromArgb(212, 132, 90); } }

        // Menu item hover
        public override Color MenuItemSelected { get { return Color.FromArgb(240, 225, 210); } }
        public override Color MenuItemSelectedGradientBegin { get { return Color.FromArgb(232, 168, 124); } }
        public override Color MenuItemSelectedGradientEnd { get { return Color.FromArgb(212, 132, 90); } }
        public override Color MenuItemBorder { get { return Color.FromArgb(181, 100, 58); } }
        public override Color MenuItemPressedGradientBegin { get { return Color.FromArgb(176, 192, 204); } }
        public override Color MenuItemPressedGradientEnd { get { return Color.FromArgb(120, 148, 168); } }

        // Menu gutter (image margin)
        public override Color ImageMarginGradientBegin { get { return Color.FromArgb(220, 228, 234); } }
        public override Color ImageMarginGradientMiddle { get { return Color.FromArgb(200, 212, 222); } }
        public override Color ImageMarginGradientEnd { get { return Color.FromArgb(176, 192, 204); } }

        // Separators
        public override Color SeparatorDark { get { return Color.FromArgb(74, 96, 112); } }
        public override Color SeparatorLight { get { return Color.FromArgb(176, 192, 204); } }

        // Status strip
        public override Color StatusStripGradientBegin { get { return Color.FromArgb(120, 148, 168); } }
        public override Color StatusStripGradientEnd { get { return Color.FromArgb(176, 192, 204); } }

        // Overflow
        public override Color OverflowButtonGradientBegin { get { return Color.FromArgb(148, 170, 186); } }
        public override Color OverflowButtonGradientMiddle { get { return Color.FromArgb(120, 148, 168); } }
        public override Color OverflowButtonGradientEnd { get { return Color.FromArgb(74, 96, 112); } }

        // Grip
        public override Color GripDark { get { return Color.FromArgb(74, 96, 112); } }
        public override Color GripLight { get { return Color.FromArgb(176, 192, 204); } }

        // Content panel / dropdown
        public override Color ToolStripContentPanelGradientBegin { get { return Color.FromArgb(220, 228, 234); } }
        public override Color ToolStripContentPanelGradientEnd { get { return Color.FromArgb(236, 240, 243); } }
        public override Color ToolStripDropDownBackground { get { return Color.FromArgb(236, 240, 243); } }

        // Check
        public override Color CheckBackground { get { return Color.FromArgb(232, 168, 124); } }
        public override Color CheckSelectedBackground { get { return Color.FromArgb(212, 132, 90); } }
        public override Color CheckPressedBackground { get { return Color.FromArgb(192, 96, 48); } }

        // Rafting container
        public override Color RaftingContainerGradientBegin { get { return Color.FromArgb(176, 192, 204); } }
        public override Color RaftingContainerGradientEnd { get { return Color.FromArgb(120, 148, 168); } }
    }
}
