using System.Drawing;
using System.Windows.Forms;

namespace ServiceKillerV1.UI
{
    public static class Theme
    {
        public static readonly Color Back = Color.FromArgb(20, 22, 26);
        public static readonly Color Panel = Color.FromArgb(28, 31, 36);
        public static readonly Color Panel2 = Color.FromArgb(35, 39, 45);
        public static readonly Color Border = Color.FromArgb(58, 63, 72);
        public static readonly Color Text = Color.FromArgb(238, 241, 245);
        public static readonly Color Muted = Color.FromArgb(160, 168, 180);
        public static readonly Color Accent = Color.FromArgb(61, 139, 255);
        public static readonly Color SelectedRow = Color.FromArgb(38, 48, 62);
        public static readonly Color DisabledPanel = Color.FromArgb(24, 26, 30);
        public static readonly Color DisabledSelectedRow = Color.FromArgb(31, 36, 43);
        public static readonly Color DisabledText = Color.FromArgb(105, 112, 123);
        public static readonly Color Low = Color.FromArgb(86, 196, 121);
        public static readonly Color Medium = Color.FromArgb(232, 185, 67);
        public static readonly Color High = Color.FromArgb(235, 99, 99);
        public static readonly Color Modified = Color.FromArgb(115, 187, 255);

        public static Font UiFont(float size, FontStyle style)
        {
            return new Font("Segoe UI", size, style, GraphicsUnit.Point);
        }

        public static Button MakeButton(string text, bool primary)
        {
            Button button = new Button();
            button.Text = text;
            button.AutoSize = false;
            button.Height = 36;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.BorderColor = Border;
            button.BackColor = primary ? Accent : Panel2;
            button.ForeColor = Text;
            button.Font = UiFont(11.0f, FontStyle.Bold);
            button.Cursor = Cursors.Hand;
            return button;
        }

        public static Label MakeLabel(string text, float size, bool bold, Color color)
        {
            Label label = new Label();
            label.Text = text;
            label.ForeColor = color;
            label.Font = UiFont(size, bold ? FontStyle.Bold : FontStyle.Regular);
            label.AutoSize = true;
            return label;
        }

        public static void ApplyApplicationIcon(Form form)
        {
            if (form == null) return;
            try
            {
                Icon icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (icon != null) form.Icon = icon;
            }
            catch
            {
                // El icono nunca debe impedir que una ventana se abra.
            }
        }
    }
}
