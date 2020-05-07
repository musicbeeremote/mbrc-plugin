using System;
using System.Reflection;
using System.Windows.Forms;

namespace MusicBeeRemote.Core.Settings.Dialog
{
    public static class CheckBoxExtension
    {
        public static void SetChecked(this CheckBox chBox, bool check)
        {
            if (chBox == null)
            {
                throw new ArgumentNullException(nameof(chBox));
            }

            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var state = check ? CheckState.Checked : CheckState.Unchecked;
            typeof(CheckBox).GetField("checkState", flags)?.SetValue(chBox, state);
            chBox.Invalidate();
        }
    }
}
