namespace MusicBeeRemote.Core.Settings.Dialog
{
    using System.Drawing;
    using System.Windows.Forms;

    public class HintTextBox : TextBox
    {
        private string _hint;

        public string Hint
        {
            get => _hint;
            set
            {
                _hint = value;
                Invalidate();
            }
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg != 0xf)
            {
                return;
            }

            var textIsEmpty = string.IsNullOrEmpty(Text);
            var hintNotEmpty = !string.IsNullOrEmpty(Hint);
            if (Focused || !textIsEmpty || !hintNotEmpty)
            {
                return;
            }
            using (var graphics = CreateGraphics())
            {
                TextRenderer.DrawText(graphics, Hint, Font,
                    ClientRectangle, SystemColors.GrayText, BackColor,
                    TextFormatFlags.Top | TextFormatFlags.Left);
            }
        }
    }
}