using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace Cloud90.GameLink
{
    // Cloud90 Live's existing brand palette and artwork, embedded for offline use.
    internal static class Design
    {
        public static readonly Color Background = ColorTranslator.FromHtml("#0a0a0f");
        public static readonly Color Surface = ColorTranslator.FromHtml("#12121a");
        public static readonly Color Raised = ColorTranslator.FromHtml("#1a1a26");
        public static readonly Color Border = ColorTranslator.FromHtml("#262638");
        public static readonly Color Purple = ColorTranslator.FromHtml("#7c3aed");
        public static readonly Color Pink = ColorTranslator.FromHtml("#ec4899");
        public static readonly Color Cyan = ColorTranslator.FromHtml("#06b6d4");
        public static readonly Color Text = ColorTranslator.FromHtml("#f1f5f9");
        public static readonly Color Muted = ColorTranslator.FromHtml("#94a3b8");
        public static readonly Color Success = ColorTranslator.FromHtml("#10b981");
        static readonly Dictionary<string, Image> Images = new Dictionary<string, Image>();

        public static Image Art(string name)
        {
            if (Images.ContainsKey(name)) return Images[name];
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Art." + name))
            {
                Image art = null;
                if (stream != null) using (Image source = Image.FromStream(stream)) art = new Bitmap(source);
                Images[name] = art;
                return art;
            }
        }
        public static Label Label(string text, int size, Color color)
        {
            return new Label { Text = text, Font = new Font("Segoe UI", size, FontStyle.Regular), ForeColor = color, BackColor = Color.Transparent, AutoEllipsis = true };
        }
        public static void Button(Button button, string text, bool primary)
        {
            button.Text = text;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.BorderColor = Border;
            button.FlatAppearance.MouseOverBackColor = primary ? ColorTranslator.FromHtml("#6d28d9") : ColorTranslator.FromHtml("#262638");
            button.FlatAppearance.MouseDownBackColor = primary ? ColorTranslator.FromHtml("#5b21b6") : ColorTranslator.FromHtml("#323248");
            button.BackColor = primary ? Purple : Raised;
            button.ForeColor = Text;
            button.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            button.Cursor = Cursors.Hand;
            button.UseVisualStyleBackColor = false;
            button.Height = 44;
            button.AccessibleName = text.Replace("&", "");
        }
        public static void Cover(Graphics graphics, Image image, Rectangle target)
        {
            if (image == null || target.Width <= 0 || target.Height <= 0) return;
            float scale = Math.Max((float)target.Width / image.Width, (float)target.Height / image.Height);
            float width = target.Width / scale, height = target.Height / scale;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(image, target, new RectangleF((image.Width - width) / 2, (image.Height - height) / 2, width, height), GraphicsUnit.Pixel);
        }
        public static string Description(string id)
        {
            switch (id)
            {
                case "gtasa": return "Los Santos también juega con tu chat.";
                case "hades": return "Tu chat tiene una cita con el inframundo.";
                case "ctrnf": return "Dale a tu chat un lugar en la carrera.";
                case "cuphead": return "Cada regalo puede cambiar el siguiente intento.";
                case "kof2002": return "Lleva la energía de tu chat al combate.";
                default: return "Tu chat se une a la misión.";
            }
        }
        public static string PlayNote(string id)
        {
            switch (id)
            {
                case "gtasa": return "Edición clásica compatible. Modo individual. F9 pausa los efectos.";
                case "ctrnf": return "Carrera local contra la CPU. Abre el cliente y pulsa Jugar. F9 pausa los efectos.";
                case "cuphead": return "Steam, modo individual. F9 pausa los efectos.";
                case "kof2002": return "MAME, jugador 1 contra la CPU. Los efectos se activan durante la ronda.";
                case "mslug3": return "MAME, modo de 1 jugador. Los efectos se activan durante la partida.";
                default: return "Abre Hades desde aquí para recibir los efectos durante la partida.";
            }
        }
    }

    internal sealed class GameBanner : Panel
    {
        readonly Image art;
        public GameBanner(string id) { art = Design.Art(id + ".jpg"); DoubleBuffered = true; BackColor = Design.Surface; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Rectangle area = ClientRectangle;
            if (area.Width < 2 || area.Height < 2) return;
            int start = area.Width / 3;
            Design.Cover(e.Graphics, art, new Rectangle(start, 0, area.Width - start, area.Height));
            using (var fade = new LinearGradientBrush(area, Design.Surface, Color.Transparent, 0f))
            {
                fade.InterpolationColors = new ColorBlend { Colors = new[] { Design.Surface, Design.Surface, Color.FromArgb(120, Design.Surface), Color.FromArgb(25, Design.Surface) }, Positions = new[] { 0f, 0.34f, 0.65f, 1f } };
                e.Graphics.FillRectangle(fade, area);
            }
            using (var bottom = new LinearGradientBrush(area, Color.Transparent, Color.FromArgb(175, Design.Surface), 90f)) e.Graphics.FillRectangle(bottom, area);
            using (var accent = new LinearGradientBrush(new Rectangle(0, Height - 3, Width, 3), Design.Purple, Design.Cyan, 0f)) e.Graphics.FillRectangle(accent, 0, Height - 3, Width, 3);
        }
    }

    internal sealed class GameNavigationButton : Button
    {
        readonly Image cover;
        readonly string title, subtitle;
        bool selected;
        public bool Selected { get { return selected; } set { selected = value; AccessibleDescription = value ? "Juego seleccionado" : "Seleccionar juego"; Invalidate(); } }
        public GameNavigationButton(string id, string title, string subtitle)
        {
            this.title = title; this.subtitle = subtitle; cover = Design.Art(id + ".jpg");
            Text = title; AccessibleName = title; AccessibleRole = AccessibleRole.PageTab;
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand;
            BackColor = Design.Surface; ForeColor = Design.Text;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Color background = selected ? Color.FromArgb(42, 29, 66) : (ClientRectangle.Contains(PointToClient(MousePosition)) ? Design.Raised : Design.Surface);
            e.Graphics.Clear(background);
            Rectangle coverArea = new Rectangle(12, 10, 48, Height - 20);
            Design.Cover(e.Graphics, cover, coverArea);
            if (selected) using (var accent = new SolidBrush(Design.Purple)) e.Graphics.FillRectangle(accent, 0, 8, 3, Height - 16);
            using (Font titleFont = new Font("Segoe UI", 10, FontStyle.Bold))
            using (Font subFont = new Font("Segoe UI", 9))
            {
                TextRenderer.DrawText(e.Graphics, title, titleFont, new Rectangle(72, 12, Width - 78, 38), Design.Text, TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(e.Graphics, subtitle, subFont, new Rectangle(72, Height - 26, Width - 78, 20), selected ? Color.FromArgb(196, 167, 255) : Design.Muted, TextFormatFlags.EndEllipsis);
            }
            if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(5, 4, Width - 11, Height - 9), Design.Text, background);
        }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Invalidate(); }
    }
}
